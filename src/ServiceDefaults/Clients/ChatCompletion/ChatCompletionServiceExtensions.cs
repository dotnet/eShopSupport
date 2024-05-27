using System.Data.Common;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
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

            var connectionStringBuilder = new DbConnectionStringBuilder();
            connectionStringBuilder.ConnectionString = builder.Configuration.GetConnectionString(name);
            if (!connectionStringBuilder.TryGetValue("Deployment", out var deploymentName))
            {
                throw new InvalidOperationException($"The connection string named '{name}' does not specify a value for 'Deployment', but this is required.");
            }

            builder.Services.AddScoped<IChatCompletionService>(services =>
            {
                var client = services.GetRequiredService<OpenAIClient>();
                return new AzureOpenAIChatCompletionService((string)deploymentName, client);
            });
        }
    }
}
