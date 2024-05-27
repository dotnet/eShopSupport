using eShopSupport.Backend.Data;
using eShopSupport.ServiceDefaults.Clients.Backend;

namespace eShopSupport.Backend.Api;

public static class TicketMessaging
{
    public static void MapTicketMessagingEndpoints(this WebApplication app)
    {
        app.MapPost("/api/ticket/{ticketId}/message", async (int ticketId, AppDbContext dbContext, ILoggerFactory loggerFactory, CancellationToken cancellationToken, SendTicketMessageRequest sendRequest) =>
        {
            dbContext.Messages.Add(new Message
            {
                TicketId = ticketId,
                CreatedAt = DateTime.UtcNow,
                IsCustomerMessage = sendRequest.IsCustomerMessage,
                Text = sendRequest.Text,
            });
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok();
        });
    }
}
