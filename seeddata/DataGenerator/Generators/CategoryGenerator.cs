using eShopSupport.DataGenerator.Model;
using System.Text.RegularExpressions;

namespace eShopSupport.DataGenerator.Generators;

public class CategoryGenerator(IServiceProvider services) : GeneratorBase<Category>(services)
{
    protected override string DirectoryName => "categories";

    protected override object GetId(Category item) => item.CategoryId;

    protected override async IAsyncEnumerable<Category> GenerateCoreAsync()
    {
        // If there are any categories already, assume this covers everything we need
        if (Directory.GetFiles(OutputDirPath).Any())
        {
            yield break;
        }

        var numCategories = 50;
        var batchSize = 50;
        var categoryNames = new HashSet<string>();

        while (categoryNames.Count < numCategories)
        {
            Console.WriteLine($"Generating {batchSize} categories...");

            var prompt = @$"Generate {batchSize} product category names for an online retailer
            of outdoor adventure goods and related clothing, electronics, and homeware.
            Each category name is a single descriptive term, so it does not use the word 'and'.
            An example category name is ""Bike Helmets"", and this should be used as the first result.
            Do not prefix categories with common words like ""Outdoor"" or ""Camping"".
            
            Each category has a list of up to 8 brand names that make products in that category. All brand names are
            purely fictional. Brand names are usually multiple words with spaces and/or special characters, e.g.
            ""Orange Gear"", ""Aqua Tech US"", ""Livewell"", ""E & K"", ""JAXⓇ"".
            Many brand names are used in multiple categories. Some categories have only 2 brands.
            
            The response should be a JSON object of the form {{ ""categories"": [{{""name"":""Tents"", ""brands"":[""Rosewood"", ""Summit Kings""]}}, ...] }}.";

            var response = await GetAndParseJsonChatCompletion<Response>(prompt, maxTokens: 70 * batchSize);
            foreach (var c in response.Categories)
            {
                if (categoryNames.Add(c.Name))
                {
                    c.CategoryId = categoryNames.Count;
                    c.Brands = c.Brands.Select(ImproveBrandName).ToArray();
                    yield return c;
                }
            }
        }
    }

    private static string ImproveBrandName(string name)
    {
        // Almost invariably, the name is a PascalCase word like AquaTech, even though we told it to use spaces.
        // For variety, convert to "Aqua Tech" or "Aquatech" sometimes.
        return Regex.Replace(name, @"([a-z])([A-Z])", m => Random.Shared.Next(3) switch
        {
            0 => $"{m.Groups[1].Value} {m.Groups[2].Value}",
            1 => $"{m.Groups[1].Value}{m.Groups[2].Value.ToLower()}",
            _ => m.Value
        });
    }

    private class Response
    {
        public required List<Category> Categories { get; set; }
    }
}
