using System.Text.Json;

namespace eShopSupport.DataGenerator.Generators;

internal static class OutputHelper
{
    public static void Write(string directoryName, ProductCategory value)
    {
        var outputDirRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "output");
        var outputDirPath = Path.Combine(outputDirRoot, directoryName);

        if (!Directory.Exists(outputDirPath))
        {
            Directory.CreateDirectory(outputDirPath);
        }

        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };

        var path = Path.Combine(outputDirPath, $"{value.CategoryId}.json");
        var json = JsonSerializer.Serialize(value, serializerOptions);
        File.WriteAllText(path, json);
    }
}
