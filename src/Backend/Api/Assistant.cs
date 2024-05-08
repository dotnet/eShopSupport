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
            var messageContent = await chatService.GetChatMessageContentAsync(new ChatHistory([new ChatMessageContent(AuthorRole.User, chatRequest.Message)]));
            return messageContent.ToString();
        });
    }
}
