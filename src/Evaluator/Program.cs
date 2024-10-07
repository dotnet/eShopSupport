using System.ClientModel;
using System.Data.Common;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using eShopSupport.Evaluator;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

// Comparing models:
//
// GPT 4o: After 100 questions: average score = 0.800, average duration = 9005.543ms
// GPT 3.5 Turbo: After 200 questions: average score = 0.733, average duration = 3450.547ms
// Mistral 7B (Ollama): After 100 questions: average score = 0.547, average duration = 25603.365ms
//
// ---
//
// Comparing prompts:
//
// Effect of adding "If this is a question about the product, you should ALWAYS search the manual."
// to the prompt:
//
// Without: After 200 questions: average score = 0.615, average duration = 2092.407ms
// With:    After 200 questions: average score = 0.775, average duration = 2670.874ms

var assistantAnsweringSemaphore = new SemaphoreSlim(/* parallelism */ 3);
var backend = await DevToolBackendClient.GetDevToolStaffBackendClientAsync(
    identityServerHttpClient: new HttpClient { BaseAddress = new Uri("https://localhost:7275/") },
    backendHttpClient: new HttpClient { BaseAddress = new Uri("https://localhost:7223/") });
var chatCompletion = GetChatCompletionService("chatcompletion");
var questions = LoadEvaluationQuestions().OrderBy(q => q.QuestionId);
using var logFile = File.Open("log.txt", FileMode.Create, FileAccess.Write, FileShare.Read);
using var log = new StreamWriter(logFile);

var questionBatches = questions.Chunk(5);
var scoringParallelism = 4;
var allScores = new List<double>();
var allDurations = new List<TimeSpan>();
await Parallel.ForEachAsync(questionBatches, new ParallelOptions { MaxDegreeOfParallelism = scoringParallelism }, async (batch, cancellationToken) =>
{
    var assistantAnswers = await Task.WhenAll(batch.Select(GetAssistantAnswerAsync));
    var scores = await ScoreAnswersAsync(batch.Zip(assistantAnswers.Select(a => a.Answer)).ToList());
    foreach (var (question, assistantAnswer, score) in batch.Zip(assistantAnswers, scores))
    {
        lock (log)
        {
            log.WriteLine($"Question ID: {question.QuestionId}");
            log.WriteLine($"Question: {question.Question}");
            log.WriteLine($"True answer: {question.Answer}");
            log.WriteLine($"Assistant answer: {assistantAnswer.Answer}");
            log.WriteLine($"Assistant duration: {assistantAnswer.Duration}");
            log.WriteLine($"Score: {score.Score}");
            log.WriteLine($"Justification: {score.Justification}");
            log.WriteLine();
            log.Flush();
            if (score.Score.HasValue)
            {
                allScores.Add(score.Score.Value);
                allDurations.Add(assistantAnswer.Duration);
            }
        }
    }

    Console.WriteLine($"After {allScores.Count} questions: average score = {allScores.Average():F3}, average duration = {allDurations.Select(d => d.TotalMilliseconds).Average():F3}ms");
});

