﻿using System.Net.Http.Json;
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
}

public record ListTicketsResult(ICollection<ListTicketsResultItem> Items, int TotalCount);

public record ListTicketsResultItem(
    int TicketId, string CustomerFullName, string? ShortSummary, int? CustomerSatisfaction, int NumMessages);

public record TicketDetailsResult(
    int TicketId, string CustomerFullName, string? ShortSummary, string? LongSummary,
    int? CustomerSatisfaction, ICollection<TicketDetailsResultMessage> Messages);

public record TicketDetailsResultMessage(int MessageId, string AuthorName, string MessageText);

public record AssistantChatRequest(int TicketId, IReadOnlyList<AssistantChatRequestMessage> Messages);

public class AssistantChatRequestMessage
{
    public bool IsAssistant { get; set; }
    public required string Text { get; set; }
}
