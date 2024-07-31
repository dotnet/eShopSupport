using System.Configuration;
using System.Data.Common;
using Azure.AI.OpenAI;
using eShopSupport.Backend.Api;
using eShopSupport.Backend.Data;
using eShopSupport.Backend.Services;
using eShopSupport.ServiceDefaults.Clients.PythonInference;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
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

builder.AddChatCompletionService("chatcompletion", builder.Configuration["E2E_TEST_CHAT_COMPLETION_CACHE_DIR"]);
builder.AddRedisClient("redis");

builder.Services.AddKeyedSingleton("oagents", (sp, _) =>
{
    var client = sp.GetRequiredService<OpenAIClient>();
    var connectionStringBuilder = new DbConnectionStringBuilder();
     //TODO read from config
    // connectionStringBuilder.ConnectionString = builder.Configuration.GetConnectionString(name);
    //         if (!connectionStringBuilder.TryGetValue("Deployment", out var deploymentName))
    //         {
    //             throw new InvalidOperationException($"The connection string named '{name}' does not specify a value for 'Deployment', but this is required.");
    //         }
    var deploymentName = "gpt-4o";
    var builder = Kernel.CreateBuilder();
    builder.AddAzureOpenAIChatCompletion(deploymentName,client);
    return builder.Build();
});

builder.UseOrleans();

JsonWebTokenHandler.DefaultInboundClaimTypeMap.Remove("sub");

builder.Services.AddAuthentication().AddJwtBearer(options =>
{
    options.Authority = builder.Configuration["IdentityUrl"];
    options.TokenValidationParameters.ValidateAudience = false;
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CustomerApi", policy => policy.RequireAuthenticatedUser())
    .AddFallbackPolicy("StaffApi", policy => policy.RequireRole("staff"));

var app = builder.Build();

var initialImportDataDir = builder.Configuration["ImportInitialDataDir"];
await AppDbContext.EnsureDbCreatedAsync(app.Services, initialImportDataDir);
await ProductSemanticSearch.EnsureSeedDataImportedAsync(app.Services, initialImportDataDir);
await ProductManualSemanticSearch.EnsureSeedDataImportedAsync(app.Services, initialImportDataDir);

app.MapAssistantApiEndpoints();
app.MapTicketApiEndpoints();
app.MapTicketMessagingApiEndpoints();
app.MapCatalogApiEndpoints();

app.Run();
