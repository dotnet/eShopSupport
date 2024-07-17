﻿using Aspire.Hosting.Testing;
using eShopSupport.ServiceDefaults.Clients.Backend;
using IdentityModel.Client;

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
        Environment.CurrentDirectory = Projects.AppHost.ProjectPath;
        Environment.SetEnvironmentVariable("E2E_TEST", "true");
        Environment.SetEnvironmentVariable("E2E_TEST_CHAT_COMPLETION_CACHE_DIR",
            Path.Combine(Projects.E2ETest.ProjectPath, "ChatCompletionCache"));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        var app = await builder.BuildAsync();
        await app.StartAsync();

        // Don't consider it initialized until we confirm it's finished data seeding
        var backendClient = await DevToolBackendClient.GetDevToolStaffBackendClientAsync(
            app.CreateHttpClient("identity-server"),
            app.CreateHttpClient("backend"));
        var tickets = await backendClient.ListTicketsAsync(new ListTicketsRequest(null, null, null, 0, 1, null, null));
        Assert.NotEmpty(tickets.Items);

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

        public async Task<string> ResolveUrlAsync(string relativeUrl)
        {
            var app = await owner._appInitializer;
            var baseUri = app.GetEndpoint(name);
            return new Uri(baseUri, relativeUrl).ToString();
        }
    }
}
