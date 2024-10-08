using Microsoft.Extensions.AI;

namespace Microsoft.Extensions.Hosting;

public static class ChatCompletionServiceExtensions
{
    public static void AddChatCompletionService(this IHostApplicationBuilder builder, string serviceName, string? cacheDir = null)
    {
        var implementationType = builder.Configuration[$"{serviceName}:Type"];
        if (implementationType == "ollama")
        {
            builder.AddOllamaChatClient(serviceName, builder => builder
                .UseFunctionInvocation()
                .UseOpenTelemetry(configure: c => c.EnableSensitiveData = true));
        }
        else
        {
            // TODO: We would prefer to use Aspire.AI.OpenAI here, but it doesn't yet support the OpenAI v2 client.
            // So for now we access the connection string and set up a client manually.
            builder.AddOpenAIChatClient(serviceName, builder => builder
                .UseFunctionInvocation()
                .UseOpenTelemetry(configure: c => c.EnableSensitiveData = true));
        }

        if (!string.IsNullOrEmpty(cacheDir))
        {
            AddChatCompletionCaching(builder, cacheDir);
        }
    }

    private static void AddChatCompletionCaching(IHostApplicationBuilder builder, string cacheDir)
    {
        throw new NotImplementedException("TODO: Implement caching");
    }
}
