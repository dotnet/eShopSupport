using System.Data.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

public static class QdrantHttpClientExtensions
{
    /// <summary>
    /// Adds a keyed <see cref="HttpClient"/> to the service collection, preconfigured to connect to the specified Qdrant service.
    /// 
    /// This is an alternative to using Aspire.Qdrant.Client. The advantage of this approach is being able to access the underlying
    /// <see cref="HttpClient"/> instance directly, which is required for Semantic Kernel's QdrantMemoryStore.
    /// </summary>
    public static void AddQdrantHttpClient(this WebApplicationBuilder builder, string connectionName)
    {
        var connectionString = builder.Configuration.GetConnectionString($"{connectionName}_http");
        var connectionBuilder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var endpoint = new Uri((string)connectionBuilder["endpoint"]);
        var key = (string)connectionBuilder["key"];

        builder.Services.AddKeyedScoped(GetServiceKey(connectionName), (services, _) =>
        {
            var httpClient = services.GetRequiredService<HttpClient>();
            httpClient.BaseAddress = endpoint;
            httpClient.DefaultRequestHeaders.Add("api-key", key);
            return httpClient;
        });
    }

    public static HttpClient GetQdrantHttpClient(this IServiceProvider services, string connectionName)
        => services.GetRequiredKeyedService<HttpClient>(GetServiceKey(connectionName));

    private static string GetServiceKey(string connectionName) => $"{connectionName}_httpclient";
}
