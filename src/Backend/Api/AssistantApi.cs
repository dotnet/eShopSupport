using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using eShopSupport.Backend.Data;
using eShopSupport.Backend.Services;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.Memory;

namespace eShopSupport.Backend.Api;

public static class AssistantApi
{
    public static void MapAssistantApiEndpoints(this WebApplication app)
    {
        app.MapPost("/api/assistant/chat", GetStreamingChatResponseAsync);
    }

    private static async Task GetStreamingChatResponseAsync(AssistantChatRequest request, HttpContext httpContext, AppDbContext dbContext, IChatClient chatClient, ProductManualSemanticSearch manualSearch, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var product = request.ProductId.HasValue
            ? await dbContext.Products.FindAsync(request.ProductId.Value)
            : null;

        // Build the prompt plus any existing conversation history
        var messages = new List<ChatMessage>([ new(ChatRole.System, $$"""
            You are a helpful AI assistant called 'Assistant' whose job is to help customer service agents working for AdventureWorks, an online retailer.
            The customer service agent is currently handling the following ticket:

            <product_id>{{request.ProductId}}</product_id>
            <product_name>{{product?.Model ?? "None specified"}}</product_name>
            <customer_name>{{request.CustomerName}}</customer_name>
            <summary>{{request.TicketSummary}}</summary>

            The most recent message from the customer is this:
            <customer_message>{{request.TicketLastCustomerMessage}}</customer_message>
            However, that is only provided for context. You are not answering that question directly. The real question will be asked by the user below.

            If this is a question about the product, ALWAYS search the product manual.

            ALWAYS justify your answer by citing a search result. Do this by including this syntax in your reply:
            <cite searchResultId=number>shortVerbatimQuote</cite>
            shortVerbatimQuote must be a very short, EXACT quote (max 10 words) from whichever search result you are citing.
            Only give one citation per answer. Always give a citation because this is important to the business.
            """) ]);

        messages.AddRange(request.Messages.Select(m => new ChatMessage(m.IsAssistant ? ChatRole.Assistant : ChatRole.User, m.Text)));
        await httpContext.Response.WriteAsync("[null");

        // Call the LLM backend
        var searchManual = AIFunctionFactory.Create(new SearchManualContext(httpContext, manualSearch).SearchManual);
        var executionSettings = new ChatOptions
        {
            Temperature = 0,
            Tools = [searchManual],
            AdditionalProperties = new() { ["seed"] = 0 },
        };
        var streamingAnswer = chatClient.CompleteStreamingAsync(messages, executionSettings, cancellationToken);

        // Stream the response to the UI
        var answerBuilder = new StringBuilder();
        await foreach (var chunk in streamingAnswer)
        {
            await httpContext.Response.WriteAsync(",\n");
            await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(AssistantChatReplyItemType.AnswerChunk, chunk.ToString())));
            answerBuilder.Append(chunk.ToString());
        }

        // Ask if this answer is suitable for sending directly to the customer
        // If so, we'll show a button in the UI
        var classification = await chatClient.CompleteAsync<MessageClassification>(
            $"Determine whether the following message is phrased as a reply to the customer {request.CustomerName} by name: {answerBuilder}",
            cancellationToken: cancellationToken);
        if (classification.TryGetResult(out var result) && result.IsAddressedToCustomerByName)
        {
            await httpContext.Response.WriteAsync(",\n");
            await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(AssistantChatReplyItemType.IsAddressedToCustomer, "true")));
        }

        // Signal to the UI that we're finished
        await httpContext.Response.WriteAsync("]");
    }

    class MessageClassification
    {
        public bool IsAddressedToCustomerByName { get; set; }
    }

    private class SearchManualContext(HttpContext httpContext, ProductManualSemanticSearch manualSearch)
    {
        public async Task<object> SearchManual(
            [Description("A phrase to use when searching the manual")] string searchPhrase,
            [Description("ID for the product whose manual to search. Set to null only if you must search across all product manuals.")] int? productId)
        {
            // Notify the UI we're doing a search
            await httpContext.Response.WriteAsync(",\n");
            await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(AssistantChatReplyItemType.Search, searchPhrase)));

            // Do the search, and supply the results to the UI so it can show one as a citation link
            var searchResults = await manualSearch.SearchAsync(productId, searchPhrase);
            foreach (var r in searchResults)
            {
                await httpContext.Response.WriteAsync(",\n");
                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(
                    AssistantChatReplyItemType.SearchResult,
                    string.Empty,
                    int.Parse(r.Metadata.Id),
                    GetProductId(r),
                    GetPageNumber(r))));
            }

            // Return the search results to the assistant
            return searchResults.Select(r => new
            {
                ProductId = GetProductId(r),
                SearchResultId = r.Metadata.Id,
                r.Metadata.Text,
            });
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
}
