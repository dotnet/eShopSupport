using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using eShopSupport.Backend.Data;
using eShopSupport.Backend.Services;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Experimental.AI.LanguageModels;
using Microsoft.SemanticKernel.Memory;

namespace eShopSupport.Backend.Api;

public static class AssistantApi
{
    private readonly static JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapAssistantApiEndpoints(this WebApplication app)
    {
        app.MapPost("/api/assistant/chat", GetStreamingChatResponseAsync);
    }

    private static async Task GetStreamingChatResponseAsync(AssistantChatRequest request, HttpContext httpContext, AppDbContext dbContext, IChatService chatService, ProductManualSemanticSearch manualSearch, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        await httpContext.Response.WriteAsync("[null");

        var product = request.ProductId.HasValue
            ? await dbContext.Products.FindAsync(request.ProductId.Value)
            : null;

        var chatHistory = new List<ChatMessage>();

        chatHistory.Add(new ChatMessage(ChatMessageRole.System, $$"""
            You are a helpful AI assistant called 'Assistant' whose job is to help customer service agents working for AdventureWorks, an online retailer.
            The customer service agent is currently handling the following ticket:
                
            <product_id>{{request.ProductId}}</product_id>
            <product_name>{{product?.Model ?? "None specified"}}</product_name>
            <customer_name>{{request.CustomerName}}</customer_name>
            <summary>{{request.TicketSummary}}</summary>

            The most recent message from the customer is this:
            <customer_message>{{request.TicketLastCustomerMessage}}</customer_message>
            However, that is only provided for context. You are not answering that question directly. The real question will be asked by the user below.
            """));

        chatHistory.AddRange(request.Messages.Select(m => new ChatMessage(m.IsAssistant ? ChatMessageRole.Assistant : ChatMessageRole.User, m.Text)));

        /*
        chatHistory.Add(new ChatMessage(ChatMessageRole.System, $$"""
            Your goal is to answer the agent's FINAL question. If relevant, use the provided tools to help you find the answer.

            If this is a question about the product, ALWAYS search the product manual.

            ALWAYS justify your answer by citing a search result. Do this by including this syntax in your reply:
            <cite searchResultId=number>shortVerbatimQuote</cite>
            shortVerbatimQuote must be a very short, EXACT quote (max 10 words) from whichever search result you are citing.
            Only give one citation per answer. Always give a citation because this is important to the business.
            """));
        */

        var options = new ChatOptions { Seed = 0, Temperature = 0 };

        var searchManualTool = chatService.CreateChatFunction("searchManual", "Searches the specified product manual, or all product manuals, to find information about a given phrase.",
            async (
                [Description("A phrase to use when searching the manual")] string searchPhrase,
                [Description("ID for the product whose manual to search")] int productId) =>
            {
                // If you're going to support parallel tool calls, be sure to do some locking or similar around these HTTP response writes.
                await httpContext.Response.WriteAsync(",\n");
                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(AssistantChatReplyItemType.Search, searchPhrase)));

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

                return searchResults.Select(r => new
                {
                    ProductId = GetProductId(r),
                    SearchResultId = r.Metadata.Id,
                    r.Metadata.Text,
                });
            });

        var executionSettings = new ChatOptions { Seed = 0, Temperature = 0, Tools = [searchManualTool] };
        var answerBuilder = new StringBuilder();
        await foreach (var chunk in chatService.CompleteChatStreamingAsync(chatHistory, executionSettings, cancellationToken: cancellationToken))
        {
            await httpContext.Response.WriteAsync(",\n");
            await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(AssistantChatReplyItemType.AnswerChunk, chunk.Content)));
            answerBuilder.Append(chunk.Content);
        }

        chatHistory.Add(new ChatMessage(ChatMessageRole.Assistant, answerBuilder.ToString()));

        chatHistory.Add(new ChatMessage(ChatMessageRole.System, """
            Consider the answer you just gave and decide whether it is addressed to the customer by name as a reply to them.
            Reply as a JSON object in this form: { "isAddressedByNameToCustomer": trueOrFalse }.
            """));
        executionSettings.ResponseFormat = ChatResponseFormat.JsonObject;
        var isAddressedToCustomer = await chatService.CompleteChatAsync(chatHistory, executionSettings, cancellationToken: cancellationToken);
        try
        {
            var isAddressedToCustomerJson = JsonSerializer.Deserialize<IsAddressedToCustomerReply>(isAddressedToCustomer.First().Content, _jsonOptions)!;
            if (isAddressedToCustomerJson.IsAddressedByNameToCustomer)
            {
                await httpContext.Response.WriteAsync(",\n");
                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(AssistantChatReplyItemType.IsAddressedToCustomer, "true")));
            }
        }
        catch { }

        await httpContext.Response.WriteAsync("]");
    }

    class IsAddressedToCustomerReply
    {
        public bool IsAddressedByNameToCustomer { get; set; }
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
