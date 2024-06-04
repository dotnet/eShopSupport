namespace Aspire.Hosting;

public static class PythonUvicornAppResourceBuilderExtensions
{
    public static IResourceBuilder<PythonUvicornAppResource> AddPythonUvicornApp(this IDistributedApplicationBuilder builder, string name, string workingDirectory, int? port = default, int? targetPort = default)
    {
        return builder.AddResource(new PythonUvicornAppResource(name, "python", workingDirectory))
            .WithArgs("-m", "uvicorn", "main:app")
            .WithHttpEndpoint(env: "UVICORN_PORT", port: port, targetPort: targetPort);
    }
}

public class PythonUvicornAppResource(string name, string command, string workingDirectory)
    : ExecutableResource(name, command, workingDirectory), IResourceWithServiceDiscovery
{
}
