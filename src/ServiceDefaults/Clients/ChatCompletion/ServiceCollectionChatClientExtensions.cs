using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

public static class ServiceCollectionChatClientExtensions
{
    public static IServiceCollection AddOllamaChatClient(
        this IServiceCollection services,
        string aspireServiceName,
        string modelName,
        Func<ChatClientBuilder, ChatClientBuilder>? builder = null)
        => services.AddOllamaChatClient(
            modelName,
            new Uri($"http://{aspireServiceName}"),
            builder);

    public static IServiceCollection AddOllamaChatClient(
        this IServiceCollection services,
        string modelName,
        Uri? uri = null,
        Func<ChatClientBuilder, ChatClientBuilder>? builder = null)
    {
        uri ??= new Uri("http://localhost:11434");
        return services.AddChatClient(innerBuilder =>
        {
            builder?.Invoke(innerBuilder);

            var httpClient = innerBuilder.Services.GetService<HttpClient>() ?? new();
            return innerBuilder.Use(new OllamaChatClient(uri, modelName, httpClient));
        });
    }
}
