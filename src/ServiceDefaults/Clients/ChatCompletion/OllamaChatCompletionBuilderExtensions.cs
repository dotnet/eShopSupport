using eShopSupport.ServiceDefaults.Clients.ChatCompletion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Microsoft.Extensions.Hosting;

public static class OllamaChatCompletionBuilderExtensions
{
    public static void AddOllamaChatCompletionService(this IHostApplicationBuilder builder, string name, string? model = null)
    {
        var modelName = model ?? builder.Configuration[$"{name}:LlmModelName"];
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException($"Expected to find the default LLM model name in an environment variable called '{name}:LlmModelName'");
        }

        builder.Services.AddScoped<IChatCompletionService>(services =>
        {
            var httpClient = services.GetRequiredService<HttpClient>();
            httpClient.BaseAddress = new Uri($"https://56lbqqkh-11434.asse.devtunnels.ms/");
            return new OllamaChatCompletionService(httpClient, modelName);
        });
    }
}
