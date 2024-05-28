using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using eShopSupport.Backend.Api;
using eShopSupport.Backend.Data;
using eShopSupport.ServiceDefaults.Clients.Backend;
using eShopSupport.ServiceDefaults.Clients.PythonInference;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel.Connectors.Qdrant;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;
using SmartComponents.LocalEmbeddings.SemanticKernel;
using StackExchange.Redis;

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
builder.Services.AddHttpClient<PythonInferenceClient>(c => c.BaseAddress = new Uri("http://python-inference"));
builder.AddAzureBlobClient("eshopsupport-blobs");

builder.AddChatCompletionService("chatcompletion");
builder.AddRedisClient("redis");

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
});

app.MapPut("/api/ticket/{ticketId:int}", async (AppDbContext dbContext, IConnectionMultiplexer redisConnection, int ticketId, UpdateTicketDetailsRequest request) =>
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
});

app.MapPut("/api/ticket/{ticketId:int}/close", async (AppDbContext dbContext, int ticketId) =>
{
    var ticket = await dbContext.Tickets.FirstOrDefaultAsync(t => t.TicketId == ticketId);
    if (ticket == null)
    {
        return Results.NotFound();
    }

    ticket.TicketStatus = TicketStatus.Closed;
    await dbContext.SaveChangesAsync();
    return Results.Ok();
});

app.MapPost("/tickets/create", async (AppDbContext dbContext, IConnectionMultiplexer redisConnection, PythonInferenceClient pythonInference, CreateTicketRequest request) =>
{
    // Classify the new ticket
    var ticketTypes = Enum.GetValues<TicketType>();
    var inferredTicketType = await pythonInference.ClassifyTextAsync(request.Message, ticketTypes.Select(type => type.ToString()));

    var ticket = new Ticket
    {
        CreatedAt = DateTime.UtcNow,
        CustomerId = request.CustomerId,
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

    await redisConnection.GetSubscriber().PublishAsync(
        RedisChannel.Literal($"ticket:{ticket.TicketId}"), "Updated");
});

app.MapPost("/tickets", async (AppDbContext dbContext, ListTicketsRequest request) =>
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

app.MapGet("/api/categories", async (AppDbContext dbContext, ITextEmbeddingGenerationService embedder, string? searchText, string? ids) =>
{
    IQueryable<ProductCategory> filteredCategories = dbContext.ProductCategories;

    if (!string.IsNullOrWhiteSpace(ids))
    {
        var idsParsed = ids.Split(',').Select(int.Parse).ToList();
        filteredCategories = filteredCategories.Where(c => idsParsed.Contains(c.CategoryId));
    }

    var matchingCategories = await filteredCategories.ToArrayAsync();  

    // If you have a small number of items, another pattern for semantic search is simply
    // to do it in process. In this case we also amend the similarity rule so that if the
    // category is an exact prefix match, it's considered a perfect match. So in effect
    // we have both a prefix match and a semantic match working together.
    if (!string.IsNullOrWhiteSpace(searchText))
    {
        var searchTextEmbedding = await embedder.GenerateEmbeddingAsync(searchText);
        matchingCategories = matchingCategories.Select(c => new
        {
            Category = c,
            Similarity = c.Name.StartsWith(searchText, StringComparison.OrdinalIgnoreCase)
                ? 1f
                : TensorPrimitives.CosineSimilarity(FromBase64(c.NameEmbeddingBase64), searchTextEmbedding.Span),
        }).Where(x => x.Similarity > 0.5f)
        .OrderByDescending(x => x.Similarity)
        .Take(5)
        .Select(x => x.Category)
        .ToArray();
    }

    return matchingCategories.Select(c => new FindCategoriesResult(c.CategoryId) { Name = c.Name });

    static ReadOnlySpan<float> FromBase64(string embeddingBase64)
    {
        var bytes = Convert.FromBase64String(embeddingBase64);
        return MemoryMarshal.Cast<byte, float>(bytes);
    }
});

app.MapGet("/api/products", (ProductSemanticSearch productSemanticSearch, string searchText) =>
{
    return productSemanticSearch.FindProductsAsync(searchText);
});

app.MapAssistantEndpoints();
app.MapTicketMessagingEndpoints();

app.Run();
