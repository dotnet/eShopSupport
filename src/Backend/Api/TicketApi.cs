using System.Text.RegularExpressions;
using CustomerWebUI;
using eShopSupport.Backend.Data;
using eShopSupport.Backend.Services;
using eShopSupport.ServiceDefaults.Clients.Backend;
using eShopSupport.ServiceDefaults.Clients.PythonInference;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace eShopSupport.Backend.Api;

public static class TicketApi
{
    public static void MapTicketApiEndpoints(this WebApplication app)
    {
        // Staff endpoints. Fallback policy required "staff" role.
        app.MapPost("/tickets", ListTicketsAsync).RequireAuthorization("CustomerApi");
        app.MapGet("/tickets/{ticketId:int}", GetTicketAsync);
        app.MapPut("/api/ticket/{ticketId:int}", UpdateTicketAsync);
        app.MapPut("/api/ticket/{ticketId:int}/close", CloseTicketAsync);
        
        // Customer endpoints. These must each take care to restrict access to the customer's own tickets.
        var customerApiPolicy = "CustomerApi";

        app.MapGet("/customer/tickets", (HttpContext httpContext, AppDbContext dbContext) =>
            ListTicketsAsync(dbContext, new(null, null, httpContext.GetRequiredCustomerId(), 0, 100, nameof(ListTicketsResultItem.TicketId), false)))
            .RequireAuthorization(customerApiPolicy);

        app.MapPost("/customer/tickets/create", CreateTicketAsync)
            .RequireAuthorization(customerApiPolicy);
    }

    private static async Task<IResult> ListTicketsAsync(AppDbContext dbContext, ListTicketsRequest request)
    {
        if (request.MaxResults > 100)
        {
            return Results.BadRequest("maxResults must be 100 or less");
        }

        IQueryable<Ticket> itemsMatchingFilter = dbContext.Tickets
            .Include(t => t.Product);

        if (request.FilterByCategoryIds is { Count: > 0 })
        {
            itemsMatchingFilter = itemsMatchingFilter
                .Where(t => t.Product != null)
                .Where(t => request.FilterByCategoryIds.Contains(t.Product!.CategoryId));
        }

        if (request.FilterByCustomerId is int customerId)
        {
            itemsMatchingFilter = itemsMatchingFilter.Where(t => t.CustomerId == customerId);
        }

        // Count open/closed
        var itemsMatchingFilterCountByStatus = await itemsMatchingFilter.GroupBy(t => t.TicketStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count);
        var totalOpen = itemsMatchingFilterCountByStatus.GetValueOrDefault(TicketStatus.Open);
        var totalClosed = itemsMatchingFilterCountByStatus.GetValueOrDefault(TicketStatus.Closed);

        // Sort and return requested range of
        if (request.FilterByStatus.HasValue)
        {
            itemsMatchingFilter = itemsMatchingFilter.Where(t => t.TicketStatus == request.FilterByStatus.Value);
        }

        if (!string.IsNullOrEmpty(request.SortBy))
        {
            switch (request.SortBy)
            {
                case nameof(ListTicketsResultItem.TicketId):
                    itemsMatchingFilter = request.SortAscending == true
                        ? itemsMatchingFilter.OrderBy(t => t.TicketId)
                        : itemsMatchingFilter.OrderByDescending(t => t.TicketId);
                    break;
                case nameof(ListTicketsResultItem.CustomerFullName):
                    itemsMatchingFilter = request.SortAscending == true
                        ? itemsMatchingFilter.OrderBy(t => t.Customer.FullName).ThenBy(t => t.TicketId)
                        : itemsMatchingFilter.OrderByDescending(t => t.Customer.FullName).ThenBy(t => t.TicketId);
                    break;
                case nameof(ListTicketsResultItem.NumMessages):
                    itemsMatchingFilter = request.SortAscending == true
                        ? itemsMatchingFilter.OrderBy(t => t.Messages.Count).ThenBy(t => t.TicketId)
                        : itemsMatchingFilter.OrderByDescending(t => t.Messages.Count).ThenBy(t => t.TicketId);
                    break;
                case nameof(ListTicketsResultItem.CustomerSatisfaction):
                    itemsMatchingFilter = request.SortAscending == true
                        ? itemsMatchingFilter.OrderBy(t => t.CustomerSatisfaction).ThenBy(t => t.TicketId)
                        : itemsMatchingFilter.OrderByDescending(t => t.CustomerSatisfaction).ThenBy(t => t.TicketId);
                    break;
                default:
                    return Results.BadRequest("Invalid sortBy value");
            }
        }

        var resultItems = itemsMatchingFilter
            .Skip(request.StartIndex)
            .Take(request.MaxResults)
            .Select(t => new ListTicketsResultItem(t.TicketId, t.TicketType, t.TicketStatus, t.CreatedAt, t.Customer.FullName, t.Product == null ? null : t.Product.Model, t.ShortSummary, t.CustomerSatisfaction, t.Messages.Count));

        return Results.Ok(new ListTicketsResult(await resultItems.ToListAsync(), await itemsMatchingFilter.CountAsync(), totalOpen, totalClosed));
    }

