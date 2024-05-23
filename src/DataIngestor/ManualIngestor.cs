using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using AspirePython.VectorDbIngestor;
using eShopSupport.Backend.Data;
using Microsoft.SemanticKernel.Text;
using SmartComponents.LocalEmbeddings.SemanticKernel;

public class ManualIngestor
{
    public async Task RunAsync(string generatedDataPath, string outputDir)
    {
        Console.WriteLine("Ingesting manuals...");

        // First make a zip of the manual PDF files
        var manualsSourceDir = Path.Combine(generatedDataPath, "manuals", "pdf");
        Console.WriteLine("Compressing manuals...");
        var manualsZipPath = Path.Combine(outputDir, "manuals.zip");
        File.Delete(manualsZipPath);
        ZipFile.CreateFromDirectory(manualsSourceDir, manualsZipPath);

        // Now chunk and embed them
        using var tika = new Tika();
        using var embeddingGenerator = new LocalTextEmbeddingGenerationService();
        var chunks = new List<ManualChunk>();
        var paragraphIndex = 0;
        foreach (var file in Directory.GetFiles(manualsSourceDir, "*.pdf"))
        {
            Console.WriteLine($"Generating chunks for {file}...");
            var docId = int.Parse(Path.GetFileNameWithoutExtension(file));
            foreach (var extract in await tika.ExtractTextAsync(file))
            {
                var paragraphs = TextChunker.SplitPlainTextParagraphs([extract.Text], 200);
                var paragraphsWithEmbeddings = paragraphs.Zip(await embeddingGenerator.GenerateEmbeddingsAsync(paragraphs));

                foreach (var p in paragraphsWithEmbeddings)
                {
                    var chunk = new ManualChunk
                    {
                        ProductId = docId,
                        PageNumber = extract.PageNumber,
                        ChunkId = ++paragraphIndex,
                        Text = p.First,
                        Embedding = MemoryMarshal.AsBytes(p.Second.Span).ToArray()
                    };
                    chunks.Add(chunk);
                }
            }
            
        }

        var outputOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(outputDir, "manual-chunks.json"), JsonSerializer.Serialize(chunks, outputOptions));
        Console.WriteLine($"Wrote {chunks.Count} manual chunks");
    }
}
