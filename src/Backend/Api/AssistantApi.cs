using System.Diagnostics.CodeAnalysis;
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

        var chatHistoryCopy = new List<ChatMessage>(chatHistory);
        var toolOutputs = await RunRetrievalLoopUntilReadyToAnswer(httpContext.Response, chatService, manualSearch, chatHistoryCopy, cancellationToken);
        chatHistory.AddRange(toolOutputs);

        chatHistory.Add(new ChatMessage(ChatMessageRole.System, "Based on this context, provide an answer to the user's question."));

        if (toolOutputs.Any())
        {
            chatHistory.Add(new ChatMessage(ChatMessageRole.System, $$"""
            ALWAYS justify your answer by citing the most relevant one of the above search results. Do this by including this syntax in your reply:
            <cite searchResultId=number>shortVerbatimQuote</cite>
            shortVerbatimQuote must be a very short, EXACT quote (max 10 words) from whichever search result you are citing.
            Only give one citation per answer. Always give a citation because this is important to the business.
            """));
        }

        var executionSettings = new ChatOptions { Seed = 0, Temperature = 0 };
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

    private static async Task<IReadOnlyList<ChatMessage>> RunRetrievalLoopUntilReadyToAnswer(HttpResponse httpResponse, IChatService chatService, ProductManualSemanticSearch manualSearch, List<ChatMessage> chatHistory, CancellationToken cancellationToken)
    {
        chatHistory.Add(new ChatMessage(ChatMessageRole.System, $$"""
            Your goal is to decide how the agent's FINAL question can best be processed. Do not reply to the agent's question directly,
            but instead return a JSON object describing how to proceed. Here are your possible choices:

            1. If more information from a single product manual would be needed to answer the agent's question, reply
              { "needMoreInfo": true, "searchProductId": number, "searchPhrase": string }.
            2. If more information from ALL product manuals would be needed to answer the agent's question, reply
              { "needMoreInfo": true, "searchPhrase": string }.
            3. If the context already gives enough information to answer the agent's question, reply
              { "needMoreInfo": false } but DO NOT ACTUALLY ANSWER THE QUESTION. Your response must NOT have any other information than this single boolean value.

            If this is a question about the product, ALWAYS set needMoreInfo to true and search the product manual.
            """));

        var toolOutputs = new List<ChatMessage>();
        for (var iteration = 0; iteration < 3; iteration++)
        {
            var action = await GetNextAction(chatService, chatHistory, cancellationToken);
            if (string.IsNullOrWhiteSpace(action?.SearchPhrase))
            {
                break;
            }

            await httpResponse.WriteAsync(",\n");
            await httpResponse.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(AssistantChatReplyItemType.Search, action.SearchPhrase)));

            var searchResults = await manualSearch.SearchAsync(action.SearchProductId, action.SearchPhrase);
            var toolOutputMessage = new ChatMessage(ChatMessageRole.System, $"""
                The assistant performed a search with term "{action.SearchPhrase}" on the user manual,
                which returned the following results:
                {string.Join("\n", searchResults.Select(r => $"<search_result productId=\"{GetProductId(r)}\" searchResultId=\"{r.Metadata.Id}\">{r.Metadata.Text}</search_result>"))}

                Based on this, decide again how to proceed using the same rules as before.
                """);
            chatHistory.Add(toolOutputMessage);
            toolOutputs.Add(toolOutputMessage);

            foreach (var r in searchResults)
            {
                await httpResponse.WriteAsync(",\n");
                await httpResponse.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(
                    AssistantChatReplyItemType.SearchResult,
                    string.Empty,
                    int.Parse(r.Metadata.Id),
                    GetProductId(r),
                    GetPageNumber(r))));
            }
        }

        return toolOutputs;
    }

    private static async Task<NextActionReply?> GetNextAction(IChatService chatService, List<ChatMessage> chatHistory, CancellationToken cancellationToken)
    {
        var executionSettings = new ChatOptions { ResponseFormat = ChatResponseFormat.JsonObject, Seed = 0, Temperature = 0 };
        var response = (await chatService.CompleteChatAsync(chatHistory, executionSettings, cancellationToken: cancellationToken)).First();
        chatHistory.Add(response);
        return TryParseNextActionReply(response.Content, out var reply) ? reply : null;
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

    private static bool TryParseNextActionReply(string reply, [NotNullWhen(true)] out NextActionReply? assistantReply)
    {
        try
        {
            assistantReply = JsonSerializer.Deserialize<NextActionReply>(reply, _jsonOptions)!;
            return true;
        }
        catch
        {
            assistantReply = null;
            return false;
        }
    }

    public class NextActionReply
    {
        public int? SearchProductId { get; set; }
        public string? SearchPhrase { get; set; }
    }
}
