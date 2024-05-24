using System.Net.Http.Json;
using System.Net.Sockets;
using System.Web;

namespace eShopSupport.ServiceDefaults.Clients.Backend;

public class BackendClient(HttpClient http)
{
    public Task<ListTicketsResult> ListTicketsAsync(int startIndex, int maxResults, string? sortBy, bool? sortAscending)
        => http.GetFromJsonAsync<ListTicketsResult>($"/tickets?startIndex={startIndex}&maxResults={maxResults}&sortBy={sortBy}&sortAscending={sortAscending}")!;

    public Task<TicketDetailsResult> GetTicketDetailsAsync(int ticketId)
        => http.GetFromJsonAsync<TicketDetailsResult>($"/tickets/{ticketId}")!;

    public async Task<Stream> AssistantChatAsync(AssistantChatRequest request, CancellationToken cancellationToken)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/chat")
        {
            Content = JsonContent.Create(request),
        };
        var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    public async Task<Stream?> ReadManualAsync(string file, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/manual?file={HttpUtility.UrlEncode(file)}");
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStreamAsync(cancellationToken) : null;
    }

    public async Task SendTicketMessageAsync(int ticketId, SendTicketMessageRequest message)
    {
        await http.PostAsJsonAsync($"/api/ticket/{ticketId}/message", message);
    }

    public async Task UpdateTicketDetailsAsync(int ticketId, int? productId, TicketType ticketType, TicketStatus ticketStatus)
    {
        await http.PutAsJsonAsync($"/api/ticket/{ticketId}", new UpdateTicketDetailsRequest(productId, ticketType, ticketStatus));
    }

    public Task<FindProductsResult[]> FindProductsAsync(string searchText)
    {
        return http.GetFromJsonAsync<FindProductsResult[]>($"/api/products?searchText={HttpUtility.UrlEncode(searchText)}")!;
    }
}

public record ListTicketsResult(ICollection<ListTicketsResultItem> Items, int TotalCount);

public record ListTicketsResultItem(
    int TicketId, TicketType TicketType, string CustomerFullName, string? ShortSummary, int? CustomerSatisfaction, int NumMessages);

public record TicketDetailsResult(
    int TicketId, string CustomerFullName, string? ShortSummary, string? LongSummary,
    int? ProductId, string? ProductBrand, string? ProductModel,
    TicketType TicketType, TicketStatus TicketStatus,
    int? CustomerSatisfaction, ICollection<TicketDetailsResultMessage> Messages);

public record TicketDetailsResultMessage(int MessageId, string AuthorName, string MessageText);

public record UpdateTicketDetailsRequest(int? ProductId, TicketType TicketType, TicketStatus TicketStatus);

public record AssistantChatRequest(int TicketId, IReadOnlyList<AssistantChatRequestMessage> Messages);

public class AssistantChatRequestMessage
{
    public bool IsAssistant { get; set; }
    public required string Text { get; set; }
}

public record SendTicketMessageRequest(string Text);

public record FindProductsResult(int ProductId, string Brand, string Model);

public enum TicketStatus
{
    Open,
    Closed,
}

public enum TicketType
{
    Question,
    Idea,
    Complaint,
    Returns,
}
