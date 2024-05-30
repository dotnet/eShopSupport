using System.Text.Json;
using eShopSupport.Evaluator;

public class EvalQuestionIngestor
{
    public async Task RunAsync(string generatedDataPath, string outputDir)
    {
        Console.WriteLine("Ingesting evaluation questions...");

        var questions = new List<EvalQuestion>();
        var questionsSourceDir = Path.Combine(generatedDataPath, "evalquestions");
        var inputOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var filename in Directory.GetFiles(questionsSourceDir, "*.json"))
        {
            var generated = await JsonSerializer.DeserializeAsync<EvalQuestion>(File.OpenRead(filename), inputOptions);
            questions.Add(generated!);
        }

        var outputOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(outputDir, "evalquestions.json"), JsonSerializer.Serialize(questions, outputOptions));
        Console.WriteLine($"Wrote {questions.Count} evaluation questions");
    }
}
