using eShopSupport.Backend.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddNpgsqlDbContext<AppDbContext>("backenddb");

var app = builder.Build();
await AppDbContext.EnsureDbCreatedAsync(app.Services);

app.MapGet("/", () => "Hello World!");

app.MapGet("/tickets", (AppDbContext dbContext) => dbContext.Tickets.ToListAsync());

app.Run();
