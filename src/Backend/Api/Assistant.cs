using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using eShopSupport.Backend.Data;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;

namespace eShopSupport.Backend.Api;

public static class Assistant
{
    private readonly static JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapAssistantEndpoints(this WebApplication app)
    {
        app.MapPost("/api/assistant/chat", async (HttpContext httpContext, AppDbContext dbContext, IChatCompletionService chatService, ProductManualSemanticSearch manualSearch, ILoggerFactory loggerFactory, CancellationToken cancellationToken, AssistantChatRequest chatRequest) =>
        {
            // TODO: Get the product details as well, and include them in the system message
            var ticket = await dbContext.Tickets
                .Include(t => t.Messages)
                .SingleAsync(t => t.TicketId == chatRequest.TicketId);

            var chatHistory = new ChatHistory($$"""
                You are a helpful AI assistant called 'Assistant' who helps customer service agents working for Northern Mountains, an online retailer.
                The customer service agent is currently handling the following ticket:
                
                <product_id>{{ticket.ProductId}}</product_id>
                <customer_name>{{ticket.CustomerFullName}}</customer_name>
                <summary>{{ticket.LongSummary}}</summary>

                The most recent message from the customer is this:
                <customer_message>{{ticket.Messages.LastOrDefault(m => m.AuthorName != "Support")?.Text}}</customer_message>
                However, that is only provided for context. You are not answering that question directly. The real question is provided below.

                The customer service agent may ask you for help either directly with the customer request, or other general information about this product or our other products. Respond to the AGENT'S question, not specifically to the customer ticket.
                If you need more information, you can search the product manual using "searchParams" as described below.

                Use the context to reply in the following JSON format:

                { "gotEnoughInfoAleady": trueOrFalse, ... }

                If this is a question about the product, you should ALWAYS set gotEnoughInfoAleady to false and search the manual.

                If the context provides information, use it to add an answer like this: { "gotEnoughInfoAlready": true, "answer": string, "mostRelevantSearchResultId": number, "mostRelevantSearchQuote": string }
                You must justify your answer by providing mostRelevantSearchResultId that supports your info, and mostRelevantSearchQuote (which is a short EXACT word-for-word quote from the most relevant search result, excluding headings).

                If you don't already have enough information, add a suggested search term to use like this: { "gotEnoughInfoAlready": false, "searchPhrase": "a phrase to look for in the manual" }.
                That will search the product manual for this specific product, so you don't have to restate the product ID or name.
                
                Remember that you are only answering the support agent's question to you. You are not answering the customer's question directly.
                Here is the real support agent's question for you to answer:
                """);

            chatHistory.AddRange(chatRequest.Messages.Select(m => new ChatMessageContent(m.IsAssistant ? AuthorRole.Assistant : AuthorRole.User, m.Text)));

            var executionSettings = new OpenAIPromptExecutionSettings
            {
                ResponseFormat = "json_object",
                Temperature = 0,
            };

            var numToolsExecuted = 0;
            var isFirstMessage = true;

            try
            {
                while (true)
                {
                    var captured = string.Empty;
                    var isFirstChunk = true;
                    await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, cancellationToken: cancellationToken))
                    {
                        if (isFirstChunk)
                        {
                            isFirstChunk = false;
                            await httpContext.Response.WriteAsync(isFirstMessage ? "[" : ", ");
                        }

                        // TODO: Force it to stop as soon as the top-level JSON object is closed, otherwise it will emit a long
                        // sequence of trailing whitespace: https://github.com/ollama/ollama/issues/2623
                        var chunkString = chunk.ToString();
                        await httpContext.Response.WriteAsync(chunkString);
                        captured += chunkString;
                    }

                    isFirstMessage = false;

                    if (TryParseReply(captured, out var assistantReply) && !string.IsNullOrWhiteSpace(assistantReply.SearchPhrase))
                    {
                        if (++numToolsExecuted < 2)
                        {
                            var searchResults = await manualSearch.SearchAsync(ticket.ProductId, assistantReply.SearchPhrase);
                            chatHistory.AddMessage(AuthorRole.System, $"""
                                The assistant performed a search with term "{assistantReply.SearchPhrase}" on the user manual,
                                which returned the following results:
                                {string.Join("\n", searchResults.Select(r => $"<search_result resultId=\"{r.Metadata.Id}\">{r.Metadata.Text}</search_result>"))}
                                """);

                            // Also emit the search result data to the response so the UI has all the metadata
                            await httpContext.Response.WriteAsync(", ");
                            await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new
                            {
                                SearchResults = searchResults.Select(r => new
                                {
                                    r.Metadata.Id,
                                    ProductId = GetProductId(r),
                                    PageNumber = GetPageNumber(r),
                                })
                            }, _jsonOptions));
                        }
                        else
                        {
                            chatHistory.AddMessage(AuthorRole.System,
                                $"""
                                Please note that \"searchPhrase\" is no longer available. Your reply *MUST* state your answer, even if you are simply saying you do not have an answer.
                                """);
                        }

                        continue;
                    }

                    await httpContext.Response.WriteAsync("]");
                    return;
                }
            }
            catch (Exception ex)
            {
                // We don't want to return the raw exception to the response as it would then show up in the chat UI
                var logger = loggerFactory.CreateLogger(typeof(Assistant).FullName!);
                logger.LogError(ex, "Error during chat completion.");
                await httpContext.Response.WriteAsync("Sorry, there was a problem. Please try again.");
            }
        });
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

    class AssistantReply
    {
        public string? Answer { get; set; }
        public string? SearchPhrase { get; set; }
    }
}
