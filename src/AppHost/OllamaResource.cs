using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

internal static class OllamaResourceExtensions
{
    public static IResourceBuilder<ContainerResource> AddOllama(this IDistributedApplicationBuilder builder, string name, bool enableGpu, string[] models)
    {
        var ollama = builder.AddContainer(name, "ollama/ollama");

        if (enableGpu)
        {
            ollama = ollama.WithContainerRunArgs("--gpus=all");
        }

        ollama = ollama.WithVolume(CreateVolumeName(ollama, name), "/root/.ollama");

        builder.Services.TryAddLifecycleHook(s => new OllamaEnsureModelAvailableHook(s, ollama, models));

        return ollama;
    }

    private static string CreateVolumeName<T>(IResourceBuilder<T> builder, string suffix) where T : IResource
    {
        // Ideally this would be public
        return (string)typeof(ContainerResource).Assembly
            .GetType("Aspire.Hosting.Utils.VolumeNameGenerator", true)!
            .GetMethod("CreateVolumeName")!
            .MakeGenericMethod(typeof(T))
            .Invoke(null, [builder, suffix])!;
    }

    internal sealed class OllamaEnsureModelAvailableHook(IServiceProvider services, IResourceBuilder<ContainerResource> ollama, string[] modelNames) : IDistributedApplicationLifecycleHook
    {
        public async Task AfterResourcesCreatedAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken)
        {
            // Ideally, the "pull model" operation would occur during the "starting resource" phase, so you can see that
            // status in the dashboard. I'm not clear on how to do that though.

            var httpEndpoint = ollama.GetEndpoint("http");

            var ollamaModelsAvailable = await new HttpClient().GetFromJsonAsync<OllamaGetTagsResponse>($"{httpEndpoint.Url}/api/tags", new JsonSerializerOptions(JsonSerializerDefaults.Web));
            foreach (var modelName in modelNames)
            {
                if (!ollamaModelsAvailable!.Models.Any(m => m.Name == modelName))
                {
                    var logger = services.GetRequiredService<ILogger<OllamaEnsureModelAvailableHook>>();
                    logger.LogInformation($"Pulling ollama model {modelName}...");
                    var httpClient = new HttpClient { Timeout = TimeSpan.FromDays(1) };
                    var request = new HttpRequestMessage(HttpMethod.Post, $"{httpEndpoint.Url}/api/pull") { Content = JsonContent.Create(new { name = modelName }) };
                    var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    var responseContentStream = await response.Content.ReadAsStreamAsync();
                    using var streamReader = new StreamReader(responseContentStream);
                    var line = (string?)null;
                    while ((line = await streamReader.ReadLineAsync()) is not null)
                    {
                        logger.LogInformation(line);
                    }

                    logger.LogInformation($"Finished pulling ollama mode {modelName}");
                }

            }
        }

        record OllamaGetTagsResponse(OllamaGetTagsResponseModel[] Models);
        record OllamaGetTagsResponseModel(string Name);
    }
}
