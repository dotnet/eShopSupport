using System.Runtime.InteropServices;
using System.Text.Json;
using eShopSupport.Backend.Data;
using Microsoft.SemanticKernel.Text;
using SmartComponents.LocalEmbeddings.SemanticKernel;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

public class ManualIngestor
{
    public async Task RunAsync(string sourceDir, string outputDir)
    {
        Console.WriteLine("Ingesting manuals...");

        // Prepare
        var manualsSourceDir = Path.Combine(sourceDir, "manuals", "pdf");
        using var embeddingGenerator = new LocalTextEmbeddingGenerationService();
        var chunks = new List<ManualChunk>();
        var paragraphIndex = 0;

        // Loop over each PDF file
        foreach (var file in Directory.GetFiles(manualsSourceDir, "*.pdf"))
        {
            Console.WriteLine($"Generating chunks for {file}...");
            var docId = int.Parse(Path.GetFileNameWithoutExtension(file));

            // Loop over each page in it
            using var pdf = PdfDocument.Open(file);
            foreach (var page in pdf.GetPages())
            {
                // [1] Parse (PDF page -> string)
                var pageText = GetPageText(page);

                // [2] Chunk (split into shorter strings on natural boundaries)
                var paragraphs = TextChunker.SplitPlainTextParagraphs([pageText], 200);

                // [3] Embed (map into semantic space)
                var paragraphsWithEmbeddings = paragraphs.Zip(await embeddingGenerator.GenerateEmbeddingsAsync(paragraphs));

                // [4] Save
                chunks.AddRange(paragraphsWithEmbeddings.Select(p => new ManualChunk
                {
                    ProductId = docId,
                    PageNumber = page.Number,
                    ChunkId = ++paragraphIndex,
                    Text = p.First,
                    Embedding = MemoryMarshal.AsBytes(p.Second.Span).ToArray()
                }));
            }
        }

        var outputOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(outputDir, "manual-chunks.json"), JsonSerializer.Serialize(chunks, outputOptions));
        Console.WriteLine($"Wrote {chunks.Count} manual chunks");
    }

    private static string GetPageText(Page pdfPage)
    {
        var letters = pdfPage.Letters;
        var words = NearestNeighbourWordExtractor.Instance.GetWords(letters);
        var textBlocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        return string.Join(Environment.NewLine + Environment.NewLine,
            textBlocks.Select(t => t.Text.ReplaceLineEndings(" ")));
    }
}
