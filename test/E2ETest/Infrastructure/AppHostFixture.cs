using Aspire.Hosting.Testing;

namespace E2ETest.Infrastructure;

public class AppHostFixture : IAsyncDisposable
{
    private readonly Task<DistributedApplication> _appInitializer;

    public Resource StaffWebUI { get; }

    public AppHostFixture()
    {
        _appInitializer = InitializeAsync();
        StaffWebUI = new Resource("staffwebui", this);
    }

    private async Task<DistributedApplication> InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        var app = await builder.BuildAsync();
        await app.StartAsync();
        return app;
    }

    public async ValueTask DisposeAsync()
    {
        var app = await _appInitializer;
        await app.StopAsync();
    }

    public class Resource(string name, AppHostFixture owner)
    {
        public async Task<HttpClient> CreateHttpClientAsync()
            => (await owner._appInitializer).CreateHttpClient(name);
    }
}
