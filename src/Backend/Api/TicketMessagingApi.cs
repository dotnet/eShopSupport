using CustomerWebUI;
using eShopSupport.Backend.Data;
using eShopSupport.Backend.Services;
using eShopSupport.ServiceDefaults.Clients.Backend;

namespace eShopSupport.Backend.Api;

public static class TicketMessagingApi
{
    public static void MapTicketMessagingApiEndpoints(this WebApplication app)
    {
        app.MapPost("/api/ticket/{ticketId}/message", async (int ticketId, AppDbContext dbContext, TicketSummarizer summarizer, SendTicketMessageRequest request) =>
        {
            // Staff can post messages on any ticket, so we don't have to validate ticketId
            await PostMessageAsync(ticketId, dbContext, summarizer, request, isCustomerMessage: false);
        });

        app.MapPost("/api/customer/ticket/{ticketId}/message", async (HttpContext httpContext, int ticketId, AppDbContext dbContext, TicketSummarizer summarizer, SendTicketMessageRequest request) =>
        {
            // Since this is a customer API call, verify this is their ticket
            var ticket = await dbContext.Tickets.FindAsync(ticketId);
            if (ticket?.CustomerId != httpContext.GetRequiredCustomerId())
            {
                return Results.NotFound();
            }

            await PostMessageAsync(ticketId, dbContext, summarizer, request, isCustomerMessage: true);
            return Results.Ok();
        }).RequireAuthorization("CustomerApi");
    }

    private static async Task PostMessageAsync(int ticketId, AppDbContext dbContext, TicketSummarizer summarizer, SendTicketMessageRequest request, bool isCustomerMessage)
    {
        dbContext.Messages.Add(new Message
        {
            TicketId = ticketId,
            CreatedAt = DateTime.UtcNow,
            IsCustomerMessage = isCustomerMessage,
            Text = request.Text,
        });
        await dbContext.SaveChangesAsync();

        // Runs in the background and notifies when the summary is updated
        summarizer.UpdateSummary(ticketId, enforceRateLimit: isCustomerMessage);
    }
}
