using System.ClientModel;
using System.Data.Common;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace Microsoft.Extensions.Hosting;

public static class ChatCompletionServiceExtensions
{
    public static void AddChatCompletionService(this IHostApplicationBuilder builder, string name, string? cacheDir = null)
    {
        var implementationType = builder.Configuration[$"{name}:Type"];
        if (implementationType == "ollama")
        {
            var modelName = builder.Configuration[$"{name}:LlmModelName"];
            if (string.IsNullOrEmpty(modelName))
            {
                throw new InvalidOperationException($"Expected to find the default LLM model name in an environment variable called '{name}:LlmModelName'");
            }

            builder.Services.AddChatClient(builder => builder
                .UseFunctionInvocation(c => c.ConcurrentInvocation = false)
                .UsePreventStreamingWithFunctions()
                .Use(new OllamaChatClient(new Uri($"http://{name}"), modelName, builder.Services.GetRequiredService<HttpClient>())));
        }
        else
        {
            // TODO: We would prefer to use Aspire.AI.OpenAI here, but it doesn't yet support the OpenAI v2 client.
            // So for now we access the connection string and set up a client manually.
            var connectionStringBuilder = new DbConnectionStringBuilder();
            connectionStringBuilder.ConnectionString = builder.Configuration.GetConnectionString(name);
            if (!connectionStringBuilder.TryGetValue("Deployment", out var deploymentName))
            {
                throw new InvalidOperationException($"The connection string named '{name}' does not specify a value for 'Deployment', but this is required.");
            }

            var endpoint = (string)connectionStringBuilder["endpoint"] ?? throw new InvalidOperationException($"The connection string named '{name}' does not specify a value for 'Endpoint', but this is required.");
            var apiKey = (string)connectionStringBuilder["key"] ?? throw new InvalidOperationException($"The connection string named '{name}' does not specify a value for 'Key', but this is required.");
            var openAIClient = new AzureOpenAIClient(
                new Uri(endpoint),
                new ApiKeyCredential(apiKey));
            builder.Services.AddScoped<OpenAIClient>(_ => openAIClient);

            builder.Services.AddChatClient(builder => builder
                .UseFunctionInvocation(c => c.ConcurrentInvocation = false)
                .Use(builder.Services.GetRequiredService<OpenAIClient>().AsChatClient((string)deploymentName)));
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
