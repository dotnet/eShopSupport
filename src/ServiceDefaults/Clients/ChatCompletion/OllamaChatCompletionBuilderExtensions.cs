using eShopSupport.ServiceDefaults.Clients.ChatCompletion;
using Experimental.AI.LanguageModels;
using Microsoft.Extensions.DependencyInjection;

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

        builder.Services.AddScoped<ChatClient>(services =>
        {
            var httpClient = services.GetRequiredService<HttpClient>();
            httpClient.BaseAddress = new Uri($"http://{name}");
            var handler = new OllamaChatCompletionHandler(httpClient, modelName);
            var service = new ChatClient(handler);
            service.UseStandardFunctionExecution();
            return service;
        });
    }
}
