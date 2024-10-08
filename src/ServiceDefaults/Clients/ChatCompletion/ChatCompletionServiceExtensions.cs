using Microsoft.Extensions.AI;

namespace Microsoft.Extensions.Hosting;

public static class ChatCompletionServiceExtensions
{
    public static void AddChatCompletionService(this IHostApplicationBuilder builder, string serviceName)
    {
        var pipeline = (ChatClientBuilder pipeline) => pipeline
            .UseFunctionInvocation()
            .UseCachingForTest()
            .UseOpenTelemetry(configure: c => c.EnableSensitiveData = true);

        if (builder.Configuration[$"{serviceName}:Type"] == "ollama")
        {
            builder.AddOllamaChatClient(serviceName, pipeline);
        }
        else
        {
            builder.AddOpenAIChatClient(serviceName, pipeline);
        }
    }
}
