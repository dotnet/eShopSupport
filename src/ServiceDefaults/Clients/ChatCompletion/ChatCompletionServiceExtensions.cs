using System.Data.Common;
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

            // TODO: Going to need to add some middleware that handles streaming by calling the nonstreaming endpoint
            // if there are tools, since Ollama doesn't currently support streaming function calling.
            builder.Services.AddChatClient(builder => builder
                .UseFunctionInvocation()
                .Use(new OllamaChatClient(new Uri($"http://{name}"), modelName)));
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

            builder.Services.AddChatClient(builder => builder
                .UseFunctionInvocation()
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
