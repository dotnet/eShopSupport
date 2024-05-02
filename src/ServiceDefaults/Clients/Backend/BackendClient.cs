using System.Net.Http.Json;

namespace eShopSupport.ServiceDefaults.Clients.Backend;

public class BackendClient(HttpClient http)
{
    public Task<ListTicketsResult[]> ListTicketsAsync()
        => http.GetFromJsonAsync<ListTicketsResult[]>("/tickets")!;
}

public record ListTicketsResult(
    int TicketId, string CustomerFullName, int NumMessages);
