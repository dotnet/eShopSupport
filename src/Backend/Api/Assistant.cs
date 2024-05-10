using System.ComponentModel;
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

                The customer's most recent message is as follows:
                <latest_customer_message>{{ticket.Messages.Where(m => m.AuthorName != "Support").LastOrDefault()?.Text}}</latest_customer_message>

                The customer service agent may ask you to suggest a response or other action. Note the following policies:

                - Returns are allowed within 30 days of purchase if the item is unused, or within 1 year if the item is defective.
                  The customer can initiate a return by visiting https://northernmountains.example.com/returns
                - Further manufacturer-provided warranty and support information may be found in the product manual

                When formulating your response, follow these important guidelines:
                - Keep your replies concise and brief, ideally just a single short sentence. If you can reply in under 5 words, do so.
                - Always aim for brevity. A good reply is something like "Customer requests a refund" or "This product does not support USB chargers".
                - Refer to the customer as "the customer" and use the pronoun "them/they", and do NOT restate the customer's real name, except if you are addressing the customer themselves.
                - Rely on context, like verbal speech. For example if asked "What's this about?", you can reply "The return policy" and don't prefix it with "This is about...".
                - Any references to "Assistant" in context do NOT refer to you - they refer to the customer support team in general.
                - Never discuss any topic other than customer service for this company. You can answer any question related to our products and policies, whether or not it relates to the current support ticket.

                When asked for information, ALWAYS use the "searchProductManual" action (see below) to find the information in the manual. Never provide information
                without checking the manual first.

                Always respond in ONE of the following JSON formats:
                { "searchProductManual": "A search term", "productId": number }
                { "reply": "Your reply here", "reference": "identifier of relevant location from searchSingleManual output" }

                Your answers must ONLY use information from the provided context. Do NOT use any external information or knowledge.
                Always find product information in the product manual.
                """);

            chatHistory.AddRange(chatRequest.Messages.Select(m => new ChatMessageContent(m.IsAssistant ? AuthorRole.Assistant : AuthorRole.User, m.Text)));

            try
            {
                var kernel = new Kernel();
                kernel.Plugins.AddFromObject(new SemanticSearchPlugin());
                var executionSettings = new OpenAIPromptExecutionSettings
                {
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                    //ResponseFormat = "json",
                };
                var streamingResponse = chatService.GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);

                await foreach (var chunk in streamingResponse)
                {
                    await httpContext.Response.WriteAsync(chunk.ToString());
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

    class SemanticSearchPlugin
    {
        [KernelFunction, Description("Searches for information in the user manual for a specified product. ONLY use this if you know the product EXACT ID, otherwise search all manuals instead.")]
        public async Task<string> SearchSingleUserManualAsync([Description("The exact product ID")] int productId, [Description("The product name")] string productName, [Description("text to look for in user manual")] string query)
        {
            return "The product is red";
        }

        [KernelFunction, Description("Searches for information across all user manuals. If this if you aren't sure of the product ID.")]
        public async Task<string> SearchAllUserManualsAsync([Description("The product name, if any")] string productName, [Description("text to look for in user manual")] string query)
        {
            return "We only sell cheese";
        }
    }
}
