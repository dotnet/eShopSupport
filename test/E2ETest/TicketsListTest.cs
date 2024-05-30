using System.Diagnostics;
using E2ETest.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
namespace E2ETest;

[Collection(AppTestCollection.Name)]
public class TicketsListTest(AppHostFixture app) : IAsyncLifetime
{
    protected IPlaywright Playwright { get; private set; } = default!;
    protected IBrowser Browser { get; private set; } = default!;
    protected IPage Page { get; private set; } = default!;

    public async Task InitializeAsync()
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

    [Fact]
    public async Task HasPageTitle()
    {
        var httpClient = await app.StaffWebUI.CreateHttpClientAsync();
        await Page.GotoAsync(httpClient.BaseAddress!.ToString());
        await Expect(Page).ToHaveTitleAsync("eShopSupport: Tickets");
    }
}
