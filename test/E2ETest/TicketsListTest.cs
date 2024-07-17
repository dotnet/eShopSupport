namespace E2ETest;

[Collection(AppTestCollection.Name)]
public class TicketsListTest(AppHostFixture app) : PlaywrightTestBase
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Page.LoginAsTestUserAsync(app);
    }

    [Fact]
    public async Task HasPageTitle()
    {
        var url = await app.StaffWebUI.ResolveUrlAsync("/");
        await Page.GotoAsync(url);
        await Expect(Page).ToHaveTitleAsync("eShopSupport: Tickets");
    }

    [Fact]
    public async Task ShowsOpenTicketsButNotClosedOnes()
    {
        var url = await app.StaffWebUI.ResolveUrlAsync("/");
        await Page.GotoAsync(url);

        await Expect(Page.Locator("a[href='ticket/1']").First).ToBeAttachedAsync();
        await Expect(Page.Locator("a[href='ticket/2']").First).Not.ToBeAttachedAsync();
    }

    [Fact]
    public async Task CanSwitchToShowClosedTickets()
    {
        var url = await app.StaffWebUI.ResolveUrlAsync("/");
        await Page.GotoAsync(url);

        await Page.ClickAsync("#filter-closed");
        await Expect(Page.Locator("a[href='ticket/2']").First).ToBeAttachedAsync();
        await Expect(Page.Locator("a[href='ticket/1']").First).Not.ToBeAttachedAsync();
    }
}
