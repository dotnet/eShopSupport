using System.Runtime.InteropServices;
using System.Text.Json;
using AspirePython.VectorDbIngestor;
using eShopSupport.Backend.Data;
using Microsoft.SemanticKernel.Text;
using SmartComponents.LocalEmbeddings.SemanticKernel;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Util;

public class ManualIngestor
{
    public async Task RunAsync(string sourceDir, string outputDir)
    {
        Console.WriteLine("Ingesting manuals...");

        // Chunk and embed them
        var manualsSourceDir = Path.Combine(sourceDir, "manuals", "pdf");
        //using var tika = new Tika();
        using var embeddingGenerator = new LocalTextEmbeddingGenerationService();
        var chunks = new List<ManualChunk>();
        var paragraphIndex = 0;
        foreach (var file in Directory.GetFiles(manualsSourceDir, "*.pdf"))
        {
            Console.WriteLine($"Generating chunks for {file}...");
            var docId = int.Parse(Path.GetFileNameWithoutExtension(file));
            using PdfDocument document = PdfDocument.Open(file);
            foreach (var page in document.GetPages())
            {
                var letters = page.Letters;
                var wordExtractor = NearestNeighbourWordExtractor.Instance;
                var words = wordExtractor.GetWords(letters);
                var pageSegmenter = DocstrumBoundingBoxes.Instance;
                var textBlocks = pageSegmenter.GetBlocks(words);
                var pageText = string.Join(Environment.NewLine + Environment.NewLine, textBlocks.Select(t => t.Text.ReplaceLineEndings(" ")));
                

                var paragraphs = TextChunker.SplitPlainTextParagraphs([pageText], 200);
                var paragraphsWithEmbeddings = paragraphs.Zip(await embeddingGenerator.GenerateEmbeddingsAsync(paragraphs));

                foreach (var p in paragraphsWithEmbeddings)
                {
                    var chunk = new ManualChunk
                    {
                        ProductId = docId,
                        PageNumber = page.Number,
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
