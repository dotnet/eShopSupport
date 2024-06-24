using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Web;

namespace eShopSupport.ServiceDefaults.Clients.Backend;

public class BackendClient(HttpClient http)
{
    public Task CreateTicketAsync(CreateTicketRequest request)
        => http.PostAsJsonAsync("/tickets/create", request);

    public async Task<ListTicketsResult> ListTicketsAsync(ListTicketsRequest request)
    {
        var result = await http.PostAsJsonAsync("/tickets", request);
        return (await result.Content.ReadFromJsonAsync<ListTicketsResult>())!;
    }

    public Task<TicketDetailsResult> GetTicketDetailsAsync(int ticketId)
        => http.GetFromJsonAsync<TicketDetailsResult>($"/tickets/{ticketId}")!;

    public async IAsyncEnumerable<AssistantChatReplyItem> AssistantChatAsync(AssistantChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/chat")
        {
            Content = JsonContent.Create(request),
        };
        var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<AssistantChatReplyItem>(stream, cancellationToken: cancellationToken))
        {
            if (item is not null)
            {
                yield return item;
            }
        }
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

    public async Task CloseTicketAsync(int ticketId)
    {
        await http.PutAsync($"/api/ticket/{ticketId}/close", null);
    }

    public async Task UpdateTicketDetailsAsync(int ticketId, int? productId, TicketType ticketType, TicketStatus ticketStatus)
    {
        await http.PutAsJsonAsync($"/api/ticket/{ticketId}", new UpdateTicketDetailsRequest(productId, ticketType, ticketStatus));
    }

    public Task<FindCategoriesResult[]> FindCategoriesAsync(string searchText)
    {
        return http.GetFromJsonAsync<FindCategoriesResult[]>($"/api/categories?searchText={HttpUtility.UrlEncode(searchText)}")!;
    }

    public Task<FindCategoriesResult[]> FindCategoriesAsync(IEnumerable<int> categoryIds)
    {
        return http.GetFromJsonAsync<FindCategoriesResult[]>($"/api/categories?ids={string.Join(",", categoryIds)}")!;
    }

    public Task<FindProductsResult[]> FindProductsAsync(string searchText)
    {
        return http.GetFromJsonAsync<FindProductsResult[]>($"/api/products?searchText={HttpUtility.UrlEncode(searchText)}")!;
    }
}

public record ListTicketsRequest(TicketStatus? FilterByStatus, List<int>? FilterByCategoryIds, int? FilterByCustomerId, int StartIndex, int MaxResults, string? SortBy, bool? SortAscending);

public record ListTicketsResult(ICollection<ListTicketsResultItem> Items, int TotalCount, int TotalOpenCount, int TotalClosedCount);

public record ListTicketsResultItem(
    int TicketId, TicketType TicketType, TicketStatus TicketStatus, DateTime CreatedAt, string CustomerFullName, string? ProductName, string? ShortSummary, int? CustomerSatisfaction, int NumMessages);

public record TicketDetailsResult(
    int TicketId, DateTime CreatedAt, int CustomerId, string CustomerFullName, string? ShortSummary, string? LongSummary,
    int? ProductId, string? ProductBrand, string? ProductModel,
    TicketType TicketType, TicketStatus TicketStatus,
    int? CustomerSatisfaction, ICollection<TicketDetailsResultMessage> Messages);

public record TicketDetailsResultMessage(int MessageId, DateTime CreatedAt, bool IsCustomerMessage, string MessageText);

public record UpdateTicketDetailsRequest(int? ProductId, TicketType TicketType, TicketStatus TicketStatus);

public record AssistantChatRequest(
    int? ProductId,
    string? CustomerName,
    string? TicketSummary,
    string? TicketLastCustomerMessage,
    IReadOnlyList<AssistantChatRequestMessage> Messages);

public class AssistantChatRequestMessage
{
    public bool IsAssistant { get; set; }
    public required string Text { get; set; }
}

public record AssistantChatReplyItem(AssistantChatReplyItemType Type, string Text, int? SearchResultId = null, int? SearchResultProductId = null, int? SearchResultPageNumber = null);

public enum AssistantChatReplyItemType { AnswerChunk, Search, SearchResult, IsAddressedToCustomer };

public record SendTicketMessageRequest(string Text, bool IsCustomerMessage);

public record FindCategoriesResult(int CategoryId)
{
    public required string Name { get; set; }
}

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

public record CreateTicketRequest(
    int CustomerId, 
    string? ProductName,
    string Message);
