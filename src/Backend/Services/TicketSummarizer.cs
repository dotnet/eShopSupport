using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using eShopSupport.Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using StackExchange.Redis;

namespace eShopSupport.Backend.Services;

public class TicketSummarizer(IServiceScopeFactory scopeFactory)
{
    private static JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    // Because this LLM call can be triggered by external end-user actions, it's helpful to impose a rate limit
    // to prevent resource consumption abuse. If the rate limit is exceeded, we'll simply not compute updated summaries
    // for a while, but everything else will continue to work. In a real application, also consider:
    // - Adjusting the parameters based on your traffic and usage patterns
    // - Scoping the rate limit to be per-user
    private static TokenBucketRateLimiter RateLimiter = new(new()
    {
        // With these settings, we're limited to generating one summary every 2 seconds as a long-run average, but
        // can burst to up to 100 summaries in a short period if it's been several minutes since the last one.
        AutoReplenishment = true,
        TokenLimit = 100,
        ReplenishmentPeriod = TimeSpan.FromSeconds(10),
        TokensPerPeriod = 5,
    });

    public void UpdateSummary(int ticketId, bool enforceRateLimit)
    {
        if (enforceRateLimit)
        {
            using var lease = RateLimiter.AttemptAcquire();
            if (lease.IsAcquired)
            {
                _ = UpdateSummaryAsync(ticketId);
            }
        }
        else
        {
            _ = UpdateSummaryAsync(ticketId);
        }
    }

    private async Task UpdateSummaryAsync(int ticketId)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TicketSummarizer>>();

        try
        {
            var chatCompletion = scope.ServiceProvider.GetRequiredService<IChatCompletionService>();

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ticket = await db.Tickets
                .Include(t => t.Product)
                .Include(t => t.Messages)
                .FirstOrDefaultAsync(t => t.TicketId == ticketId);
            if (ticket is not null)
            {
                string[] satisfactionScores = ["AbsolutelyFurious", "VeryUnhappy", "Unhappy",
                    "Disappointed", "Indifferent", "Pleased", "Happy", "Delighted", "UnspeakablyThrilled"];

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

                    3. Based ONLY on what the customer said (not the support agent), score the customer's satisfaction using one of the following
                       phrases ranked from worst to best:
                       {{string.Join(", ", satisfactionScores)}}.
                       Pay particular attention to the TONE of the customer's messages, as we are most interested in their emotional state.

                    Both summaries will only be seen by customer support agents.

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

                // Issue notification
                var redisConnection = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
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
