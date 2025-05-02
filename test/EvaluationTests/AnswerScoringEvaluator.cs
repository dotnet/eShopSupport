// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// --------------------------------------------------------------------------------------------

namespace Microsoft.Extensions.AI.Evaluation.Quality;

public sealed class AnswerScoringEvaluator : IEvaluator
{
    public sealed class Context(string expectedAnswer) : EvaluationContext(ContextName, content: expectedAnswer)
    {
        private const string ContextName = "Answer Score";

        public string ExpectedAnswer { get; } = expectedAnswer;
    }

    private const string MetricName = "Answer Score";

    public IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    public async ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modelResponse);
        ArgumentNullException.ThrowIfNull(chatConfiguration);

        var numericMetric = new NumericMetric(MetricName);
        var result = new EvaluationResult(numericMetric);

        if (!messages.TryGetUserRequest(out ChatMessage? userRequest, out IReadOnlyList<ChatMessage> conversationHistory))
        {
            result.AddDiagnosticsToAllMetrics(
                EvaluationDiagnostic.Error(
                    $"The ${messages} supplied for evaluation did not contain a user request as the last message."));
            return result;
        }

        if (string.IsNullOrWhiteSpace(modelResponse.Text))
        {
            result.AddDiagnosticsToAllMetrics(
                EvaluationDiagnostic.Error($"The {nameof(modelResponse)} supplied for evaluation was null or empty."));
            return result;
        }

        var evaluationInstructions = GetEvaluationInstructions(
            userRequest,
            modelResponse,
            conversationHistory,
            additionalContext);

        var response = await chatConfiguration.ChatClient.GetResponseAsync<ScoringResponse>(
            evaluationInstructions,
            cancellationToken: cancellationToken);

        if (!response.TryGetResult(out var scoringResponse))
        {
            result.AddDiagnosticsToAllMetrics(
                EvaluationDiagnostic.Error("Scoring response was not provided in a valid format."));
            return result;
        }

        if (scoringResponse.Scores is not [var score, ..])
        {
            result.AddDiagnosticsToAllMetrics(
                EvaluationDiagnostic.Error("Scoring response contained no scores."));
            return result;
        }

        numericMetric.Value = score.ScoreLabel;

        if (!string.IsNullOrWhiteSpace(score.DescriptionOfQuality))
        {
            numericMetric.AddDiagnostics(EvaluationDiagnostic.Informational(score.DescriptionOfQuality));
        }

        numericMetric.Interpretation = Interpret(numericMetric);
        return result;
    }

    private static List<ChatMessage> GetEvaluationInstructions(
        ChatMessage? userRequest,
        ChatResponse modelResponse,
        IEnumerable<ChatMessage> includedHistory,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        string renderedModelResponse = modelResponse.RenderText();
        string renderedUserRequest = userRequest?.RenderText() ?? string.Empty;
        string answer;

        if (additionalContext is not null &&
            additionalContext.OfType<Context>().FirstOrDefault() is Context context)
        {
            answer = context.ExpectedAnswer;
        }
        else
        {
            throw new InvalidOperationException($"The ExpectedAnswer must be provided in the additional context.");
        }

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

        return [new ChatMessage(ChatRole.User, prompt)];
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
}

record ScoringResponse(AnswerScore[] Scores);
record AnswerScore(int Index, int ScoreLabel, string DescriptionOfQuality);
