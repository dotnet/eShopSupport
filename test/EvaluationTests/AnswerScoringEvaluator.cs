// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// --------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Microsoft.Extensions.AI.Evaluation.Quality;

public sealed class AnswerScoringEvaluator : ChatConversationEvaluator
{

    public sealed class Context(string expectedAnswer) : EvaluationContext
    {
        public string ExpectedAnswer { get; } = expectedAnswer;
    }

    const string MetricName = "Answer Score";

    protected override bool IgnoresHistory => true;

    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];
    
    protected override EvaluationResult InitializeResult()
    {
        return new EvaluationResult(new NumericMetric(MetricName));
    }

    protected override async ValueTask<string> RenderEvaluationPromptAsync(
        ChatMessage? userRequest,
        ChatMessage modelResponse,
        IEnumerable<ChatMessage>? includedHistory,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken token)
    {
        string renderedModelResponse = await this.RenderAsync(modelResponse, token);

        string renderedUserRequest =
            userRequest is not null
                ? await this.RenderAsync(userRequest, token)
                : string.Empty;

        string answer = "";

        if (additionalContext is not null &&
            additionalContext.OfType<Context>().FirstOrDefault() is Context context)
        {
            answer = context.ExpectedAnswer;
        }
        else
        {
            throw new InvalidOperationException($"The ExpectedAnswer must be provided in the additional context.");
        }

        List<string> scoreWords = ["Awful", "Poor", "Good", "Perfect"];

        var prompt = $$"""
        There is an AI assistant that answers questions about products sold by an online retailer. The questions
        may be asked by customers or by customer support agents.

        You are evaluating the quality of an AI assistant's response to several questions. Here are the
        questions, the desired true answers, and the answers given by the AI system:

        <questions>
            <question index="0">
                <text>{{renderedUserRequest}}</text>
                <truth>{{answer}}</truth>
                <assistantAnswer>{{renderedModelResponse}}</assistantAnswer>
            </question>
        </questions>

        Evaluate each of the assistant's answers separately by replying in this JSON format:

        {
            "scores": [
                { "index": 0, "descriptionOfQuality": string, "scoreLabel": number },
                { "index": 1, "descriptionOfQuality": string, "scoreLabel": number },
                ... etc ...
            ]
        ]

        Score only based on whether the assistant's answer is true and answers the question. As long as the
        answer covers the question and is consistent with the truth, it should score as perfect. There is
        no penalty for giving extra on-topic information or advice. Only penalize for missing necessary facts
        or being misleading.

        The descriptionOfQuality should be up to 5 words summarizing to what extent the assistant answer
        is correct and sufficient.

        Based on descriptionOfQuality, the scoreLabel must be a number between 1 and 5 inclusive, where 5 is best and 1 is worst.
        Do not use any other words for scoreLabel. You may only pick one of those scores.
        
        """
        ;

        return prompt;
    }

    protected override ValueTask ParseEvaluationResponseAsync(
        string modelResponseForEvaluationPrompt, 
        EvaluationResult result,
        ChatConfiguration configuration,
        CancellationToken token)
    {
        bool hasMetric = result.TryGet<NumericMetric>(MetricName, out var numericMetric);
        if (!hasMetric || numericMetric is null)
        {
            throw new Exception("NumericMetric was not properly initialized.");
        }

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        
        var parsedResponse = JsonSerializer.Deserialize<ScoringResponse>(TrimMarkdownDelimiters(modelResponseForEvaluationPrompt), jsonOptions)!;
        var score = parsedResponse.Scores.FirstOrDefault();

        if (score == null)
        {
            numericMetric.AddDiagnostic(EvaluationDiagnostic.Error("Score was inconclusive"));
        } 
        else
        {
            numericMetric.Value = score.ScoreLabel;

            if (!string.IsNullOrWhiteSpace(score.DescriptionOfQuality))
            {
                numericMetric.AddDiagnostic(EvaluationDiagnostic.Informational(score.DescriptionOfQuality));
            }
        }

        numericMetric.Interpretation = Interpret(numericMetric);

        return new ValueTask();
    }

    internal static EvaluationMetricInterpretation Interpret(NumericMetric metric)
    {
        double score = metric?.Value ?? -1.0;
        EvaluationRating rating = score switch {
            1.0 => EvaluationRating.Unacceptable,
            2.0 => EvaluationRating.Poor,
            3.0 => EvaluationRating.Average,
            4.0 => EvaluationRating.Good,
            5.0 => EvaluationRating.Exceptional,
            _ => EvaluationRating.Inconclusive,
        };
        return new EvaluationMetricInterpretation(rating, failed: rating == EvaluationRating.Inconclusive);
    }

    internal static ReadOnlySpan<char> TrimMarkdownDelimiters(string json)
    {
#if NETSTANDARD2_0
        ReadOnlySpan<char> trimmed = json.ToCharArray();
#else
        ReadOnlySpan<char> trimmed = json;
#endif
        trimmed = trimmed.Trim().Trim(['`']); // trim whitespace and markdown characters from beginning and end
        // trim 'json' marker from markdown if it exists
        if (trimmed.Length > 4 && trimmed[0..4].SequenceEqual(['j', 's', 'o', 'n']))
        {
            trimmed = trimmed.Slice(4);
        }

        return trimmed;
    }


}

record ScoringResponse(AnswerScore[] Scores);
record AnswerScore(int Index, int ScoreLabel, string DescriptionOfQuality);
