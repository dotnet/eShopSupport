using Azure.Storage.Blobs;
using eShopSupport.Backend.Api;
using eShopSupport.Backend.Data;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel.Connectors.Qdrant;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;
using SmartComponents.LocalEmbeddings.SemanticKernel;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<AppDbContext>("backenddb");
builder.Services.AddScoped(s =>
{
    var httpClient = s.GetRequiredService<HttpClient>();
    httpClient.BaseAddress = new Uri("http://vector-db");
    return new QdrantMemoryStore(httpClient, 384);
});
builder.Services.AddScoped<IMemoryStore>(s => s.GetRequiredService<QdrantMemoryStore>());
builder.Services.AddScoped<ITextEmbeddingGenerationService, LocalTextEmbeddingGenerationService>();
builder.Services.AddScoped<ISemanticTextMemory, SemanticTextMemory>();
builder.Services.AddScoped<ProductSemanticSearch>();
builder.Services.AddScoped<ProductManualSemanticSearch>();
builder.AddAzureBlobClient("eshopsupport-blobs");

builder.AddChatCompletionService("chatcompletion");

var app = builder.Build();
await AppDbContext.EnsureDbCreatedAsync(app.Services);
await ProductSemanticSearch.EnsureSeedDataImportedAsync(app.Services);
await ProductManualSemanticSearch.EnsureSeedDataImportedAsync(app.Services);

app.MapGet("/", () => "Hello World!");

app.MapGet("/tickets/{ticketId:int}", async (AppDbContext dbContext, int ticketId) =>
{
    var ticket = await dbContext.Tickets
        .Include(t => t.Messages)
        .Include(t => t.Product)
        .FirstOrDefaultAsync(t => t.TicketId == ticketId);
    return ticket == null ? Results.NotFound() : Results.Ok(new TicketDetailsResult(
        ticket.TicketId,
        ticket.CustomerFullName,
        ticket.ShortSummary,
        ticket.LongSummary,
        ticket.ProductId,
        ticket.Product?.Brand,
        ticket.Product?.Model,
        ticket.TicketType,
        ticket.TicketStatus,
        ticket.CustomerSatisfaction,
        ticket.Messages.OrderBy(m => m.MessageId).Select(m => new TicketDetailsResultMessage(m.MessageId, m.AuthorName, m.Text)).ToList()
    ));
});

app.MapPut("/api/ticket/{ticketId:int}", async (AppDbContext dbContext, int ticketId, UpdateTicketDetailsRequest request) =>
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
    return Results.Ok();
});

app.MapPost("/tickets", async (AppDbContext dbContext, ListTicketsRequest request) =>
{
    if (request.MaxResults > 100)
    {
        return Results.BadRequest("maxResults must be 100 or less");
    }

    IQueryable<Ticket> itemsMatchingFilter = dbContext.Tickets;

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
                    ? itemsMatchingFilter.OrderBy(t => t.CustomerFullName).ThenBy(t => t.TicketId)
                    : itemsMatchingFilter.OrderByDescending(t => t.CustomerFullName).ThenBy(t => t.TicketId);
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
        .Select(t => new ListTicketsResultItem(t.TicketId, t.TicketType, t.CustomerFullName, t.ShortSummary, t.CustomerSatisfaction, t.Messages.Count));

    return Results.Ok(new ListTicketsResult(await resultItems.ToListAsync(), await itemsMatchingFilter.CountAsync(), totalOpen, totalClosed));
});

app.MapGet("/manual", async (string file, BlobServiceClient blobServiceClient) =>
{
    var blobClient = blobServiceClient.GetBlobContainerClient("manuals").GetBlobClient(file);
    if (!(await blobClient.ExistsAsync()))
    {
        return Results.NotFound();
    }

    var download = await blobClient.DownloadStreamingAsync();
    return Results.File(download.Value.Content, "application/pdf");
});

app.MapGet("/api/products", (ProductSemanticSearch productSemanticSearch, string searchText) =>
{
    return productSemanticSearch.FindProductsAsync(searchText);
});

app.MapAssistantEndpoints();
app.MapTicketMessagingEndpoints();

app.Run();
