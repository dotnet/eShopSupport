using System.Text.Json;
using eShopSupport.Backend.Data;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace eShopSupport.Backend.Api;

public static class Assistant
{
    public static void MapAssistantEndpoints(this WebApplication app)
    {
        app.MapPost("/api/assistant/chat", async (HttpContext httpContext, AppDbContext dbContext, IChatCompletionService chatService, ILoggerFactory loggerFactory, CancellationToken cancellationToken, AssistantChatRequest chatRequest) =>
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

                If the context provides information, use it to add an answer like this: { "gotEnoughInfoAlready": true, "answer": string }
                Always try to use information from context instead of searching the manual.

                If you don't already have enough information, add a suggested search term to use like this: { "gotEnoughInfoAlready": false, "searchPhrase": "a phrase to look for in the manual" }.
                That will search the product manual for this specific product, so you don't have to restate the product ID or name.
                
                Remember that you are only answering the support agent's question to you. You are not answering the customer's question directly.
                Here is the real support agent's question for you to answer:
                """);

            chatHistory.AddRange(chatRequest.Messages.Select(m => new ChatMessageContent(m.IsAssistant ? AuthorRole.Assistant : AuthorRole.User, m.Text)));


            var executionSettings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                ResponseFormat = "json_object",
                Temperature = 0,
            };

            try
            {
                while (true)
                {
                    var response = await chatService.GetChatMessageContentAsync(chatHistory, executionSettings, cancellationToken: cancellationToken);
                    var responseString = response.ToString();
                    var reply = JsonSerializer.Deserialize<AssistantReply>(response.ToString(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

                    if (string.IsNullOrWhiteSpace(reply.Answer) && !string.IsNullOrWhiteSpace(reply.SearchPhrase))
                    {
                        // Function call to search the manual
                        await httpContext.Response.WriteAsync($"[Searching: {reply.SearchPhrase}] ");

                        var searchResult = """
                    For additional context, here is information from the product manual:
                    
                    <manual_extract ref="1">The handle is pink and made of plastic.</manual_extract>
                    <manual_extract ref="2">Using this item outdoors is not recommended, but feel free to use it in a bathroom.</manual_extract>
                    <manual_extract ref="3">For support, contact support@grillzone.com</manual_extract>
                    """;

                        await httpContext.Response.WriteAsync($"[Search result: {searchResult}] ");

                        chatHistory.Add(new ChatMessageContent(AuthorRole.System, searchResult));
                    }
                    else
                    {
                        // Final reply
                        await httpContext.Response.WriteAsync(reply.Answer ?? "Sorry, I don't have an answer.");
                        break;
                    }
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

    class AssistantReply
    {
        public string? Answer { get; set; }
        public string? SearchPhrase { get; set; }
    }
}
