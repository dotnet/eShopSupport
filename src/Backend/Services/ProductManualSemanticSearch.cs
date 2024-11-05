using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Azure.Storage.Blobs;
using eShopSupport.Backend.Data;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;

namespace eShopSupport.Backend.Services;

public class ProductManualSemanticSearch(ITextEmbeddingGenerationService embedder, IVectorStore store)
{
    private const string ManualCollectionName = "manuals";

    public async Task<VectorSearchResults<ManualChunk>> SearchAsync(int? productId, string query)
    {
        var embedding = await embedder.GenerateEmbeddingAsync(query);

        var filter = new VectorSearchFilter([
                new EqualToFilterClause("external_source_name", $"productid:{productId}")
            ]);


        var searchOptions = new VectorSearchOptions
        {
            Filter = filter,
            Top = 3
        };

        var collection = store.GetCollection<int,ManualChunk>(ManualCollectionName);

        var results = await collection.VectorizedSearchAsync(embedding, searchOptions);

        return results;
    }

    public static async Task EnsureSeedDataImportedAsync(IServiceProvider services, string? initialImportDataDir)
    {
        if (!string.IsNullOrEmpty(initialImportDataDir))
        {
            using var scope = services.CreateScope();
            await ImportManualFilesSeedDataAsync(initialImportDataDir, scope);
            await ImportManualChunkSeedDataAsync(initialImportDataDir, scope);
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
        var semanticMemory = scope.ServiceProvider.GetRequiredService<IVectorStore>();
        var collections = await semanticMemory.ListCollectionNamesAsync().ToListAsync();

        if (!collections.Contains(ManualCollectionName))
        {
            var collection = semanticMemory.GetCollection<int,ManualChunk>(ManualCollectionName);

            using var fileStream = File.OpenRead(Path.Combine(importDataFromDir, "manual-chunks.json"));
            var manualChunks = JsonSerializer.DeserializeAsyncEnumerable<ManualChunk>(fileStream);
            await foreach (var chunkChunk in ReadChunkedAsync(manualChunks, 1000))
            {

                var mappedRecords = chunkChunk.Select(chunk =>
                {

                });


                //var mappedRecords = chunkChunk.Select(chunk =>
                //{
                //    var id = chunk!.ChunkId.ToString();
                //    var metadata = new MemoryRecordMetadata(false, id, chunk.Text, "", $"productid:{chunk.ProductId}", $"pagenumber:{chunk.PageNumber}");
                //    var embedding = MemoryMarshal.Cast<byte, float>(new ReadOnlySpan<byte>(chunk.Embedding)).ToArray();
                //    return new MemoryRecord(metadata, embedding, null);
                //});

                //await foreach (var _ in semanticMemory.UpsertBatchAsync(ManualCollectionName, mappedRecords)) { }
            }
        }
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
