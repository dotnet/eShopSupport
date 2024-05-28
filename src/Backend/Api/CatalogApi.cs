using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using Azure.Storage.Blobs;
using eShopSupport.Backend.Data;
using eShopSupport.Backend.Services;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel.Embeddings;

namespace eShopSupport.Backend.Api;

public static class CatalogApi
{
    public static void MapCatalogApiEndpoints(this WebApplication app)
    {
        app.MapGet("/manual", GetManualPdfAsync);
        app.MapGet("/api/categories", SearchCategoriesAsync);
        app.MapGet("/api/products", SearchProductsAsync);
    }

    private static async Task<IEnumerable<FindCategoriesResult>> SearchCategoriesAsync(AppDbContext dbContext, ITextEmbeddingGenerationService embedder, string? searchText, string? ids)
    {
        IQueryable<ProductCategory> filteredCategories = dbContext.ProductCategories;

        if (!string.IsNullOrWhiteSpace(ids))
        {
            var idsParsed = ids.Split(',').Select(int.Parse).ToList();
            filteredCategories = filteredCategories.Where(c => idsParsed.Contains(c.CategoryId));
        }

        var matchingCategories = await filteredCategories.ToArrayAsync();

        // If you have a small number of items, another pattern for semantic search is simply
        // to do it in process. In this case we also amend the similarity rule so that if the
        // category is an exact prefix match, it's considered a perfect match. So in effect
        // we have both a prefix match and a semantic match working together.
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var searchTextEmbedding = await embedder.GenerateEmbeddingAsync(searchText);
            matchingCategories = matchingCategories.Select(c => new
            {
                Category = c,
                Similarity = c.Name.StartsWith(searchText, StringComparison.OrdinalIgnoreCase)
                                    ? 1f
                                    : TensorPrimitives.CosineSimilarity(FromBase64(c.NameEmbeddingBase64), searchTextEmbedding.Span),
            }).Where(x => x.Similarity > 0.5f)
                            .OrderByDescending(x => x.Similarity)
                            .Take(5)
                            .Select(x => x.Category)
                            .ToArray();
        }

        return matchingCategories.Select(c => new FindCategoriesResult(c.CategoryId) { Name = c.Name });

        static ReadOnlySpan<float> FromBase64(string embeddingBase64)
        {
            var bytes = Convert.FromBase64String(embeddingBase64);
            return MemoryMarshal.Cast<byte, float>(bytes);
        }
    }

    private static Task<IEnumerable<FindProductsResult>> SearchProductsAsync(ProductSemanticSearch productSemanticSearch, string searchText)
        => productSemanticSearch.FindProductsAsync(searchText);

    private static async Task<IResult> GetManualPdfAsync(string file, BlobServiceClient blobServiceClient)
    {
        var blobClient = blobServiceClient.GetBlobContainerClient("manuals").GetBlobClient(file);
        if (!(await blobClient.ExistsAsync()))
        {
            return Results.NotFound();
        }

        var download = await blobClient.DownloadStreamingAsync();
        return Results.File(download.Value.Content, "application/pdf");
    }
}
