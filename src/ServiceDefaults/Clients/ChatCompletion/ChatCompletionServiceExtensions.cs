using Microsoft.Extensions.AI;

namespace Microsoft.Extensions.Hosting;

public static class ChatCompletionServiceExtensions
{
    public static void AddChatCompletionService(this IHostApplicationBuilder builder, string serviceName)
    {
        ChatClientBuilder chatClientBuilder = (builder.Configuration[$"{serviceName}:Type"] == "ollama") ?
            builder.AddOllamaChatClient(serviceName) :
            builder.AddOpenAIChatClient(serviceName);

        chatClientBuilder
            .UseFunctionInvocation()
            .UseCachingForTest()
            .UseOpenTelemetry(configure: c => c.EnableSensitiveData = true);
    }
}
