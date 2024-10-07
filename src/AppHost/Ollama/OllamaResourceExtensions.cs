using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

internal static class OllamaResourceExtensions
{
    public static IResourceBuilder<OllamaResource> AddOllama(this IDistributedApplicationBuilder builder, string name, string[]? models = null, string? defaultModel = null, bool enableGpu = true, int? port = null)
    {
        const string configKey = "OllamaModel";
        defaultModel ??= builder.Configuration[configKey];

        if (models is null or { Length: 0 })
        {
            if (string.IsNullOrEmpty(defaultModel))
            {
                throw new InvalidOperationException($"Expected the parameter '{nameof(defaultModel)}' or '{nameof(models)}' to be nonempty, or to find a configuration value '{configKey}', but none were provided.");
            }
            models = [defaultModel];
        }

        var resource = new OllamaResource(name, models, defaultModel ?? models.First(), enableGpu);
        var ollama = builder.AddResource(resource)
            .WithHttpEndpoint(port: port, targetPort: 11434)
            .WithImage("ollama/ollama", tag: "0.3.12");

        if (enableGpu)
        {
            ollama = ollama.WithContainerRuntimeArgs("--gpus=all");
        }

        builder.Services.TryAddLifecycleHook<OllamaEnsureModelAvailableHook>();

        // This is a bit of a hack to show downloading models in the UI
        builder.AddResource(new OllamaModelDownloaderResource($"ollama-model-downloader-{name}", resource))
            .WithInitialState(new()
            {
                Properties = [],
                ResourceType = "ollama downloader",
                State = KnownResourceStates.Hidden
            })
            .ExcludeFromManifest();

        return ollama;
    }
    
    public static IResourceBuilder<OllamaResource> WithDataVolume(this IResourceBuilder<OllamaResource> builder)
    {
        return builder.WithVolume(CreateVolumeName(builder, builder.Resource.Name), "/root/.ollama");
    }

    public static IResourceBuilder<TDestination> WithReference<TDestination>(this IResourceBuilder<TDestination> builder, IResourceBuilder<OllamaResource> ollamaBuilder)
        where TDestination : IResourceWithEnvironment
    {
        return builder
            .WithReference(ollamaBuilder.GetEndpoint("http"))
            .WithEnvironment($"{ollamaBuilder.Resource.Name}:Type", "ollama")
            .WithEnvironment($"{ollamaBuilder.Resource.Name}:LlmModelName", ollamaBuilder.Resource.DefaultModel);
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

    private sealed class OllamaEnsureModelAvailableHook(
        ResourceLoggerService loggerService,
        ResourceNotificationService notificationService,
        DistributedApplicationExecutionContext context) : IDistributedApplicationLifecycleHook
    {
        public Task AfterEndpointsAllocatedAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken = default)
        {
            if (context.IsPublishMode)
            {
                return Task.CompletedTask;
            }

            var client = new HttpClient();

            foreach (var downloader in appModel.Resources.OfType<OllamaModelDownloaderResource>())
            {
                var ollama = downloader.ollamaResource;

                var logger = loggerService.GetLogger(downloader);

                _ = Task.Run(async () =>
                {
                    var httpEndpoint = ollama.GetEndpoint("http");

                    // TODO: Make this resilient to failure
                    var ollamaModelsAvailable = await client.GetFromJsonAsync<OllamaGetTagsResponse>($"{httpEndpoint.Url}/api/tags", new JsonSerializerOptions(JsonSerializerDefaults.Web));

                    if (ollamaModelsAvailable is null)
                    {
                        return;
                    }

                    var availableModelNames = ollamaModelsAvailable.Models?.Select(m => m.Name) ?? [];

                    var modelsToDownload = ollama.Models.Except(availableModelNames);

                    if (!modelsToDownload.Any())
                    {
                        return;
                    }

                    logger.LogInformation("Downloading models {Models} for ollama {OllamaName}...", string.Join(", ", modelsToDownload), ollama.Name);

                    await notificationService.PublishUpdateAsync(downloader, s => s with
                    {
                        State = new("Downloading models...", KnownResourceStateStyles.Info)
                    });

                    await Parallel.ForEachAsync(modelsToDownload, async (modelName, ct) =>
                    {
                        await DownloadModelAsync(logger, httpEndpoint, modelName, ct);
                    });

                    await notificationService.PublishUpdateAsync(downloader, s => s with
                    {
                        State = new("Models downloaded", KnownResourceStateStyles.Success)
                    });
                }, 
                cancellationToken);
            }

            return Task.CompletedTask;
        }

        private static async Task DownloadModelAsync(ILogger logger, EndpointReference httpEndpoint, string? modelName, CancellationToken cancellationToken)
        {
            logger.LogInformation("Pulling ollama model {ModelName}...", modelName);

            var httpClient = new HttpClient { Timeout = TimeSpan.FromDays(1) };
            var request = new HttpRequestMessage(HttpMethod.Post, $"{httpEndpoint.Url}/api/pull") { Content = JsonContent.Create(new { name = modelName }) };
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var responseContentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var streamReader = new StreamReader(responseContentStream);
            var line = (string?)null;
            while ((line = await streamReader.ReadLineAsync(cancellationToken)) is not null)
            {
                logger.Log(LogLevel.Information, 0, line, null, (s, ex) => s);
            }

            logger.LogInformation("Finished pulling ollama mode {ModelName}", modelName);
        }

        record OllamaGetTagsResponse(OllamaGetTagsResponseModel[]? Models);
        record OllamaGetTagsResponseModel(string Name);
    }

    private class OllamaModelDownloaderResource(string name, OllamaResource ollamaResource) : Resource(name)
    {
        public OllamaResource ollamaResource { get; } = ollamaResource;
    }
}
