using System.Text;
using System.Text.Json;
using eShopSupport.Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using StackExchange.Redis;

namespace eShopSupport.Backend.Services;

public class TicketSummarizer(
    AppDbContext dbContext,
    IConnectionMultiplexer redisConnection,
    IChatCompletionService chatCompletion)
{
    private static JsonSerializerOptions SerializerOptions= new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task UpdateSummaryAsync(int ticketId)
    {
        // Load the data we want to summarize
        var ticket = (await dbContext.Tickets
            .Include(t => t.Product)
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.TicketId == ticketId))!;

        // Score with words, not numbers, as LLMs are much better with words
        string[] satisfactionScores = ["AbsolutelyFurious", "VeryUnhappy", "Unhappy",
            "Disappointed", "Indifferent", "Pleased", "Happy", "Delighted", "UnspeakablyThrilled"];

        var prompt = $$"""
            You are part of a customer support ticketing system.
            Your job is to write brief summaries of customer support interactions. This is to help support agents
            understand the context quickly so they can help the customer efficiently.

            Here are details of a support ticket:
            Product: {{ticket.Product?.Model ?? "Not specified"}}
            Brand: {{ticket.Product?.Brand ?? "Not specified"}}

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

        // Invoke the LLM backend and parse the response as JSON
        var responseMessage = await chatCompletion.GetChatMessageContentAsync(new ChatHistory(prompt), new OpenAIPromptExecutionSettings
        {
            ResponseFormat = "json_object",
            Seed = 0,
        });
        var response = ParseJson<Response>(responseMessage);

        // Map the satisfaction label to a numerical score
        int? satisfactionScore = Array.IndexOf(satisfactionScores, response.CustomerSatisfaction ?? string.Empty);
        if (satisfactionScore < 0)
        {
            satisfactionScore = null;
        }

        // Write the changes to the database
        await dbContext.Tickets.Where(t => t.TicketId == ticketId).ExecuteUpdateAsync(t => t
            .SetProperty(t => t.ShortSummary, response.ShortSummary)
            .SetProperty(t => t.LongSummary, response.LongSummary)
            .SetProperty(t => t.CustomerSatisfaction, satisfactionScore));

        // Notify any subscribers so we can update the UI in realtime
        await redisConnection.GetSubscriber().PublishAsync(
            RedisChannel.Literal($"ticket:{ticketId}"), "Updated");
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

    private static TResponse ParseJson<TResponse>(ChatMessageContent message)
    {
        // We need to read only the *first* object in the response, as the LLM may return multiple objects
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(message.ToString()).AsSpan());
        return JsonSerializer.Deserialize<TResponse>(ref reader, SerializerOptions)!;
    }

    private class Response
    {
        public required string LongSummary { get; set; }
        public required string ShortSummary { get; set; }
        public string? CustomerSatisfaction { get; set; }
    }
}
