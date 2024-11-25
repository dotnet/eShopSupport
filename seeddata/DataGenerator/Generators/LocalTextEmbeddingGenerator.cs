using Microsoft.Extensions.AI;
using SmartComponents.LocalEmbeddings;

namespace eShopSupport.DataGenerator.Generators;

public class LocalTextEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly LocalEmbedder _embedder = new();

    public EmbeddingGeneratorMetadata Metadata => new("local");

    public void Dispose() => _embedder.Dispose();

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var results = values.Select(v => new Embedding<float>(_embedder.Embed(v).Values)).ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
    }

    public object? GetService(Type serviceType, object? key = null) 
        => serviceType == typeof(IEmbeddingGenerator<string, Embedding<float>>) ? this : null;
}
