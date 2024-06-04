using System.Text;
using System.Text.Json;
using eShopSupport.Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using StackExchange.Redis;

namespace eShopSupport.Backend.Services;

public class TicketSummarizer(IServiceScopeFactory scopeFactory)
{
    private static JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public void UpdateSummary(int ticketId)
    {
        _ = UpdateSummaryAsync(ticketId);
    }

    private async Task UpdateSummaryAsync(int ticketId)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TicketSummarizer>>();

        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var redisConnection = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
            var chatCompletion = scope.ServiceProvider.GetRequiredService<IChatCompletionService>();

            var ticket = await db.Tickets
                .Include(t => t.Product)
                .Include(t => t.Messages)
                .FirstOrDefaultAsync(t => t.TicketId == ticketId);
            if (ticket is not null)
            {
                // Score with words, not numbers, as LLMs are much better with words
                string[] satisfactionScores = ["AbsolutelyFurious", "VeryUnhappy", "Unhappy", "Disappointed", "Indifferent", "Pleased", "Happy", "Delighted", "UnspeakablyThrilled"];

                var product = ticket.Product;
                var prompt = $$"""
                    You are part of a customer support ticketing system.
                    Your job is to write brief summaries of customer support interactions. This is to help support agents
                    understand the context quickly so they can help the customer efficiently.

                    Here are details of a support ticket:
                    Product: {{product?.Model ?? "Not specified"}}
                    Brand: {{product?.Brand ?? "Not specified"}}

                    The message log so far is:
                    {{FormatMessagesForPrompt(ticket.Messages)}}

                    Write these summaries for customer support agents:

                    1. A longer summary that is up to 30 words long, condensing as much distinctive information
                       as possible. Do NOT repeat the customer or product name, since this is known anyway.
                       Try to include what SPECIFIC questions/info were given, not just stating in general that questions/info were given.
                       Always cite specifics of the questions or answers. For example, if there is pending question, summarize it in a few words.
                       FOCUS ON THE CURRENT STATUS AND WHAT KIND OF RESPONSE (IF ANY) WOULD BE MOST USEFUL FROM THE NEXT SUPPORT AGENT.

                    2. Condense that into a shorter summary up to 8 words long, which functions as a title for the ticket,
                       so the goal is to distinguish what's unique about this ticket.

                    3. A customerSatisfaction score using one of the following phrases ranked from worst to best:
                       {{string.Join(", ", satisfactionScores)}}.
                       Focus on the CUSTOMER'S messages (not support agent messages) to determine their satisfaction level.

                    Respond as JSON in the following form: {
                      "longSummary": "string",
                      "shortSummary": "string",
                      "customerSatisfaction": "string"
                    }
                    """;

                var chatHistory = new ChatHistory();
                chatHistory.AddUserMessage(prompt);
                var response = await chatCompletion.GetChatMessageContentAsync(chatHistory, new OpenAIPromptExecutionSettings
                {
                    ResponseFormat = "json_object",
                    Seed = 0,
                });

                // Due to what seems like a server-side bug, when asking for a json_object response and with tools enabled,
                // it often replies with two or more JSON objects concatenated together (duplicates or slight variations).
                // As a workaround, just read the first complete JSON object from the response.
                var responseString = response.ToString();
                var parsed = ReadAndDeserializeSingleValue<Response>(responseString, SerializerOptions)!;

                var shortSummary = parsed.ShortSummary;
                var longSummary = parsed.LongSummary;
                int? satisfactionScore = Array.IndexOf(satisfactionScores, parsed.CustomerSatisfaction ?? string.Empty);
                if (satisfactionScore < 0)
                {
                    satisfactionScore = null;
                }

                await db.Tickets.Where(t => t.TicketId == ticketId).ExecuteUpdateAsync(t => t
                    .SetProperty(t => t.ShortSummary, shortSummary)
                    .SetProperty(t => t.LongSummary, longSummary)
                    .SetProperty(t => t.CustomerSatisfaction, satisfactionScore));

                await redisConnection.GetSubscriber().PublishAsync(
                    RedisChannel.Literal($"ticket:{ticketId}"), "Updated");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during summarization");
        }
    }

    private static string FormatMessagesForPrompt(IReadOnlyList<Message> messages)
    {
        var sb = new StringBuilder();
        foreach (var message in messages)
        {
            sb.AppendLine($"<message role=\"{(message.IsCustomerMessage ? "customer" : "support")}\">{message.Text}</message>");
        }
        return sb.ToString();
    }

    private static TResponse? ReadAndDeserializeSingleValue<TResponse>(string json, JsonSerializerOptions options)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json).AsSpan());
        return JsonSerializer.Deserialize<TResponse>(ref reader, options);
    }

    private class Response
    {
        public required string LongSummary { get; set; }
        public required string ShortSummary { get; set; }
        public string? CustomerSatisfaction { get; set; }
    }
}
