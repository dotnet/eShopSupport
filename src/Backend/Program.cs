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

app.MapGet("/tickets", (AppDbContext dbContext) => dbContext.Tickets.Select(t =>
    new ListTicketsResult(t.TicketId, t.CustomerFullName, t.Messages.Count)).ToListAsync());

app.Run();
