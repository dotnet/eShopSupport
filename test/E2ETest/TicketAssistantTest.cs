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
        Assert.Equal("Trailblazer Bike Helmet", await GetNthReplyText(0));
    }

    private async Task SendMessageAsync(string text)
    {
        await WriteMessageTextArea.FillAsync(text);
        await WriteMessageTextArea.PressAsync("Enter");
    }

    private async Task<string?> GetNthReplyText(int n)
    {
        // Wait for it to be added to the page, and the response to be completed
        await Expect(NthReply(n)).ToBeAttachedAsync();
        await Expect(Page.Locator(".assistant .write-message.in-progress"))
            .ToHaveCountAsync(0, new() { Timeout = 30000 });

        return await NthReply(n).Locator(".message-text").TextContentAsync();
    }
}
