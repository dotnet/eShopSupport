using System.Text;
using System.Threading.Channels;
using eShopSupport.DataGenerator.Model;

namespace eShopSupport.DataGenerator.Generators;

public class EvalQuestionGenerator(IReadOnlyList<Product> products, IReadOnlyList<Category> categories, IReadOnlyList<Manual> manuals, IServiceProvider services)
    : GeneratorBase<EvalQuestion>(services)
{
    protected override string DirectoryName => "evalquestions";

    protected override object GetId(EvalQuestion item)
        => item.QuestionId;

    protected override async IAsyncEnumerable<EvalQuestion> GenerateCoreAsync()
    {
        // If there are any questions already, assume this covers everything we need
        if (Directory.GetFiles(OutputDirPath).Any())
        {
            yield break;
        }

        var numQuestions = 500;
        var questionId = 0;
        var outputChannel = Channel.CreateUnbounded<EvalQuestion>();
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 10 };
        CompleteOutputAfterTask(outputChannel.Writer, Parallel.ForAsync(0, numQuestions, parallelOptions, async (_, _) =>
        {
            var item = await GenerateSingle();
            item.QuestionId = Interlocked.Increment(ref questionId);
            await outputChannel.Writer.WriteAsync(item);
        }));

        await foreach (var item in outputChannel.Reader.ReadAllAsync())
        {
            yield return item;
        }
    }

    private static void CompleteOutputAfterTask<T>(ChannelWriter<T> writer, Task task)
    {
        Task.Run(async () =>
        {
            try
            {
                await task;
                writer.Complete();
            }
            catch (Exception ex)
            {
                writer.Complete(ex);
            }
        });
    }

    private async Task<EvalQuestion> GenerateSingle()
    {
        var product = products[Random.Shared.Next(products.Count)];
        var category = categories.Single(c => c.CategoryId == product.CategoryId);
        var manual = manuals.Single(m => m.ProductId == product.ProductId);
        var manualExtract = ExtractFromManual(manual);
        var isQuestionWrittenByAgent = Random.Shared.NextDouble() < 0.75;
        var questionPrompt = isQuestionWrittenByAgent
            ? """
                    Questions are short, typically 3-6 words, and are not always full sentences. They may look
                    like search queries or things typed in a hurry by a support agent. They are not polite or
                    verbose, since they are addressed to a machine.
                    Example questions might be "weight", "what are the dimensions", "how to shut down",
                    "can use on pets?", "what accessories does it come with?"
                    """
            : """
                    The question is actually an entire email written by a customer. It usually starts with a
                    greeting, some description of their situation, and then their question. The whole thing
                    may be up to 100 words long. It may contain spelling and grammar errors, and may be angry
                    or rude.
                    """;

        var question = await GetAndParseJsonChatCompletion<EvalQuestion>($$"""
                    There is an AI system used by customer support agents working for an online retailer.
                    The AI system is used to help agents answer customer questions.

                    Your task is to write question/answer pairs that will be used to evaluate the
                    performance of that AI system. All the questions you write will be about actual products
                    sold by that retailer, based on information from the product catalog and manuals. The
                    questions should plausibly represent what customers and support agents will ask.

                    In this case, you are to write a question/answer pair based on the following context:

                    <product_name>{{product.Model}}</product_name>
                    <brand>{{product.Brand}}</brand>
                    <category>{{category.Name}}</category>
                    <extract_from_manual>{{manualExtract}}</extract_from_manual>

                    Questions are one of the following types:
                     - A pre-purchase question to help a customer who wants to know about the product
                       features, suitability for particular use cases, or other objective facts
                     - A post-purchase question to help a customer resolve an issue or understand how to
                       use the product

                    You must select an OBJECTIVE FACT from the product manual and write a question to which
                    that fact is the answer. Only select facts that are distinctive about this specific product.

                    Always follow these style guidelines:
                     - {{questionPrompt}}
                     - Answers are short, typically a single brief sentence of 1-10 words. Never use more than
                       20 words for an answer.
                     - The "verbatim_quote_from_manual" is 3-6 words taken EXACTLY from the manual which are
                       the factual basis for the question and asnwer.

                    Respond as JSON in the following form: {
                        "question": "string",
                        "answer": "string",
                        "verbatimQuoteFromManual": "string"
                    }
                    """);
        question.ProductId = product.ProductId;
        return question;
    }

    private string ExtractFromManual(Manual manual)
    {
        // We don't want to push the entire manual text into the prompt as it may be arbitrarily long
        // Instead, pick a lengthy chunk at random.
        // TODO: Consider storing the markdown in a more structured, per-TOC-section way, so
        // we can more easily extract a coherent section of text.
        var approxExtractLengthInChars = 1500;
        var startChar = Random.Shared.Next(manual.MarkdownText.Length - approxExtractLengthInChars);

        // Find the line containing this char
        var lines = manual.MarkdownText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var lineIndexContainingStartChar = 0;
        for (var numCharsSeen = 0; numCharsSeen < startChar; lineIndexContainingStartChar++)
        {
            numCharsSeen += lines[lineIndexContainingStartChar].Length;
        }

        // Add lines until we have enough text
        var extract = new StringBuilder();
        for (var i = lineIndexContainingStartChar; i < lines.Length; i++)
        {
            extract.AppendLine(lines[i]);
            if (extract.Length >= approxExtractLengthInChars)
            {
                break;
            }
        }

        return extract.ToString();
    }
}
