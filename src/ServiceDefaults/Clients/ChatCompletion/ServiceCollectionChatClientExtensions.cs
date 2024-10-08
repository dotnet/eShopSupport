using Azure.AI.OpenAI;
using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using System.Data.Common;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.Hosting;

public static class ServiceCollectionChatClientExtensions
{
    public static IServiceCollection AddAspireOllamaChatClient(
        this IHostApplicationBuilder hostBuilder,
        string serviceName,
        string modelName,
        Func<ChatClientBuilder, ChatClientBuilder>? builder = null)
        => hostBuilder.Services.AddOllamaChatClient(
            modelName,
            new Uri($"http://{serviceName}"),
            builder);

    public static IServiceCollection AddOllamaChatClient(
        this IServiceCollection services,
        string modelName,
        Uri? uri = null,
        Func<ChatClientBuilder, ChatClientBuilder>? builder = null)
    {
        uri ??= new Uri("http://localhost:11434");
        return services.AddChatClient(pipeline =>
        {
            builder?.Invoke(pipeline);

            // Temporary workaround for Ollama issues
            pipeline.UsePreventStreamingWithFunctions();

            var httpClient = pipeline.Services.GetService<HttpClient>() ?? new();
            return pipeline.Use(new OllamaChatClient(uri, modelName, httpClient));
        });
    }

    public static IServiceCollection AddAspireOpenAIChatClient(
        this IHostApplicationBuilder hostBuilder,
        string serviceName,
        Func<ChatClientBuilder, ChatClientBuilder>? builder = null,
        string? modelOrDeploymentName = null)
    {
        var connectionString = hostBuilder.Configuration.GetConnectionString(serviceName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"No connection string named '{serviceName}' was found. Ensure a corresponding Aspire service was registered.");
        }

        var connectionStringBuilder = new DbConnectionStringBuilder();
        connectionStringBuilder.ConnectionString = connectionString;
        var endpoint = (string?)connectionStringBuilder["endpoint"];
        var apiKey = (string)connectionStringBuilder["key"] ?? throw new InvalidOperationException($"The connection string named '{serviceName}' does not specify a value for 'Key', but this is required.");

        modelOrDeploymentName ??= (connectionStringBuilder["Deployment"] ?? connectionStringBuilder["Model"]) as string;
        if (string.IsNullOrWhiteSpace(modelOrDeploymentName))
        {
            throw new InvalidOperationException($"The connection string named '{serviceName}' does not specify a value for 'Deployment' or 'Model', and no value was passed for {nameof(modelOrDeploymentName)}.");
        }

        var endpointUri = string.IsNullOrEmpty(endpoint) ? null : new Uri(endpoint);
        return hostBuilder.Services.AddOpenAIChatClient(apiKey, modelOrDeploymentName, endpointUri, builder);
    }

    public static IServiceCollection AddOpenAIChatClient(
        this IServiceCollection services,
        string apiKey,
        string modelOrDeploymentName,
        Uri? endpoint = null,
        Func<ChatClientBuilder, ChatClientBuilder>? builder = null)
    {
        return services
            .AddSingleton(_ => endpoint is null
                ? new OpenAIClient(apiKey)
                : new AzureOpenAIClient(endpoint, new ApiKeyCredential(apiKey)))
            .AddChatClient(pipeline =>
            {
                builder?.Invoke(pipeline);
                var openAiClient = pipeline.Services.GetRequiredService<OpenAIClient>();
                return pipeline.Use(openAiClient.AsChatClient(modelOrDeploymentName));
            });
    }
}
