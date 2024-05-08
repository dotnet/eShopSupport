using eShopSupport.ServiceDefaults.Clients.Backend;

namespace eShopSupport.Backend.Api;

public static class Assistant
{
    public static void MapAssistantEndpoints(this WebApplication app)
    {
        app.MapPost("/api/assistant/chat", async (HttpContext httpContext, AssistantChatRequest chatRequest) =>
        {
            var responseMessage = $"You said: {chatRequest.Message}";
            for (var i = 0; i < responseMessage.Length; i++)
            {
                await Task.Delay(20);
                await httpContext.Response.WriteAsync(responseMessage[i].ToString());
                await httpContext.Response.Body.FlushAsync();
            }
        });
    }
}
