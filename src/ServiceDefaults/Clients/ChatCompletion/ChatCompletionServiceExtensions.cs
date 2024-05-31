using System.Data.Common;
using Azure.AI.OpenAI;
using eShopSupport.ServiceDefaults.Clients.ChatCompletion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Microsoft.Extensions.Hosting;

public static class ChatCompletionServiceExtensions
{
    public static ChatCompletionServiceBuilder AddChatCompletionService(this IHostApplicationBuilder builder, string name)
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

        return new ChatCompletionServiceBuilder(builder);
    }

    public class ChatCompletionServiceBuilder(IHostApplicationBuilder builder)
    {
        public void WithChatCompletionServiceCache(string cacheDir)
        {
            var underlyingRegistration = builder.Services.Last(s => s.ServiceType == typeof(IChatCompletionService));

            builder.Services.Replace(new ServiceDescriptor(typeof(IChatCompletionService), services =>
            {
                var underlyingInstance = underlyingRegistration.ImplementationInstance
                    ?? underlyingRegistration.ImplementationFactory!(services);
                return new CachedChatCompletionService((IChatCompletionService)underlyingInstance, cacheDir, services.GetRequiredService<ILoggerFactory>());
            }, underlyingRegistration.Lifetime));
        }
    }
}
