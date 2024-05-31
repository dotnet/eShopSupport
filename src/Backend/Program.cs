using eShopSupport.Backend.Api;
using eShopSupport.Backend.Data;
using eShopSupport.Backend.Services;
using eShopSupport.ServiceDefaults.Clients.PythonInference;
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
builder.Services.AddScoped<TicketSummarizer>();
builder.Services.AddHttpClient<PythonInferenceClient>(c => c.BaseAddress = new Uri("http://python-inference"));
builder.AddAzureBlobClient("eshopsupport-blobs");

builder.AddChatCompletionService("chatcompletion", Environment.GetEnvironmentVariable("E2E_TEST_CHAT_COMPLETION_CACHE_DIR"));
builder.AddRedisClient("redis");

var app = builder.Build();
await AppDbContext.EnsureDbCreatedAsync(app.Services);
await ProductSemanticSearch.EnsureSeedDataImportedAsync(app.Services);
await ProductManualSemanticSearch.EnsureSeedDataImportedAsync(app.Services);

app.MapAssistantApiEndpoints();
app.MapTicketApiEndpoints();
app.MapTicketMessagingApiEndpoints();
app.MapCatalogApiEndpoints();

app.Run();
