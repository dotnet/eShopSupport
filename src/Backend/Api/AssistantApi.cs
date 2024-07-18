using System.ComponentModel;
using System.Text;
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
        await httpContext.Response.WriteAsync("[null");

        var product = request.ProductId.HasValue
            ? await dbContext.Products.FindAsync(request.ProductId.Value)
            : null;

        var chatHistory = new ChatHistory();

        chatHistory.AddSystemMessage($$"""
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
            """);

        chatHistory.AddRange(request.Messages.Select(m => new ChatMessageContent(m.IsAssistant ? AuthorRole.Assistant : AuthorRole.User, m.Text)));

        var kernel = new Kernel();
        kernel.ImportPluginFromObject(new SearchManualPlugin(httpContext, manualSearch));

        var executionSettings = new OpenAIPromptExecutionSettings { Seed = 0, Temperature = 0, ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions };
        var answerBuilder = new StringBuilder();
        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken: cancellationToken))
        {
            await httpContext.Response.WriteAsync(",\n");
            await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(AssistantChatReplyItemType.AnswerChunk, chunk.ToString())));
            answerBuilder.Append(chunk.ToString());
        }

        chatHistory.AddAssistantMessage(answerBuilder.ToString());

        chatHistory.AddSystemMessage("""
            Consider the answer you just gave and decide whether it is addressed to the customer by name as a reply to them.
            Reply as a JSON object in this form: { "isAddressedByNameToCustomer": trueOrFalse }.
            """);
        executionSettings.ResponseFormat = "json_object";
        var isAddressedToCustomer = await chatService.GetChatMessageContentAsync(chatHistory, executionSettings, cancellationToken: cancellationToken);
        try
        {
            var isAddressedToCustomerJson = JsonSerializer.Deserialize<IsAddressedToCustomerReply>(isAddressedToCustomer.ToString(), _jsonOptions)!;
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

    private class SearchManualPlugin(HttpContext httpContext, ProductManualSemanticSearch manualSearch)
    {
        [KernelFunction]
        public async Task<object> SearchManual(
            [Description("A phrase to use when searching the manual")] string searchPhrase,
            [Description("ID for the product whose manual to search. Set to null only if you must search across all product manuals.")] int? productId)
        {
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
