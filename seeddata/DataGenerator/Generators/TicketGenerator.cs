using eShopSupport.DataGenerator.Model;

namespace eShopSupport.DataGenerator.Generators;

public class TicketGenerator(IReadOnlyList<Product> products, IReadOnlyList<Category> categories, IReadOnlyList<Manual> manuals, IServiceProvider services) : GeneratorBase<Ticket>(services)
{
    protected override string DirectoryName => "tickets/enquiries";

    protected override object GetId(Ticket item) => item.TicketId;

    protected override async IAsyncEnumerable<Ticket> GenerateCoreAsync()
    {
        // If there are any tickets already, assume this covers everything we need
        if (Directory.GetFiles(OutputDirPath).Any())
        {
            yield break;
        }

        var numTickets = 3;
        var batchSize = 3;
        var ticketId = 0;

        string[] situations = [
            "asking about a particular usage scenario before purchase",
            "asking a specific technical question about the product's capabilities",
            "needs information on the product's suitability for its most obvious use case",
            "unable to make the product work in one particular way",
            "thinks the product doesn't work at all",
            "can't understand how to do something",
            "has broken the product",
            "needs reassurance that the product is behaving as expected",
            "wants to use the product for a wildly unexpected purpose, but without self-awareness and assumes it's reasonable",
            "incredibly fixated on one minor, obscure detail (before or after purchase), but without self-awareness that they are fixated on an obscure matter. Do not use the word 'fixated'.",
            "a business-to-business enquiry from another retailer who stocks the product and has their own customer enquiries to solve",
        ];

        string[] styles = [
            "polite",
            "extremely jovial, as if trying to be best friends",
            "formal",
            "embarassed and thinks they are the cause of their own problem",
            "not really interested in communicating clearly, only using a few words and assuming support can figure it out",
            "demanding and entitled",
            "frustrated and angry",
            "grumpy, and trying to claim there are logical flaws in whatever the support agent has said",
            "extremely brief and abbreviated, by a teenager typing on a phone while distracted by another task",
            "extremely technical, as if trying to prove the superiority of their own knowledge",
            "relies on extremely, obviously false assumptions, but is earnest and naive",
            "providing almost no information, so it's impossible to know what they want or why they are submitting the support message",
        ];

        while (ticketId < numTickets)
        {
            var numInBatch = Math.Min(batchSize, numTickets - ticketId);
            var ticketsInBatch = await Task.WhenAll(Enumerable.Range(0, numInBatch).Select(async _ =>
            {
                var product = products[Random.Shared.Next(products.Count)];
                var category = categories.Single(c => c.CategoryId == product.CategoryId);
                var situation = situations[Random.Shared.Next(situations.Length)];
                var style = styles[Random.Shared.Next(styles.Length)];
                var manual = manuals.Single(m => m.ProductId == product.ProductId);
                var manualExtract = ManualGenerator.ExtractFromManual(manual);

                var prompt = @$"You are creating test data for a customer support ticketing system.
                    Write a message by a customer who has purchased, or is considering purchasing, the following:

                    Product: {product.Model}
                    Brand: {product.Brand}
                    Category: {category.Name}
                    Description: {product.Description}
                    Random extract from manual: <extract>{manualExtract}</extract>

                    The situation is: {situation}
                    If applicable, they can ask for a refund/replacement/repair. However in most cases they
                    are asking for information or help with a problem.

                    The customer writes in the following style: {style}

                    Create a name for the author, writing the message as if you are that person. The customer name
                    should be fictional and random, and not based on the support enquiry itself. Do not use cliched
                    or stereotypical names.

                    Where possible, the message should refer to something specific about this product such as a feature
                    mentioned in its description or a fact mentioned in the manual (but the customer does not refer
                    to having read the manual).

                    The message length may be anything from very brief (around 10 words) to very long (around 200 words).
                    Use blank lines for paragraphs if needed.

                    The result should be JSON form {{ ""customerFullName"": ""string"", ""message"": ""string"" }}.";

                var ticket = await GetAndParseJsonChatCompletion<Ticket>(prompt);
                ticket.ProductId = product.ProductId;
                ticket.CustomerSituation = situation;
                ticket.CustomerStyle = style;
                return ticket;
            }));

            foreach (var t in ticketsInBatch)
            {
                t.TicketId = ++ticketId;
                yield return t;
            }
        }
    }
}
