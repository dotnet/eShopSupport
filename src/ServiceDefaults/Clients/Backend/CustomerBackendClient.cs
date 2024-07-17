using System.Net.Http.Json;
using System.Web;

namespace eShopSupport.ServiceDefaults.Clients.Backend;

public class CustomerBackendClient(HttpClient http)
{
    public Task CreateTicketAsync(CreateTicketRequest request)
        => http.PostAsJsonAsync("/customer/tickets/create", request);

    public Task<ListTicketsResult> ListTicketsAsync()
        => http.GetFromJsonAsync<ListTicketsResult>("/customer/tickets")!;

    public Task<TicketDetailsResult> GetTicketDetailsAsync(int ticketId)
        => http.GetFromJsonAsync<TicketDetailsResult>($"/customer/tickets/{ticketId}")!;

    public Task SendTicketMessageAsync(int ticketId, SendTicketMessageRequest message)
        => http.PostAsJsonAsync($"/api/customer/ticket/{ticketId}/message", message);

    public Task CloseTicketAsync(int ticketId)
        => http.PutAsync($"/api/customer/ticket/{ticketId}/close", null);

    public Task<FindProductsResult[]> FindProductsAsync(string searchText)
        => http.GetFromJsonAsync<FindProductsResult[]>($"/api/customer/products?searchText={HttpUtility.UrlEncode(searchText)}")!;
}
