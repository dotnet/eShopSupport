using eShopSupport.ServiceDefaults.Clients.Backend;

namespace eShopSupport.Backend.Api;

public static class Assistant
{
    public static void MapAssistantEndpoints(this WebApplication app)
    {
        app.MapPost("/api/assistant/chat", (AssistantChatRequest chatRequest) =>
        {
            return new AssistantChatResponse($"You said: {chatRequest.Message}");
        });
    }
}
