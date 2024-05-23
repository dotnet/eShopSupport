using System.Text.Json;
using eShopSupport.Backend.Data;

class ProductCategoryIngestor
{
    public async Task RunAsync(string generatedDataPath, string outputDir)
    {
        Console.WriteLine("Ingesting product categories...");

        var categories = new List<ProductCategory>();
        var categoriesSourceDir = Path.Combine(generatedDataPath, "categories");
        var inputOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var filename in Directory.GetFiles(categoriesSourceDir, "*.json"))
        {
            var generated = (await JsonSerializer.DeserializeAsync<GeneratedCategory>(File.OpenRead(filename), inputOptions))!;
            categories.Add(new ProductCategory
            {
                CategoryId = generated.CategoryId,
                Name = generated.Name,
            });
        }

        var outputOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(outputDir, "categories.json"), JsonSerializer.Serialize(categories, outputOptions));
        Console.WriteLine($"Wrote {categories.Count} categories");
    }

    internal record GeneratedCategory(int CategoryId, string Name);
}
