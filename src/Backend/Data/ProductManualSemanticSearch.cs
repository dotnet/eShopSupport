using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;

namespace eShopSupport.Backend.Data;

public class ProductManualSemanticSearch(ITextEmbeddingGenerationService embedder, HttpClient httpClient)
{
    private const string ManualCollectionName = "manuals";
    private const string ProductCollectionName = "products";

    public async Task<IReadOnlyList<MemoryQueryResult>> SearchAsync(int? productId, string query)
    {
        var embedding = await embedder.GenerateEmbeddingAsync(query);
        var filter = !productId.HasValue
            ? null
            : new
            {
                must = new[]
                {
                    new { key = "external_source_name", match = new { value = $"productid:{productId}" } }
                }
            };
        var response = await httpClient.PostAsync($"http://vector-db/collections/{ManualCollectionName}/points/search",
            JsonContent.Create(new
            {
                vector = embedding,
                with_payload = new[] { "id", "text", "external_source_name", "additional_metadata" },
                limit = 3,
                filter = filter,
            }));

        var responseParsed = await response.Content.ReadFromJsonAsync<QdrantResult>();

        return responseParsed!.Result.Select(r => new MemoryQueryResult(
            new MemoryRecordMetadata(true, r.Payload.Id, r.Payload.Text, "", r.Payload.External_Source_Name, r.Payload.Additional_Metadata),
            r.Score,
            null)).ToList();
    }

    public static async Task EnsureSeedDataImportedAsync(IServiceProvider services)
    {
        var importDataFromDir = Environment.GetEnvironmentVariable("ImportInitialDataDir");
        if (!string.IsNullOrEmpty(importDataFromDir))
        {
            using var scope = services.CreateScope();
            await ImportManualFilesSeedDataAsync(importDataFromDir, scope);
            await ImportManualChunkSeedDataAsync(importDataFromDir, scope);
            await ImportProductSeedDataAsync(importDataFromDir, scope);
        }
    }

    private static async Task ImportProductSeedDataAsync(string importDataFromDir, IServiceScope scope)
    {
        var semanticMemory = scope.ServiceProvider.GetRequiredService<IMemoryStore>();
        var collections = await ToListAsync(semanticMemory.GetCollectionsAsync());

        if (!collections.Contains(ProductCollectionName))
        {
            var products = JsonSerializer.Deserialize<Product[]>(
                File.ReadAllText(Path.Combine(importDataFromDir, "products.json")))!;

            await semanticMemory.CreateCollectionAsync(ProductCollectionName);
            var mappedRecords = products.Select(product =>
            {
                var id = product.ProductId.ToString();
                var metadata = new MemoryRecordMetadata(false, id, $"{product.Model} ({product.Brand})", "", "", "");
                return new MemoryRecord(metadata, product.NameEmbedding, null);
            });

            await foreach (var _ in semanticMemory.UpsertBatchAsync(ProductCollectionName, mappedRecords)) { }
        }
    }

    private static async Task ImportManualFilesSeedDataAsync(string importDataFromDir, IServiceScope scope)
    {
        var blobStorage = scope.ServiceProvider.GetRequiredService<BlobServiceClient>();
        var blobClient = blobStorage.GetBlobContainerClient("manuals");
        if (await blobClient.ExistsAsync())
        {
            return;
        }

        await blobClient.CreateIfNotExistsAsync();

        var manualsZipFilePath = Path.Combine(importDataFromDir, "manuals.zip");
        using var zipFile = ZipFile.OpenRead(manualsZipFilePath);
        foreach (var file in zipFile.Entries)
        {
            using var fileStream = file.Open();
            await blobClient.UploadBlobAsync(file.FullName, fileStream);
        }
    }

    private static async Task ImportManualChunkSeedDataAsync(string importDataFromDir, IServiceScope scope)
    {
        var semanticMemory = scope.ServiceProvider.GetRequiredService<IMemoryStore>();
        var collections = await ToListAsync(semanticMemory.GetCollectionsAsync());

        if (!collections.Contains(ManualCollectionName))
        {
            await semanticMemory.CreateCollectionAsync(ManualCollectionName);

            using var fileStream = File.OpenRead(Path.Combine(importDataFromDir, "manual-chunks.json"));
            var manualChunks = JsonSerializer.DeserializeAsyncEnumerable<ManualChunk>(fileStream);
            await foreach (var chunkChunk in ReadChunkedAsync(manualChunks, 1000))
            {
                var mappedRecords = chunkChunk.Select(chunk =>
                {
                    var id = chunk!.ChunkId.ToString();
                    var metadata = new MemoryRecordMetadata(false, id, chunk.Text, "", $"productid:{chunk.ProductId}", $"pagenumber:{chunk.PageNumber}");
                    var embedding = MemoryMarshal.Cast<byte, float>(new ReadOnlySpan<byte>(chunk.Embedding)).ToArray();
                    return new MemoryRecord(metadata, embedding, null);
                });

                await foreach (var _ in semanticMemory.UpsertBatchAsync(ManualCollectionName, mappedRecords)) { }
            }
        }
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> asyncEnumerable)
    {
        var result = new List<T>();
        await foreach (var item in asyncEnumerable)
        {
            result.Add(item);
        }
        return result;
    }

    private static async Task<bool> HasAnyAsync<T>(IAsyncEnumerable<T> asyncEnumerable)
    {
        await foreach (var item in asyncEnumerable)
        {
            return true;
        }

        return false;
    }

    private static async IAsyncEnumerable<IEnumerable<T>> ReadChunkedAsync<T>(IAsyncEnumerable<T> source, int chunkLength)
    {
        var buffer = new T[chunkLength];
        var index = 0;
        await foreach (var item in source)
        {
            buffer[index++] = item;
            if (index == chunkLength)
            {
                yield return new ArraySegment<T>(buffer, 0, index);
                index = 0;
            }
        }

        if (index > 0)
        {
            yield return new ArraySegment<T>(buffer, 0, index);
        }
    }

    class QdrantResult
    {
        public required QdrantResultEntry[] Result { get; set; }
    }

    class QdrantResultEntry
    {
        public float Score { get; set; }
        public required QdrantResultEntryPayload Payload { get; set; }
    }

    class QdrantResultEntryPayload
    {
        public required string Id { get; set; }
        public required string Text { get; set; }
        public required string External_Source_Name { get; set; }
        public required string Additional_Metadata { get; set; }
    }
}
