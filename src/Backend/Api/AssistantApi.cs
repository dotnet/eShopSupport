using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using eShopSupport.Backend.Data;
using eShopSupport.Backend.Services;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;

namespace eShopSupport.Backend.Api;

public static class AssistantApi
{
    private readonly static JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapAssistantApiEndpoints(this WebApplication app)
    {
        app.MapPost("/api/assistant/chat", GetStreamingChatResponseAsync);
    }

    private static async Task GetStreamingChatResponseAsync(AssistantChatRequest request, HttpContext httpContext, AppDbContext dbContext, IChatCompletionService chatService, ProductManualSemanticSearch manualSearch, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var product = request.ProductId.HasValue
            ? await dbContext.Products.FindAsync(request.ProductId.Value)
            : null;

        var chatHistory = new ChatHistory($$"""
            You are a helpful AI assistant called 'Assistant' who helps customer service agents working for AdventureWorks, an online retailer.
            The customer service agent is currently handling the following ticket:
                
            <product_id>{{request.ProductId}}</product_id>
            <product_name>{{product?.Model ?? "None specified"}}</product_name>
            <customer_name>{{request.CustomerName}}</customer_name>
            <summary>{{request.TicketSummary}}</summary>

            The most recent message from the customer is this:
            <customer_message>{{request.TicketLastCustomerMessage}}</customer_message>
            However, that is only provided for context. You are not answering that question directly. The real question is provided below.

            The customer service agent may ask you for help either directly with the customer request, or other general information about this product or our other products. Respond to the AGENT'S question, not specifically to the customer ticket.
            If you need more information, you can search the product manual using "searchParams" as described below.

            Use the context to reply in the following JSON format:

            { "gotEnoughInfoAleady": trueOrFalse, ... }

            If this is a question about the product, you should ALWAYS set gotEnoughInfoAleady to false and search the manual.

            If the context provides information, use it to add an answer like this: { "gotEnoughInfoAlready": true, "answer": string, "mostRelevantSearchResultId": number, "mostRelevantSearchQuote": string, "isAddressedToCustomerByName": trueOrFalse }
            You must justify your answer by providing mostRelevantSearchResultId that supports your info, and mostRelevantSearchQuote (which is a short EXACT word-for-word quote from the most relevant search result, excluding headings).

            If you are asked to write a suggested reply to the customer, set isAddressedToCustomerByName to true and address your
            answer DIRECTLY to the customer by name (e.g., begin "Dear [name]...", and sign off as "AdventureWorks Support").
            Always use paragraph breaks to improve readability.

            If you don't already have enough information, add a suggested search term to use like this: { "gotEnoughInfoAlready": false, "searchProductId": numberOrNull, "searchPhrase": "a phrase to look for in the manual" }.
            That will search the product manual for the specified product, so you don't have to restate the name in the searchPhrase.                
            If the question needs information from ALL product manuals (not just the one for this product), then set searchProductId to null.

            Remember that you are only answering the support agent's question to you. You are not answering the customer's question directly.
            Here is the real support agent's question for you to answer:
            """);

        chatHistory.AddRange(request.Messages.Select(m => new ChatMessageContent(m.IsAssistant ? AuthorRole.Assistant : AuthorRole.User, m.Text)));

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ResponseFormat = "json_object",
            Seed = 0,
        };

        var maxIterations = 3; // On each iteration, it's allowed to call a tool or to return an answer

        try
        {
            // The response will be a JSON array like [{}, {}, ...]
            await httpContext.Response.WriteAsync("[");

            for (var iteration = 0; iteration < maxIterations; iteration++)
            {
                if (iteration == maxIterations - 1)
                {
                    chatHistory.AddMessage(AuthorRole.System,
                        $"""
                        Please note that \"searchPhrase\" is no longer available. Your next reply *MUST* state your answer, even if you are simply saying you do not have an answer.
                        """);
                }

                // Call the chat completion service and stream its output to the HTTP response
                var captured = string.Empty;
                await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, cancellationToken: cancellationToken))
                {
                    // TODO: Force it to stop as soon as the top-level JSON object is closed, otherwise it will emit a long
                    // sequence of trailing whitespace: https://github.com/ollama/ollama/issues/2623
                    var chunkString = chunk.ToString();
                    await httpContext.Response.WriteAsync(chunkString);
                    captured += chunkString;
                }
                await httpContext.Response.WriteAsync(", ");

                // If it's not trying to call a tool, we're finished
                if (!TryParseReply(captured, out var assistantReply) || string.IsNullOrWhiteSpace(assistantReply.SearchPhrase))
                {
                    await httpContext.Response.WriteAsync("{}]");
                    return;
                }

                // It is trying to call a tool, so do that before the next iteration
                var searchResults = await manualSearch.SearchAsync(assistantReply.SearchProductId, assistantReply.SearchPhrase);
                chatHistory.AddMessage(AuthorRole.System, $"""
                        The assistant performed a search with term "{assistantReply.SearchPhrase}" on the user manual,
                        which returned the following results:
                        {string.Join("\n", searchResults.Select(r => $"<search_result productId=\"{GetProductId(r)}\" resultId=\"{r.Metadata.Id}\">{r.Metadata.Text}</search_result>"))}
                        """);

                // Also emit the search result data to the response so the UI has all the metadata
                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    SearchResults = searchResults.Select(r => new
                    {
                        r.Metadata.Id,
                        ProductId = GetProductId(r),
                        PageNumber = GetPageNumber(r),
                    })
                }, _jsonOptions));
                await httpContext.Response.WriteAsync(", ");
            }

            // Since we ran out of iterations and still didn't provide an answer, give up
            await httpContext.Response.WriteAsync("{ \"answer\": \"Sorry, I couldn't find any answer to that.\" }]");
        }
        catch (Exception ex)
        {
            // We don't want to return the raw exception to the response as it would then show up in the chat UI
            var logger = loggerFactory.CreateLogger(typeof(AssistantApi).FullName!);
            logger.LogError(ex, "Error during chat completion.");
            await httpContext.Response.WriteAsync("{ \"answer\": \"Sorry, there was a problem. Please try again.\" }]");
        }
    }

    private static int? GetProductId(MemoryQueryResult result)
    {
        var match = Regex.Match(result.Metadata.ExternalSourceName, @"productid:(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    private static int? GetPageNumber(MemoryQueryResult result)
    {
        var match = Regex.Match(result.Metadata.AdditionalMetadata, @"pagenumber:(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    private static bool TryParseReply(string reply, [NotNullWhen(true)] out AssistantReply? assistantReply)
    {
        try
        {
            assistantReply = JsonSerializer.Deserialize<AssistantReply>(reply, _jsonOptions)!;
            return true;
        }
        catch
        {
            assistantReply = null;
            return false;
        }
    }

    public class AssistantReply
    {
        public string? Answer { get; set; }
        public int? SearchProductId { get; set; }
        public string? SearchPhrase { get; set; }
    }
}
