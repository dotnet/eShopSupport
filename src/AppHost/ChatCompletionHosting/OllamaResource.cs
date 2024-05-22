namespace Aspire.Hosting;

internal class OllamaResource(string name, string[] models, string? defaultModel, bool enableGpu) : ContainerResource(name)
{
    public string[] Models { get; } = models;
    public string? DefaultModel { get; } = defaultModel;
    public bool EnableGpu { get; } = enableGpu;
}
