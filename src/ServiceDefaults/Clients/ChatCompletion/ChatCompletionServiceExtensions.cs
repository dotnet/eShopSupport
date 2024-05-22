using Azure.AI.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Microsoft.Extensions.Hosting;

public static class ChatCompletionServiceExtensions
{
    public static void AddChatCompletionService(this IHostApplicationBuilder builder, string name)
    {
        var implementationType = Environment.GetEnvironmentVariable($"{name}:Type");
        if (implementationType == "ollama")
        {
            builder.AddOllamaChatCompletionService(name);
        }
        else
        {
            builder.AddAzureOpenAIClient(name);

            var deploymentName = "gpt-35-1106"; // TODO: Get from connection string
            builder.Services.AddScoped<IChatCompletionService>(services =>
            {
                var client = services.GetRequiredService<OpenAIClient>();
                return new AzureOpenAIChatCompletionService(deploymentName, client);
            });
        }
    }
}
