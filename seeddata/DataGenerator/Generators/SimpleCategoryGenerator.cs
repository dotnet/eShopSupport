using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace eShopSupport.DataGenerator.Generators;

public class SimpleCategoryGenerator(IServiceProvider services)
{
    private readonly static JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private IChatCompletionService ChatCompletion { get; } = services.GetRequiredService<IChatCompletionService>();

    public async Task<List<ProductCategory>> GenerateAsync()
    {
        var numCategories = 10;
        var batchSize = 5;
        var categories = new Dictionary<string, ProductCategory>();

        while (categories.Count < numCategories)
        {
            Console.WriteLine($"Generating {batchSize} categories...");

            var prompt = $$"""
            Generate {{batchSize}} product category names for an online retailer
            of outdoor adventure goods and related clothing, electronics, etc.
            Each category has 3-5 fictional brand names that may be repeated across categories.
                        
            Respond in the following JSON format:
            {
              "categories": [
                { "name": string, "brands": [string, string, ...] },
                ...
              ]
            }
            """;

            var response = await ChatCompletion.GetChatMessageContentAsync(
                new ChatHistory(prompt),
                new OpenAIPromptExecutionSettings { ResponseFormat = "json_object" });

            // Parse the response, and output any new ones
            var parsedResponse = JsonSerializer.Deserialize<Response>(response.ToString(), JsonOptions)!;
            foreach (var c in parsedResponse.Categories)
            {
                if (!categories.ContainsKey(c.Name))
                {
                    var category = c with { CategoryId = categories.Count + 1 };
                    categories.Add(c.Name, category);
                    OutputHelper.Write("categories", category);
                }
            }
        }

        return categories.Values.ToList();
    }
}

public record Response(List<ProductCategory> Categories);
public record ProductCategory(int CategoryId, string Name, string[] Brands);
