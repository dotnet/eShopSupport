using eShopSupport.ServiceDefaults.Clients.ChatCompletion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Microsoft.Extensions.Hosting;

public static class OllamaChatCompletionBuilderExtensions
{
    public static void AddOllamaChatCompletionService(this IHostApplicationBuilder builder, string name)
    {
        var modelName = Environment.GetEnvironmentVariable($"{name}:LlmModelName");
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException($"Expected to find the default LLM model name in an environment variable called '{name}:LlmModelName'");
        }

        builder.Services.AddScoped<IChatCompletionService>(services =>
        {
            var httpClient = services.GetRequiredService<HttpClient>();
            httpClient.BaseAddress = new Uri($"http://{name}");
            return new OllamaChatCompletionService(httpClient, modelName);
        });
    }
}
