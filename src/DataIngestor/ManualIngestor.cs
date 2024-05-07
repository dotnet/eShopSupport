using System.Runtime.InteropServices;
using System.Text.Json;
using AspirePython.VectorDbIngestor;
using eShopSupport.Backend.Data;
using Microsoft.SemanticKernel.Text;
using SmartComponents.LocalEmbeddings.SemanticKernel;

public class ManualIngestor
{
    public async Task RunAsync(string generatedDataPath)
    {
        Console.WriteLine("Ingesting manuals...");

        using var tika = new Tika();
        using var embeddingGenerator = new LocalTextEmbeddingGenerationService();

        var manualsSourceDir = Path.Combine(generatedDataPath, "manuals", "pdf");
        var chunks = new List<ManualChunk>();
        var paragraphIndex = 0;

        foreach (var file in Directory.GetFiles(manualsSourceDir, "*.pdf"))
        {
            Console.WriteLine($"Generating chunks for {file}...");
            var docId = int.Parse(Path.GetFileNameWithoutExtension(file));
            var extractedText = await tika.ExtractTextAsync(file);
            var paragraphs = TextChunker.SplitPlainTextParagraphs([extractedText], 200);
            var paragraphsWithEmbeddings = paragraphs.Zip(await embeddingGenerator.GenerateEmbeddingsAsync(paragraphs));

            foreach (var p in paragraphsWithEmbeddings)
            {
                var chunk = new ManualChunk
                {
                    ProductId = docId,
                    ParagraphId = ++paragraphIndex,
                    Text = p.First,
                    Embedding = MemoryMarshal.AsBytes(p.Second.Span).ToArray()
                };
                chunks.Add(chunk);
            }
        }

        var solutionDir = PathUtils.FindAncestorDirectoryContaining("*.sln");
        var outputDir = Path.Combine(solutionDir, "seeddata", "dev");

        var outputOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(outputDir, "manual-chunks.json"), JsonSerializer.Serialize(chunks, outputOptions));
        Console.WriteLine($"Wrote {chunks.Count} manual chunks");

    }
}
