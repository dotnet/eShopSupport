using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using eShopSupport.Backend.Data;
using Experimental.AI.LanguageModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Npgsql.Internal;
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
            var chatCompletion = scope.ServiceProvider.GetRequiredService<ChatService>();

            var ticket = await db.Tickets
                .Include(t => t.Product)
                .Include(t => t.Messages)
                .FirstOrDefaultAsync(t => t.TicketId == ticketId);
            if (ticket is not null)
            {
                // The reason for prompting to express satisfation in words rather than numerically, and forcing it to generate a summary
                // of the customer's words before doing so, are necessary prompt engineering techniques. If it's asked to generate sentiment
                // score without first summarizing the customer's words, then it scores the agent's response even when told not to. If it's
                // asked to score numerically, it produces wildly random scores - it's much better with words than numbers.
                string[] satisfactionScores = ["AbsolutelyFurious", "VeryUnhappy", "Unhappy", "Disappointed", "Indifferent", "Pleased", "Happy", "Delighted", "UnspeakablyThrilled"];

                var product = ticket.Product;
                var prompt = $$"""
                    You are part of a customer support ticketing system.
                    Your job is to write brief summaries of customer support interactions. This is to help support agents
                    understand the context quickly so they can help the customer efficiently.

                    Here are details of a support ticket.

                    Product: {{product?.Model ?? "Not specified"}}
                    Brand: {{product?.Brand ?? "Not specified"}}

                    The message log so far is:

                    {{FormatMessagesForPrompt(ticket.Messages)}}

                    Write these summaries:

                    1. A longer summary that is up to 30 words long, condensing as much distinctive information
                       as possible. Do NOT repeat the customer or product name, since this is known anyway.
                       Try to include what SPECIFIC questions/info were given, not just stating in general that questions/info were given.
                       Always cite specifics of the questions or answers. For example, if there is pending question, summarize it in a few words.
                       FOCUS ON THE CURRENT STATUS AND WHAT KIND OF RESPONSE (IF ANY) WOULD BE MOST USEFUL FROM THE NEXT SUPPORT AGENT.

                    2. A shorter summary that is up to 8 words long. This functions as a title for the ticket,
                       so the goal is to distinguish what's unique about this ticket.

                    3. A 10-word summary of the latest thing the CUSTOMER has said, ignoring any agent messages. Then, based
                       ONLY on tenWordsSummarizingOnlyWhatCustomerSaid, score the customer's satisfaction using one of the following
                       phrases ranked from worst to best:
                       {{string.Join(", ", satisfactionScores)}}.
                       Pay particular attention to the TONE of the customer's messages, as we are most interested in their emotional state.

                    Both summaries will only be seen by customer support agents.

                    Respond as JSON in the following form: {
                      "longSummary": "string",
                      "shortSummary": "string",
                      "tenWordsSummarizingOnlyWhatCustomerSaid": "string",
                      "customerSatisfaction": "string"
                    }
                    """;

                var chatHistory = new List<ChatMessage>
                {
                    new (ChatMessageRole.User, prompt)
                };

                var response = (await chatCompletion.ChatAsync(chatHistory, new ChatOptions
                {
                    ResponseFormat = ChatResponseFormat.JsonObject,
                    Seed = 0,
                })).First();

                // Due to what seems like a server-side bug, when asking for a json_object response and with tools enabled,
                // it often replies with two or more JSON objects concatenated together (duplicates or slight variations).
                // As a workaround, just read the first complete JSON object from the response.
                var responseString = response.Content!;
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
