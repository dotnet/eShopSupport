using System.Text.Json;
using eShopSupport.Backend.Data;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.SemanticKernel.Memory;

namespace eShopSupport.Backend.Services;

public class ProductSemanticSearch(ISemanticTextMemory semanticTextMemory)
{
    private const string ProductCollectionName = "products";

    public async Task<IEnumerable<FindProductsResult>> FindProductsAsync(string searchText)
    {
        var results = new List<FindProductsResult>();
        await foreach (var result in semanticTextMemory.SearchAsync(ProductCollectionName, searchText, minRelevanceScore: 0.6, limit: 5))
        {
            // It's a bit weird to get the brand from "description" but MemoryQueryResult doesn't have more structured custom metadata
            results.Add(new FindProductsResult(int.Parse(result.Metadata.Id), result.Metadata.Description, result.Metadata.Text));
        }

        return results;
    }

    public static async Task EnsureSeedDataImportedAsync(IServiceProvider services)
    {
        var importDataFromDir = Environment.GetEnvironmentVariable("ImportInitialDataDir");
        if (!string.IsNullOrEmpty(importDataFromDir))
        {
            using var scope = services.CreateScope();
            await ImportProductSeedDataAsync(importDataFromDir, scope);
        }
    }

    private static async Task ImportProductSeedDataAsync(string importDataFromDir, IServiceScope scope)
    {
        var semanticMemory = scope.ServiceProvider.GetRequiredService<IMemoryStore>();
        var collections = await semanticMemory.GetCollectionsAsync().ToListAsync();

        if (!collections.Contains(ProductCollectionName))
        {
            var products = JsonSerializer.Deserialize<Product[]>(
                File.ReadAllText(Path.Combine(importDataFromDir, "products.json")))!;

            await semanticMemory.CreateCollectionAsync(ProductCollectionName);
            var mappedRecords = products.Select(product =>
            {
                var id = product.ProductId.ToString();
                var metadata = new MemoryRecordMetadata(false, id, product.Model, product.Brand, "", "");
                return new MemoryRecord(metadata, product.NameEmbedding, null);
            });

            await foreach (var _ in semanticMemory.UpsertBatchAsync(ProductCollectionName, mappedRecords)) { }
        }
    }
}
