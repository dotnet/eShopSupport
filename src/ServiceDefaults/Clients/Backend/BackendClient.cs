using System.Net.Http.Json;

namespace eShopSupport.ServiceDefaults.Clients.Backend;

public class BackendClient(HttpClient http)
{
    public Task<ListTicketsResult> ListTicketsAsync(int startIndex, int maxResults, string? sortBy, bool? sortAscending)
        => http.GetFromJsonAsync<ListTicketsResult>($"/tickets?startIndex={startIndex}&maxResults={maxResults}&sortBy={sortBy}&sortAscending={sortAscending}")!;

    public Task<TicketDetailsResult> GetTicketDetailsAsync(int ticketId)
        => http.GetFromJsonAsync<TicketDetailsResult>($"/tickets/{ticketId}")!;

    public async Task<AssistantChatResponse> AssistantChatAsync(AssistantChatRequest request)
    {
        var response = await http.PostAsJsonAsync("/api/assistant/chat", request);
        return (await response.Content.ReadFromJsonAsync<AssistantChatResponse>())!;
    }

}

public record ListTicketsResult(ICollection<ListTicketsResultItem> Items, int TotalCount);

public record ListTicketsResultItem(
    int TicketId, string CustomerFullName, string? ShortSummary, int? CustomerSatisfaction, int NumMessages);

public record TicketDetailsResult(
    int TicketId, string CustomerFullName, string? ShortSummary, string? LongSummary,
    int? CustomerSatisfaction, ICollection<TicketDetailsResultMessage> Messages);

public record TicketDetailsResultMessage(int MessageId, string AuthorName, string MessageText);

public record AssistantChatRequest(string Message);

public record AssistantChatResponse(string Reply);
