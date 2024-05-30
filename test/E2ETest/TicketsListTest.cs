namespace E2ETest;

[Collection(AppTestCollection.Name)]
public class TicketsListTest(AppHostFixture app) : PlaywrightTestBase
{
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

        await Expect(Page.Locator("a[href='ticket/1']")).ToHaveCountAsync(5);
        await Expect(Page.Locator("a[href='ticket/2']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task CanSwitchToShowClosedTickets()
    {
        var url = await app.StaffWebUI.ResolveUrlAsync("/");
        await Page.GotoAsync(url);

        await Page.ClickAsync("#filter-closed");
        await Expect(Page.Locator("a[href='ticket/2']")).ToHaveCountAsync(5);
        await Expect(Page.Locator("a[href='ticket/1']")).ToHaveCountAsync(0);
    }
}
