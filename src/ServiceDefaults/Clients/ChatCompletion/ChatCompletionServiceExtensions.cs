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

        var implementationType = builder.Configuration[$"{serviceName}:Type"];
        if (implementationType == "ollama")
        {
            builder.AddOllamaChatClient(serviceName, pipeline);
        }
        else
        {
            // TODO: We would prefer to use Aspire.AI.OpenAI here, but it doesn't yet support the OpenAI v2 client.
            // So for now we access the connection string and set up a client manually.
            builder.AddOpenAIChatClient(serviceName, pipeline);
        }
    }
}
