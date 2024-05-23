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
builder.Services.AddScoped<ProductManualSemanticSearch>();
builder.AddAzureBlobClient("eshopsupport-blobs");

builder.AddChatCompletionService("chatcompletion");

var app = builder.Build();
await AppDbContext.EnsureDbCreatedAsync(app.Services);
await ProductManualSemanticSearch.EnsureSeedDataImportedAsync(app.Services);

app.MapGet("/", () => "Hello World!");

app.MapGet("/tickets/{ticketId:int}", async (AppDbContext dbContext, int ticketId) =>
{
    var ticket = await dbContext.Tickets
        .Include(t => t.Messages)
        .FirstOrDefaultAsync(t => t.TicketId == ticketId);
    return ticket == null ? Results.NotFound() : Results.Ok(new TicketDetailsResult(
        ticket.TicketId,
        ticket.CustomerFullName,
        ticket.ShortSummary,
        ticket.LongSummary,
        ticket.TicketType,
        ticket.TicketStatus,
        ticket.CustomerSatisfaction,
        ticket.Messages.OrderBy(m => m.MessageId).Select(m => new TicketDetailsResultMessage(m.MessageId, m.AuthorName, m.Text)).ToList()
    ));
});

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

app.MapAssistantEndpoints();
app.MapTicketMessagingEndpoints();

app.Run();
