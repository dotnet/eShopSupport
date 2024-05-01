using System.Net.Http.Json;

namespace eShopSupport.ServiceDefaults.Clients.Backend;

public class BackendClient(HttpClient http)
{
    public Task<Ticket[]> GetTicketsAsync()
        => http.GetFromJsonAsync<Ticket[]>("/tickets")!;
}

public record Ticket(
    int TicketId, string CustomerFullName);
