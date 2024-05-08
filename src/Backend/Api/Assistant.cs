using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace eShopSupport.Backend.Api;

public static class Assistant
{
    public static void MapAssistantEndpoints(this WebApplication app)
    {
        app.MapPost("/api/assistant/chat", async (HttpContext httpContext, IChatCompletionService chatService, AssistantChatRequest chatRequest) =>
        {
            var chatHistory = new ChatHistory(chatRequest.Messages.Select(m => new ChatMessageContent(
                m.IsAssistant ? AuthorRole.Assistant : AuthorRole.User,
                m.Text)));

            var streamingResponse = chatService.GetStreamingChatMessageContentsAsync(chatHistory);
            await foreach (var chunk in streamingResponse)
            {
                await httpContext.Response.WriteAsync(chunk.ToString());
            }
        });
    }
}
