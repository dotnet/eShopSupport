using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.SemanticKernel.Memory;

namespace eShopSupport.Backend.Data;

public class ProductManualSemanticSearch(ISemanticTextMemory semanticTextMemory)
{
    private const string ManualCollectionName = "manuals";

    public async Task<IReadOnlyList<MemoryQueryResult>> SearchAsync(string query)
    {
        var results = new List<MemoryQueryResult>();

        await foreach (var result in semanticTextMemory.SearchAsync(ManualCollectionName, query, limit: 3))
        {
            results.Add(result);
        }

        return results;
    }

    public static async Task EnsureSeedDataImportedAsync(IServiceProvider services)
    {
        var importDataFromDir = Environment.GetEnvironmentVariable("ImportInitialDataDir");
        if (!string.IsNullOrEmpty(importDataFromDir))
        {
            using var scope = services.CreateScope();

            var semanticMemory = scope.ServiceProvider.GetRequiredService<IMemoryStore>();
            var collections = semanticMemory.GetCollectionsAsync();

            if (!(await HasAnyAsync(collections)))
            {
                await semanticMemory.CreateCollectionAsync(ManualCollectionName);

                using var fileStream = File.OpenRead(Path.Combine(importDataFromDir, "manual-chunks.json"));
                var manualChunks = JsonSerializer.DeserializeAsyncEnumerable<ManualChunk>(fileStream);
                await foreach (var chunkChunk in ReadChunkedAsync(manualChunks, 1000))
                {
                    var mappedRecords = chunkChunk.Select(chunk =>
                    {
                        var id = chunk!.ParagraphId.ToString();
                        var metadata = new MemoryRecordMetadata(false, id, chunk.Text, "", "", $"productid:{chunk.ProductId}");
                        var embedding = MemoryMarshal.Cast<byte, float>(new ReadOnlySpan<byte>(chunk.Embedding)).ToArray();
                        return new MemoryRecord(metadata, embedding, null);
                    });

                    await foreach (var _ in semanticMemory.UpsertBatchAsync(ManualCollectionName, mappedRecords)) { }
                }
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
}
