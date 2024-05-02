using System.Collections;
using System.Net.Http.Json;

namespace eShopSupport.ServiceDefaults.Clients.Backend;

public class BackendClient(HttpClient http)
{
    public Task<ListTicketsResult> ListTicketsAsync(int startIndex, int maxResults, string? sortBy, bool? sortAscending)
        => http.GetFromJsonAsync<ListTicketsResult>($"/tickets?startIndex={startIndex}&maxResults={maxResults}&sortBy={sortBy}&sortAscending={sortAscending}")!;
}

public record ListTicketsResult(ICollection<ListTicketsResultItem> Items, int TotalCount);

public record ListTicketsResultItem(
    int TicketId, string CustomerFullName, int NumMessages);
