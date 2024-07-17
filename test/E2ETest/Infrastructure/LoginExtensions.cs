using Microsoft.Playwright;

namespace E2ETest;

public static class LoginExtensions
{
    public static async Task LoginAsTestUserAsync(this IPage page, AppHostFixture app)
    {
        var homeUrl = await app.StaffWebUI.ResolveUrlAsync("/");
        await page.GotoAsync(homeUrl);

        var userNameInput = page.Locator("#Input_Username");
        var passwordInput = page.Locator("#Input_Password");
        await Expect(userNameInput).ToBeVisibleAsync();

        await userNameInput.FillAsync("bob");
        await passwordInput.FillAsync("bob");
        await page.Locator("button[value='login']").ClickAsync();

        await Expect(page).ToHaveTitleAsync("eShopSupport: Tickets");
    }
}
