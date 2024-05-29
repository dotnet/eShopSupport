using eShopSupport.DataGenerator.Model;

namespace eShopSupport.DataGenerator.Generators;

public class EvalQuestionGenerator(IReadOnlyList<Product> products, IReadOnlyList<Manual> manuals, IServiceProvider services)
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

        var numQuestions = 2;
        var batchSize = 2;
        var questionId = 0;
        while (questionId < numQuestions)
        {
            var numInBatch = Math.Min(batchSize, numQuestions - questionId);
            var ticketsInBatch = await Task.WhenAll(Enumerable.Range(0, numInBatch).Select(async _ =>
            {
                var question = await GetAndParseJsonChatCompletion<EvalQuestion>($$"""
                    You are writing question/answer pairs that will be used to evaluate the performance of a
                    AI system that answers questions.

                    Write a question about general knowledge.

                    Respond as JSON in the following form: {
                        "question": "string",
                        "answer": "string"
                    }
                    """);
                return question;
            }));

            foreach (var question in ticketsInBatch)
            {
                question.QuestionId = ++questionId;
                yield return question;
            }
        }
    }
}