async Task<(double? Score, string Justification)[]> ScoreAnswersAsync(IReadOnlyCollection<(EvalQuestion Question, string AssistantAnswer)> questionAnswerPairs)
{
    var rangeText = $"questions {questionAnswerPairs.Min(p => p.Question.QuestionId)} to {questionAnswerPairs.Max(p => p.Question.QuestionId)}";
    Console.WriteLine($"Scoring answers to {rangeText}...");
    var formattedQuestionAnswerPairs = questionAnswerPairs.Select((pair, index) => 
        $$"""
            <question index="{{index}}">
                <text>{{pair.Question.Question}}</text>
                <truth>{{pair.Question.Answer}}</truth>
                <assistantAnswer>{{pair.AssistantAnswer}}</assistantAnswer>
            </question>
        """);

    List<string> scoreWords = ["Awful", "Poor", "Good", "Perfect"];

    var prompt = $$"""
        There is an AI assistant that answers questions about products sold by an online retailer. The questions
        may be asked by customers or by customer support agents.

        You are evaluating the quality of an AI assistant's response to several questions. Here are the
        questions, the desired true answers, and the answers given by the AI system:

        <questions>
            {{string.Join("\n", formattedQuestionAnswerPairs)}}
        </questions>

        Evaluate each of the assistant's answers separately by replying in this JSON format:

        {
            "scores": [
                { "index": 0, "descriptionOfQuality": string, "scoreLabel": string },
                { "index": 1, "descriptionOfQuality": string, "scoreLabel": string },
                ... etc ...
            ]
        ]

        Score only based on whether the assistant's answer is true and answers the question. As long as the
        answer covers the question and is consistent with the truth, it should score as perfect. There is
        no penalty for giving extra on-topic information or advice. Only penalize for missing necessary facts
        or being misleading.

        The descriptionOfQuality should be up to 5 words summarizing to what extent the assistant answer
        is correct and sufficient.

        Based on descriptionOfQuality, the scoreLabel must be one of the following labels, from worst to best: {{string.Join(", ", scoreWords)}}
        Do not use any other words for scoreLabel. You may only pick one of those labels.
        """;

    var chatHistory = new List<ChatMessage> { new(ChatRole.User, prompt) };
    var promptExecutionSettings = new ChatOptions
    {
        ResponseFormat = ChatResponseFormat.Json,
        Temperature = 0,
        AdditionalProperties = new() { ["seed"] = 0 },
    };
    var response = await chatCompletion.CompleteAsync(chatHistory, promptExecutionSettings);
    var responseJson = response.ToString();
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var parsedResponse = JsonSerializer.Deserialize<ScoringResponse>(responseJson, jsonOptions)!;
    return parsedResponse.Scores.Select(s =>
    {
        var labelIndex = scoreWords.FindIndex(w => w.Equals(s.ScoreLabel, StringComparison.OrdinalIgnoreCase));
        return (labelIndex < 0 ? (double?)null : ((double)labelIndex) / (scoreWords.Count - 1), s.DescriptionOfQuality);
    }).ToArray();
}

static EvalQuestion[] LoadEvaluationQuestions()
{
    var questionDataPath = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "EvalQuestionsJsonPath").Value!;
    if (!File.Exists(questionDataPath))
    {
        throw new FileNotFoundException("Questions not found. Ensure the data ingestor has run.", questionDataPath);
    }
    var questionsJson = File.ReadAllText(questionDataPath);
    return JsonSerializer.Deserialize<EvalQuestion[]>(questionsJson)!;
}

async Task<(string Answer, TimeSpan Duration)> GetAssistantAnswerAsync(EvalQuestion question)
{
    await assistantAnsweringSemaphore.WaitAsync();
    var startTime = DateTime.Now;

    try
    {
        Console.WriteLine($"Asking question {question.QuestionId}...");
        var responseItems = backend.AssistantChatAsync(new AssistantChatRequest(
            question.ProductId,
            null,
            null,
            null,
            [new() { IsAssistant = true, Text = question.Question }]),
            CancellationToken.None);
        var answerBuilder = new StringBuilder();
        await foreach (var item in responseItems)
        {
            if (item.Type == AssistantChatReplyItemType.AnswerChunk)
            {
                answerBuilder.Append(item.Text);
            }
        }

        var duration = DateTime.Now - startTime;
        var finalAnswer = answerBuilder.ToString();
        Console.WriteLine($"Received answer to question {question.QuestionId}");
        return (string.IsNullOrWhiteSpace(finalAnswer) ? "No answer provided" : finalAnswer, duration);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error on question {question.QuestionId}: {ex.Message}");
        return ("SYSTEM ERROR", DateTime.Now - startTime);
    }
    finally
    {
        assistantAnsweringSemaphore.Release();
    }
}

static IChatClient GetChatCompletionService(string connectionStringName)
{
    var config = new ConfigurationManager();
    config.AddJsonFile("appsettings.json");
    config.AddJsonFile("appsettings.Local.json", optional: true);

    var connectionStringBuilder = new DbConnectionStringBuilder();
    var connectionString = config.GetConnectionString(connectionStringName);
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException($"Missing connection string {connectionStringName}");
    }

    connectionStringBuilder.ConnectionString = connectionString;

    var deployment = connectionStringBuilder.TryGetValue("Deployment", out var deploymentValue) ? (string)deploymentValue : throw new InvalidOperationException($"Connection string {connectionStringName} is missing 'Deployment'");
    var endpoint = connectionStringBuilder.TryGetValue("Endpoint", out var endpointValue) ? (string)endpointValue : throw new InvalidOperationException($"Connection string {connectionStringName} is missing 'Endpoint'");
    var key = connectionStringBuilder.TryGetValue("Key", out var keyValue) ? (string)keyValue : throw new InvalidOperationException($"Connection string {connectionStringName} is missing 'Key'");

    return new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(key)).AsChatClient(deployment);
}

record ScoringResponse(AnswerScore[] Scores);
record AnswerScore(int Index, string ScoreLabel, string DescriptionOfQuality);
