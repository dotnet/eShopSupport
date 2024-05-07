using System.Text.Json;
using eShopSupport.Backend.Data;

class TicketIngestor
{
    public async Task RunAsync(string generatedDataPath)
    {
        Console.WriteLine("Ingesting tickets...");

        var tickets = new List<Ticket>();
        var ticketsSourceDir = Path.Combine(generatedDataPath, "tickets", "threads");
        var inputOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var messageId = 0;
        foreach (var filename in Directory.GetFiles(ticketsSourceDir, "*.json"))
        {
            // TODO: Consider simplifying by ensuring the generated data is already in exactly the right form
            var generated = (await JsonSerializer.DeserializeAsync<GeneratedTicket>(File.OpenRead(filename), inputOptions))!;
            tickets.Add(new Ticket
            {
                TicketId = generated.TicketId,
                ProductId = generated.ProductId,
                CustomerFullName = generated.CustomerFullName,
                ShortSummary = generated.ShortSummary,
                LongSummary = generated.LongSummary,
                CustomerSatisfaction = generated.CustomerSatisfaction,
                Messages = generated.Messages.Select(generatedMessage => new Message
                {
                    MessageId = ++messageId,
                    AuthorName = generatedMessage.AuthorRole == 0 ? generated.CustomerFullName : "Support",
                    Text = generatedMessage.Text
                }).ToList()
            });
        }

        var solutionDir = PathUtils.FindAncestorDirectoryContaining("*.sln");
        var outputDir = Path.Combine(solutionDir, "seeddata", "dev");

        var outputOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(outputDir, "tickets.json"), JsonSerializer.Serialize(tickets, outputOptions));
        Console.WriteLine($"Wrote {tickets.Count} tickets");
    }

    internal record GeneratedTicket(int TicketId, int ProductId, string CustomerFullName, string ShortSummary, string LongSummary, int? CustomerSatisfaction, List<GeneratedMessage> Messages);
    internal record GeneratedMessage(int MessageId, int AuthorRole, string Text);
}
