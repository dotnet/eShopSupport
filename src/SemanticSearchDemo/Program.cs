// This console app exists only for demo purposes. It is not relevant to a real app.
// It performs a semantic search in memory just to show how that works.

using Microsoft.SemanticKernel.Memory;
using SmartComponents.LocalEmbeddings.SemanticKernel;

ISemanticTextMemory semanticTextMemory = new SemanticTextMemory(
    new InProcessVectorStore("seeddata/dev/manual-chunks.json"),
    new LocalTextEmbeddingGenerationService());

while (true)
{
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write("Search: ");
    var searchTerm = Console.ReadLine()!;

    var results = semanticTextMemory.SearchAsync("my-collection", searchTerm, limit: 3, minRelevanceScore: 0.5f);
    await foreach (var result in results)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"RELEVANCE: {result.Relevance:F2} [{result.Metadata.ExternalSourceName} {result.Metadata.AdditionalMetadata} embedding={TextUtil.Embedding(result.Embedding!.Value)}]");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(TextUtil.Indent(result.Metadata.Text, Console.WindowWidth));
    }
    Console.WriteLine();
}
