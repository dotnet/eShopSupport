using eShopSupport.Backend.Data;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace eShopSupport.Backend.Api;

public static class Assistant
{
    public static void MapAssistantEndpoints(this WebApplication app)
    {
        app.MapPost("/api/assistant/chat", async (HttpContext httpContext, AppDbContext dbContext, IChatCompletionService chatService, ILoggerFactory loggerFactory, AssistantChatRequest chatRequest) =>
        {
            // TODO: Get the product details as well, and include them in the system message
            var ticket = await dbContext.Tickets
                .Include(t => t.Messages)
                .SingleAsync(t => t.TicketId == chatRequest.TicketId);

            var chatHistory = new ChatHistory($"""
                You are a helpful AI assistant called 'Assistant' who helps customer service agents working for Northern Mountains, an online retailer.
                The customer service agent is currently handling the following ticket:
                
                <product_id>{ticket.ProductId}</product_id>
                <customer_name>{ticket.CustomerFullName}</customer_name>
                <summary>{ticket.LongSummary}</summary>

                The customer's most recent message is as follows:
                <latest_customer_message>{ticket.Messages.Where(m => m.AuthorName != "Support").LastOrDefault()?.Text}</latest_customer_message>

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
                """);

            chatHistory.AddRange(chatRequest.Messages.Select(m => new ChatMessageContent(m.IsAssistant ? AuthorRole.Assistant : AuthorRole.User, m.Text)));

            try
            {
                var streamingResponse = chatService.GetStreamingChatMessageContentsAsync(chatHistory);

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
}
