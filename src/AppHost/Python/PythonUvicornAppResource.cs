namespace AppHost;

public class PythonUvicornAppResource(string name, string command, string workingDirectory)
    : ExecutableResource(name, command, workingDirectory), IResourceWithServiceDiscovery
{
}
