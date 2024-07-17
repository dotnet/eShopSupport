using System.Diagnostics;
using Microsoft.Playwright;
namespace E2ETest.Infrastructure;

public class PlaywrightTestBase : IAsyncLifetime
{
    protected IPlaywright Playwright { get; private set; } = default!;
    protected IBrowser Browser { get; private set; } = default!;
    protected IPage Page { get; private set; } = default!;

    public virtual async Task InitializeAsync()
    {
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new()
        {
            Headless = !Debugger.IsAttached,
        });

        if (Page is not null)
        {
            // Otherwise we have to deal with unsubscribing from Page.PageError
            throw new InvalidOperationException("Cannot intialize a new page when one is already initialized");
        }

        Page = await Browser.NewPageAsync();
        Page.PageError += (_, message)
            => throw new InvalidOperationException("Page error: " + message);
    }

    public async Task DisposeAsync()
    {
        await Browser.DisposeAsync();
        Playwright.Dispose();
    }
}
