// This console app exists only for demo purposes. It is not relevant to a real app.
// It performs a semantic search in memory just to show how that works.

using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using eShopSupport.Backend.Data;
using Microsoft.SemanticKernel.Memory;

internal class InProcessVectorStore : IMemoryStore
{
    private List<MemoryRecord> _records;

    public InProcessVectorStore(string sourcePath)
    {
        var filePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", sourcePath));
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found", filePath);
        }

        var chunks = JsonSerializer.Deserialize<List<ManualChunk>>(File.ReadAllText(filePath))!;
        _records = chunks.Select(c => new MemoryRecord(
            new MemoryRecordMetadata(true, c.ChunkId.ToString(), c.Text, "", $"{c.ProductId}.pdf", $"page={c.PageNumber}"),
            ToFloats(c.Embedding),
            c.ChunkId.ToString()))
            .ToList();
    }

    public async IAsyncEnumerable<(MemoryRecord, double)> GetNearestMatchesAsync(string collectionName, ReadOnlyMemory<float> embedding, int limit, double minRelevanceScore = 0, bool withEmbeddings = false, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Finding closest vectors to {TextUtil.Embedding(embedding)}");

        var closest = _records.Select(r => (r, TensorPrimitives.CosineSimilarity(embedding.Span, r.Embedding.Span)))
            .Where(t => t.Item2 >= minRelevanceScore)
            .OrderByDescending(t => t.Item2)
            .Take(limit);
        await Task.Yield();
        foreach (var item in closest)
        {
            yield return item;
        }
    }

    private static ReadOnlyMemory<float> ToFloats(byte[] embedding)
        => MemoryMarshal.Cast<byte, float>(new ReadOnlySpan<byte>(embedding)).ToArray();

    // For this demo, that's all we need

    public Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DoesCollectionExistAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<MemoryRecord?> GetAsync(string collectionName, string key, bool withEmbedding = false, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<MemoryRecord> GetBatchAsync(string collectionName, IEnumerable<string> keys, bool withEmbeddings = false, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<string> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<(MemoryRecord, double)?> GetNearestMatchAsync(string collectionName, ReadOnlyMemory<float> embedding, double minRelevanceScore = 0, bool withEmbedding = false, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task RemoveAsync(string collectionName, string key, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task RemoveBatchAsync(string collectionName, IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<string> UpsertAsync(string collectionName, MemoryRecord record, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<string> UpsertBatchAsync(string collectionName, IEnumerable<MemoryRecord> records, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
