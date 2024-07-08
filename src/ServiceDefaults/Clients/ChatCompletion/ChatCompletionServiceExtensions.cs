using System.Data.Common;
using Azure.AI.OpenAI;
using eShopSupport.ServiceDefaults.Clients.ChatCompletion;
using Experimental.AI.LanguageModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

public static class ChatCompletionServiceExtensions
{
    public static void AddChatService(this IHostApplicationBuilder builder, string name, string? cacheDir = null)
    {
        var implementationType = builder.Configuration[$"{name}:Type"];
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

            var chatBuilder = new ChatHandlerBuilder().UseStandardFunctionExecution();

            builder.Services.AddScoped<ChatService>(services =>
            {
                var client = services.GetRequiredService<OpenAIClient>();
                return new OpenAIChatService(client, (string)deploymentName, chatBuilder);
            });
        }

        if (!string.IsNullOrEmpty(cacheDir))
        {
            AddChatCompletionCaching(builder, cacheDir);
        }
    }

    private static void AddChatCompletionCaching(IHostApplicationBuilder builder, string cacheDir)
    {
        throw new NotImplementedException();

        /*
        var underlyingRegistration = builder.Services.Last(s => s.ServiceType == typeof(IChatService));

        builder.Services.Replace(new ServiceDescriptor(typeof(IChatService), services =>
        {
            var underlyingInstance = underlyingRegistration.ImplementationInstance
                ?? underlyingRegistration.ImplementationFactory!(services);
            return new CachedChatCompletionService((IChatService)underlyingInstance, cacheDir, services.GetRequiredService<ILoggerFactory>());
        }, underlyingRegistration.Lifetime));
        */
    }
}
