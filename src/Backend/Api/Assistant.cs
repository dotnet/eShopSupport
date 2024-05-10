using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace eShopSupport.Backend.Api;

public static class Assistant
{
    public static void MapAssistantEndpoints(this WebApplication app)
    {
        app.MapPost("/api/assistant/chat", async (HttpContext httpContext, IChatCompletionService chatService, ILoggerFactory loggerFactory, AssistantChatRequest chatRequest) =>
        {
            var chatHistory = new ChatHistory(chatRequest.Messages.Select(m => new ChatMessageContent(
                m.IsAssistant ? AuthorRole.Assistant : AuthorRole.User,
                m.Text)));

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