    private static async Task<IResult> GetTicketAsync(AppDbContext dbContext, int ticketId)
    {
        var ticket = await dbContext.Tickets
            .Include(t => t.Messages)
            .Include(t => t.Product)
            .Include(t => t.Customer)
            .FirstOrDefaultAsync(t => t.TicketId == ticketId);
        return ticket == null ? Results.NotFound() : Results.Ok(new TicketDetailsResult(
            ticket.TicketId,
            ticket.CreatedAt,
            ticket.CustomerId,
            ticket.Customer.FullName,
            ticket.ShortSummary,
            ticket.LongSummary,
            ticket.ProductId,
            ticket.Product?.Brand,
            ticket.Product?.Model,
            ticket.TicketType,
            ticket.TicketStatus,
            ticket.CustomerSatisfaction,
            ticket.Messages.OrderBy(m => m.MessageId).Select(m => new TicketDetailsResultMessage(m.MessageId, m.CreatedAt, m.IsCustomerMessage, m.Text)).ToList()
        ));
    }

    private static async Task<IResult> UpdateTicketAsync(AppDbContext dbContext, IConnectionMultiplexer redisConnection, int ticketId, UpdateTicketDetailsRequest request)
    {
        var ticket = await dbContext.Tickets.FirstOrDefaultAsync(t => t.TicketId == ticketId);
        if (ticket == null)
        {
            return Results.NotFound();
        }

        ticket.ProductId = request.ProductId;
        ticket.TicketType = request.TicketType;
        ticket.TicketStatus = request.TicketStatus;
        await dbContext.SaveChangesAsync();

        await redisConnection.GetSubscriber().PublishAsync(
            RedisChannel.Literal($"ticket:{ticketId}"), "Updated");

        return Results.Ok();
    }

    private static async Task<IResult> CloseTicketAsync(AppDbContext dbContext, int ticketId)
    {
        var ticket = await dbContext.Tickets.FirstOrDefaultAsync(t => t.TicketId == ticketId);
        if (ticket == null)
        {
            return Results.NotFound();
        }

        ticket.TicketStatus = TicketStatus.Closed;
        await dbContext.SaveChangesAsync();
        return Results.Ok();
    }

    private static async Task CreateTicketAsync(HttpContext httpContext, AppDbContext dbContext, TicketSummarizer summarizer, PythonInferenceClient pythonInference, CreateTicketRequest request)
    {
        // Classify the new ticket using the small zero-shot classifier model
        var ticketTypes = Enum.GetValues<TicketType>();
        var inferredTicketType = await pythonInference.ClassifyTextAsync(
            request.Message,
            candidateLabels: ticketTypes.Select(type => type.ToString()));

        var ticket = new Ticket
        {
            CreatedAt = DateTime.UtcNow,
            CustomerId = httpContext.GetRequiredCustomerId(),
            Customer = default!, // Will be populated by DB reference
            TicketStatus = TicketStatus.Open,
            TicketType = Enum.TryParse<TicketType>(inferredTicketType, out var type) ? type : TicketType.Question,
        };

        // TODO: Better lookup using ID
        if (!string.IsNullOrEmpty(request.ProductName)
            && Regex.Match(request.ProductName, @"^(.*) \((.*)\)$") is { Success: true } match)
        {
            var brand = match.Groups[2].Value;
            var model = match.Groups[1].Value;
            var product = await dbContext.Products.FirstOrDefaultAsync(p => p.Brand == brand && p.Model == model);
            ticket.ProductId = product?.ProductId;
        }

        ticket.Messages.Add(new Message
        {
            IsCustomerMessage = true,
            Text = request.Message,
            CreatedAt = DateTime.UtcNow,
        });

        dbContext.Tickets.Add(ticket);
        await dbContext.SaveChangesAsync();

        summarizer.UpdateSummary(ticket.TicketId);
    }
}
