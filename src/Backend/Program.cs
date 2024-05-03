using System.ComponentModel.DataAnnotations;
using eShopSupport.Backend.Data;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<AppDbContext>("backenddb");

var app = builder.Build();
await AppDbContext.EnsureDbCreatedAsync(app.Services);

app.MapGet("/", () => "Hello World!");

app.MapGet("/tickets", async (AppDbContext dbContext, int startIndex, int maxResults, string? sortBy, bool sortAscending) =>
{
    if (maxResults > 100)
    {
        return Results.BadRequest("maxResults must be 100 or less");
    }

    IQueryable<Ticket> itemsMatchingFilter = dbContext.Tickets;

    if (!string.IsNullOrEmpty(sortBy))
    {
        switch (sortBy)
        {
            case "TicketId":
                itemsMatchingFilter = sortAscending == true
                    ? itemsMatchingFilter.OrderBy(t => t.TicketId)
                    : itemsMatchingFilter.OrderByDescending(t => t.TicketId);
                break;
            case "CustomerFullName":
                itemsMatchingFilter = sortAscending == true
                    ? itemsMatchingFilter.OrderBy(t => t.CustomerFullName).ThenBy(t => t.TicketId)
                    : itemsMatchingFilter.OrderByDescending(t => t.CustomerFullName).ThenBy(t => t.TicketId);
                break;
            case "NumMessages":
                itemsMatchingFilter = sortAscending == true
                    ? itemsMatchingFilter.OrderBy(t => t.Messages.Count).ThenBy(t => t.TicketId)
                    : itemsMatchingFilter.OrderByDescending(t => t.Messages.Count).ThenBy(t => t.TicketId);
                break;
            default:
                return Results.BadRequest("Invalid sortBy value");
        }
    }

    var resultItems = itemsMatchingFilter
        .Skip(startIndex)
        .Take(maxResults)
        .Select(t => new ListTicketsResultItem(t.TicketId, t.CustomerFullName, t.ShortSummary, t.CustomerSatisfaction, t.Messages.Count));
    return Results.Ok(new ListTicketsResult(await resultItems.ToListAsync(), await itemsMatchingFilter.CountAsync()));
});

app.Run();
