﻿using eShopSupport.DataGenerator.Model;
using Microsoft.SemanticKernel;
using System.Text;
using System.ComponentModel;
using Microsoft.SemanticKernel.Text;
using Microsoft.SemanticKernel.Embeddings;
using SmartComponents.LocalEmbeddings.SemanticKernel;
using System.Numerics.Tensors;

namespace eShopSupport.DataGenerator.Generators;

public class TicketThreadGenerator(IReadOnlyList<Ticket> tickets, IReadOnlyList<Product> products, IReadOnlyList<Manual> manuals) : GeneratorBase<TicketThread>
{
    private readonly ITextEmbeddingGenerationService embedder = new LocalTextEmbeddingGenerationService();

    protected override object GetId(TicketThread item) => item.TicketId;

    protected override string DirectoryName => "tickets/threads";

    protected override IAsyncEnumerable<TicketThread> GenerateCoreAsync()
    {
        // Skip the ones we already have
        var threadsToGenerate = tickets.Where(t => !File.Exists(GetItemOutputPath(t.TicketId.ToString()))).ToList();
        return MapParallel(threadsToGenerate, GenerateThreadAsync);
    }

    private async Task<TicketThread> GenerateThreadAsync(Ticket ticket)
    {
        var messageId = 0;
        var thread = new TicketThread
        {
            TicketId = ticket.TicketId,
            ProductId = ticket.ProductId,
            CustomerFullName = ticket.CustomerFullName,
            Messages = [new TicketThreadMessage { AuthorRole = Role.Customer, MessageId = ++messageId, Text = ticket.Message }]
        };
        var product = products.Single(p => p.ProductId == ticket.ProductId);

        var maxMessagesInThread = 5;
        for (var i = 0; i < maxMessagesInThread; i++)
        {
            var lastMessageRole = thread.Messages.Last().AuthorRole;
            var messageRole = lastMessageRole switch
            {
                Role.Customer => Role.Assistant,
                Role.Assistant => Role.Customer,
                _ => throw new NotImplementedException(),
            };

            var response = messageRole switch
            {
                Role.Customer => await GenerateCustomerMessageAsync(product, ticket, thread.Messages),
                Role.Assistant => await GenerateAssistantMessageAsync(product, ticket, thread.Messages, manuals),
                _ => throw new NotImplementedException(),
            };

            thread.Messages.Add(new TicketThreadMessage { MessageId = ++messageId, AuthorRole = messageRole, Text = response.Message });

            if (response.ShouldClose || (messageRole == Role.Customer && Random.Shared.Next(5) < 2))
            {
                break;
            }
        }

        return thread;
    }

    private async Task<Response> GenerateCustomerMessageAsync(Product product, Ticket ticket, IReadOnlyList<TicketThreadMessage> messages)
    {
        var prompt = $@"You are generating test data for a customer support ticketing system. There is an open ticket as follows:
        
        Product: {product.Model}
        Brand: {product.Brand}
        Customer name: {ticket.CustomerFullName}

        The message log so far is:

        {FormatMessagesForPrompt(messages)}

        Generate the next reply from the customer. You may do any of:

        - Supply more information as requested by the support agent
        - Say you did what the support agent suggested and whether or not it worked
        - Confirm that your enquiry is now resolved and you accept the resolution
        - Complain about the resolution
        - Say you need more information

        Write as if you are the customer. This customer ALWAYS writes in the following style: {ticket.CustomerStyle}.

        Respond in the following JSON format: {{ ""message"": ""string"", ""shouldClose"": bool }}.
        Indicate that the ticket should be closed if, as the customer, you feel the ticket is resolved (whether or not you are satisfied).
";

        return await GetAndParseJsonChatCompletion<Response>(prompt);
    }

    private async Task<Response> GenerateAssistantMessageAsync(Product product, Ticket ticket, IReadOnlyList<TicketThreadMessage> messages, IReadOnlyList<Manual> manuals)
    {
        var prompt = $@"You are a customer service agent working for Northern Mountains, an online retailer. You are responding to a customer
        enquiry about the following product:

        Product: {product.Model}
        Brand: {product.Brand}

        The message log so far is:

        {FormatMessagesForPrompt(messages)}

        Your job is to provide the next message to send to the customer, and ideally close the ticket. Your goal is to help resolve their enquiry, which might include:

        - Providing information or technical support
        - Recommending a return or repair, if compliant with policy below
        - Closing off-topic enquiries

        You must first decide if you have enough information, and if not, either ask the customer for more details or search for information
        in the product manual using the configured tool. Don't repeat information that was already given earlier in the message log.

        Our policy for returns/repairs is:
        - Returns are allowed within 30 days if the product is unused
        - Defective products may be returned within 1 year of purchase for a refund
        - There may be other warranty or repair options provided by the manufacturer, as detailed in the manual
        Returns may be initiated at https://northernmountains.example.com/support/returns

        You ONLY give information based on the product details and manual. If you cannot answer based on the provided context, say that you don't know.
        Whenever possible, give your answer as a quote from the manual, for example saying ""According to the manual, ..."".
        If needed, refer the customer to the manufacturer's support contact detail in the user manual, if any.

        You refer to yourself only as ""Northern Mountains Support"", or ""Support team"".

        Respond in the following JSON format: {{ ""message"": ""string"", ""shouldClose"": bool }}.
        Indicate that the ticket should be closed only if the customer has confirmed it is resolved.
        It's OK to give very short, 1-sentence replies if applicable.
        ";

        var manual = manuals.Single(m => m.ProductId == product.ProductId);
        var tools = new AssistantTools(embedder, manual);

        return await GetAndParseJsonChatCompletion<Response>(prompt, tools: tools);
    }

    public static string FormatMessagesForPrompt(IReadOnlyList<TicketThreadMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var message in messages)
        {
            sb.AppendLine($"<message role=\"{message.AuthorRole}\">{message.Text}</message>");
        }
        return sb.ToString();
    }

    private class Response
    {
        public required string Message { get; set; }
        public bool ShouldClose { get; set; }
    }

    private class AssistantTools(ITextEmbeddingGenerationService embedder, Manual manual)
    {
        [KernelFunction, Description("Searches for information in the product's user manual.")]
        public async Task<string> SearchUserManualAsync([Description("text to look for in user manual")] string query)
        {
            // Obviously it would be more performant to chunk and embed each manual only once, but this is simpler for now
            var chunks = TextChunker.SplitPlainTextParagraphs([manual.MarkdownText], 100);
            var embeddings = await embedder.GenerateEmbeddingsAsync(chunks);
            var candidates = chunks.Zip(embeddings);
            var queryEmbedding = await embedder.GenerateEmbeddingAsync(query);

            var closest = candidates
                .Select(c => new { Text = c.First, Similarity = TensorPrimitives.CosineSimilarity(c.Second.Span, queryEmbedding.Span) })
                .OrderByDescending(c => c.Similarity)
                .Take(3)
                .Where(c => c.Similarity > 0.6f)
                .ToList();

            if (closest.Any())
            {
                return string.Join(Environment.NewLine, closest.Select(c => $"<snippet_from_manual>{c.Text}</snippet_from_manual>"));
            }
            else
            {
                return "The manual contains no relevant information about this";
            }
        }
    }
}
