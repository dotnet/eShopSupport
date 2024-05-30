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
}
