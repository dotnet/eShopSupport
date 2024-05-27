using System.Collections.Concurrent;
using System.Text.Json;
using eShopSupport.Backend.Data;
using eShopSupport.ServiceDefaults.Clients.Backend;

class TicketIngestor
{
    public async Task RunAsync(string generatedDataPath, string outputDir)
    {
        Console.WriteLine("Ingesting tickets and customers...");

        var tickets = new List<Ticket>();
        var customersByName = new ConcurrentDictionary<string, Customer>();
        var ticketsSourceDir = Path.Combine(generatedDataPath, "tickets", "threads");
        var inputOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var messageId = 0;
        var customerId = 0;
        foreach (var filename in Directory.GetFiles(ticketsSourceDir, "*.json"))
        {
            // TODO: Consider simplifying by ensuring the generated data is already in exactly the right form
            var generated = (await JsonSerializer.DeserializeAsync<GeneratedTicket>(File.OpenRead(filename), inputOptions))!;
            var customer = customersByName.GetOrAdd(generated.CustomerFullName, name => new Customer { FullName = name, CustomerId = ++customerId });

            tickets.Add(new Ticket
            {
                TicketId = generated.TicketId,
                ProductId = generated.ProductId,
                TicketType = Enum.Parse<TicketType>(generated.TicketType),
                TicketStatus = Enum.Parse<TicketStatus>(generated.TicketStatus),
                CustomerId = customer.CustomerId,
                Customer = customer,
                ShortSummary = generated.ShortSummary,
                LongSummary = generated.LongSummary,
                CustomerSatisfaction = generated.CustomerSatisfaction,
                Messages = generated.Messages.Select(generatedMessage => new Message
                {
                    MessageId = ++messageId,
                    IsCustomerMessage = generatedMessage.AuthorRole == 0,
                    Text = generatedMessage.Text
                }).ToList()
            });
        }

        var outputOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(outputDir, "tickets.json"), JsonSerializer.Serialize(tickets, outputOptions));
        Console.WriteLine($"Wrote {tickets.Count} tickets");

        await File.WriteAllTextAsync(Path.Combine(outputDir, "customers.json"), JsonSerializer.Serialize(customersByName.Values, outputOptions));
        Console.WriteLine($"Wrote {customersByName.Count} customers");
    }

    internal record GeneratedTicket(int TicketId, int ProductId, string TicketType, string TicketStatus, string CustomerFullName, string ShortSummary, string LongSummary, int? CustomerSatisfaction, List<GeneratedMessage> Messages);
    internal record GeneratedMessage(int MessageId, int AuthorRole, string Text);
}
