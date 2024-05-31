using Microsoft.Playwright;

namespace E2ETest;

[Collection(AppTestCollection.Name)]
public class TicketAssistantTest(AppHostFixture app) : PlaywrightTestBase
{
    ILocator SuggestionLinks => Page.Locator(".assistant .suggestions a");
    ILocator WriteMessageTextArea => Page.Locator(".assistant .write-message textarea");
    ILocator NthReply(int n) => Page.Locator(".assistant .message.assistant").Nth(n);

    [Fact]
    public async Task OffersOptionsToCheckManualAndWriteReply()
    {
        var url = await app.StaffWebUI.ResolveUrlAsync("/ticket/1");
        await Page.GotoAsync(url);

        await Expect(SuggestionLinks).ToHaveCountAsync(2);
        await Expect(SuggestionLinks.Nth(0)).ToHaveTextAsync("What does the manual say about this?");
        await Expect(SuggestionLinks.Nth(1)).ToHaveTextAsync("Write a suggested reply to the customer.");
    }

    [Fact]
    public async Task CanAnswerQuestionAboutTicket()
    {
        var url = await app.StaffWebUI.ResolveUrlAsync("/ticket/1");
        await Page.GotoAsync(url);
        await SendMessageAsync("What product is this about? Reply with the product name only and no other text.");

        var reply = await GetNthCompletedReply(0);
        await Expect(reply.Locator(".message-text")).ToHaveTextAsync("Trailblazer Bike Helmet");
    }

    [Fact]
    public async Task CanSearchManualToGetAnswer()
    {
        var url = await app.StaffWebUI.ResolveUrlAsync("/ticket/1");
        await Page.GotoAsync(url);

        await SuggestionLinks.Nth(0).ClickAsync();

        // See it does a search and gets info from the manual
        var reply = await GetNthCompletedReply(0);
        await Expect(reply.Locator(".search-info")).ToContainTextAsync("safety light troubleshooting");
        await Expect(reply.Locator(".message-text")).ToContainTextAsync("contact Rugged Riders");

        // Also check the link to the manual
        var referenceLink = reply.Locator(".reference-link");
        await Expect(referenceLink).ToContainTextAsync("please follow the steps below");
        var referenceLinkUrl = await referenceLink.GetAttributeAsync("href");
        Assert.StartsWith("manual.html?file=1.pdf&page=7", referenceLinkUrl);
    }

    private async Task SendMessageAsync(string text)
    {
        await WriteMessageTextArea.FillAsync(text);
        await Task.Delay(500);
        await WriteMessageTextArea.PressAsync("Enter");
    }

    private async Task<ILocator> GetNthCompletedReply(int n)
    {
        // Wait for it to be added to the page, and the response to be completed
        await Expect(NthReply(n)).ToBeAttachedAsync();
        await Expect(Page.Locator(".assistant .write-message.in-progress"))
            .ToHaveCountAsync(0, new() { Timeout = 30000 });

        return NthReply(n);
    }
}
