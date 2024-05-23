using System.Text.Json;
using eShopSupport.Backend.Data;

class ProductIngestor
{
    public async Task RunAsync(string generatedDataPath, string outputDir)
    {
        Console.WriteLine("Ingesting products...");

        var products = new List<Product>();
        var productsSourceDir = Path.Combine(generatedDataPath, "products");
        var inputOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var filename in Directory.GetFiles(productsSourceDir, "*.json"))
        {
            var generated = (await JsonSerializer.DeserializeAsync<GeneratedProduct>(File.OpenRead(filename), inputOptions))!;
            products.Add(new Product
            {
                ProductId = generated.ProductId,
                CategoryId = generated.CategoryId,
                Brand = generated.Brand,
                Model = generated.Model,
                Description = generated.Description,
                Price = generated.Price,
            });
        }

        var outputOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(outputDir, "products.json"), JsonSerializer.Serialize(products, outputOptions));
        Console.WriteLine($"Wrote {products.Count} products");
    }

    internal record GeneratedProduct(int ProductId, int CategoryId, string Brand, string Model, string Description, decimal Price);
}
