using eShopSupport.Backend.Data;
using eShopSupport.Backend.Services;
using eShopSupport.ServiceDefaults.Clients.Backend;

namespace eShopSupport.Backend.Api;

public static class TicketMessagingApi
{
    public static void MapTicketMessagingApiEndpoints(this WebApplication app)
    {
        app.MapPost("/api/ticket/{ticketId}/message", PostMessageAsync);
    }

    private static async Task<IResult> PostMessageAsync(int ticketId, AppDbContext dbContext, TicketSummarizer summarizer, ILoggerFactory loggerFactory, CancellationToken cancellationToken, SendTicketMessageRequest sendRequest)
    {
        dbContext.Messages.Add(new Message
        {
            TicketId = ticketId,
            CreatedAt = DateTime.UtcNow,
            IsCustomerMessage = sendRequest.IsCustomerMessage,
            Text = sendRequest.Text,
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        // Runs in the background and notifies when the summary is updated
        summarizer.UpdateSummary(ticketId);

        return Results.Ok();
    }
}
