

using System.Data.Common;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage.Disk;
using Microsoft.ML.Tokenizers;

namespace eShopSupport.EvaluationTests
{
    public class EvaluationTests : IAsyncLifetime
    {
        public static IEnumerable<EvalQuestion> Questions => LoadEvaluationQuestions()
            .OrderBy(a => a.QuestionId);

        public static StaffBackendClient? backend = null;
        public static IChatClient? chatCompletion = null;

        public async Task InitializeAsync()
        {
            backend = await DevToolBackendClient.GetDevToolStaffBackendClientAsync(
                identityServerHttpClient: new HttpClient { BaseAddress = new Uri("https://localhost:7275/") },
                backendHttpClient: new HttpClient { BaseAddress = new Uri("https://localhost:7223/") });
            chatCompletion = GetChatCompletionService("chatcompletion");
        }

        public Task DisposeAsync() => Task.CompletedTask;

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

        static IChatClient GetChatCompletionService(string connectionStringName)
        {
            var connectionStringBuilder = new DbConnectionStringBuilder();
            var connectionString = "Endpoint=https://aievaluation-openai.openai.azure.com/;Deployment=gpt-4o";
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException($"Missing connection string {connectionStringName}");
            }

            connectionStringBuilder.ConnectionString = connectionString;

            var deployment = connectionStringBuilder.TryGetValue("Deployment", out var deploymentValue) ? (string)deploymentValue : throw new InvalidOperationException($"Connection string {connectionStringName} is missing 'Deployment'");
            var endpoint = connectionStringBuilder.TryGetValue("Endpoint", out var endpointValue) ? (string)endpointValue : throw new InvalidOperationException($"Connection string {connectionStringName} is missing 'Endpoint'");
            //var key = connectionStringBuilder.TryGetValue("Key", out var keyValue) ? (string)keyValue : throw new InvalidOperationException($"Connection string {connectionStringName} is missing 'Key'");

            return new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential()).AsChatClient(deployment);
        }

        static ReportingConfiguration GetReportingConfiguration()
        {
            // Setup and configure the evaluators you would like to utilize for each AI chat
            IEvaluator rtcEvaluator =
                new RelevanceTruthAndCompletenessEvaluator(
                    new RelevanceTruthAndCompletenessEvaluator.Options(includeReasoning: true));
            IEvaluator coherenceEvaluator = new CoherenceEvaluator();
            IEvaluator fluencyEvaluator = new FluencyEvaluator();
            IEvaluator groundednessEvaluator = new GroundednessEvaluator();
            IEvaluator answerScoringEvaluator = new AnswerScoringEvaluator();

            var endpoint = new Uri(AzureOpenAIEndpoint);
            var azureClient = new AzureOpenAIClient(endpoint, new DefaultAzureCredential());

            IChatClient chatClient = azureClient.AsChatClient(AzureOpenAIDeploymentName);
            Tokenizer tokenizer = TiktokenTokenizer.CreateForModel(AzureOpenAIModelName);

            var chatConfig = new ChatConfiguration(chatClient, tokenizer.ToTokenCounter(6000));

            return DiskBasedReportingConfiguration.Create(
                    storageRootPath: StorageRootPath,
                    chatConfiguration: chatConfig,
                    evaluators: [
                        rtcEvaluator,
                        coherenceEvaluator,
                        fluencyEvaluator,
                        groundednessEvaluator,
                        answerScoringEvaluator],
                    executionName: ExecutionName);
        }

        const string StorageRootPath = @"C:\src\eShopCache";
        const string AzureOpenAIEndpoint = "https://aievaluation-openai.openai.azure.com/";
        const string AzureOpenAIModelName = "gpt-4o";
        const string AzureOpenAIDeploymentName = "gpt-4o";
        static readonly string ExecutionName = $"{DateTime.UtcNow:yyyyMMddTHHmmss}";

        [Fact]
        public async Task EvaluateQuestionsInALoop()
        {

            const int scoringParallelism = 4;
            var reportingConfiguration = GetReportingConfiguration();

            await Parallel.ForEachAsync(Questions.Take(5), new ParallelOptions { MaxDegreeOfParallelism = scoringParallelism }, (Func<EvalQuestion, CancellationToken, ValueTask>)(async (question, cancellationToken) =>
            {

                for (int i = 0; i < 3; i++)
                {
                    await EvaluateQuestion(question, reportingConfiguration, i, cancellationToken);
                }
            }));
        }

        public static TheoryData<EvalQuestion> EvalQuestions => [.. LoadEvaluationQuestions().OrderBy(a => a.QuestionId).Take(5)];

        [Theory]
        [MemberData(nameof(EvalQuestions))]
        public async Task EvaluateQuestionsWithMemberData(EvalQuestion question)
        {
            var reportingConfiguration = GetReportingConfiguration();
            for (int i = 0; i < 3; i++)
            {
                await EvaluateQuestion(question, reportingConfiguration, i, CancellationToken.None);
            }
        }

        [Fact]
        public async Task EvaluateQuestion_HowToAccessEssentials()
        {
            var reportingConfiguration = GetReportingConfiguration();
            var question = new EvalQuestion
            {
                QuestionId = 1,
                ProductId = 158,
                Question = "How to access essentials?",
                Answer = "Unzip the main compartment"
            };
            await EvaluateQuestion(question, reportingConfiguration, 0, CancellationToken.None);
        }

        [Fact]
        public async Task EvaluateQuestion_WhatAreTheOverheatingPrecautions()
        {
            var reportingConfiguration = GetReportingConfiguration();
            var question = new EvalQuestion
            {
                QuestionId = 2,
                ProductId = 199,
                Question = "What are the overheating precautions?",
                Answer = "Do not leave in direct sunlight for extended periods."
            };

            await EvaluateQuestion(question, reportingConfiguration, 0, CancellationToken.None);
        }

        [Fact]
        public async Task EvaluateQuestion_Summit3000TrekkingBackpackStrapAdjustment()
        {
            var reportingConfiguration = GetReportingConfiguration();
            var question = new EvalQuestion
            {
                QuestionId = 3,
                ProductId = 99,
                Question = "Hi there, I recently purchased the Summit 3000 Trekking Backpack and I\u0027m having issues with the strap adjustment. Can you provide me with the specified torque value for the strap adjustment bolts?",
                Answer = "15-20 Nm"
            };
            await EvaluateQuestion(question, reportingConfiguration, 0, CancellationToken.None);
        }

        private static async Task EvaluateQuestion(EvalQuestion question, ReportingConfiguration reportingConfiguration, int i, CancellationToken cancellationToken)
        {
            await using ScenarioRun scenario = await reportingConfiguration.CreateScenarioRunAsync($"Question_{question.QuestionId}", $"Iteration {i + 1}", cancellationToken: cancellationToken);

            var responseItems = backend!.AssistantChatAsync(new AssistantChatRequest(
                question.ProductId,
                null,
                null,
                null,
                [new() { IsAssistant = true, Text = question.Question }]),
                cancellationToken);

            var answerBuilder = new StringBuilder();
            await foreach (var item in responseItems)
            {
                if (item.Type == AssistantChatReplyItemType.AnswerChunk)
                {
                    answerBuilder.Append(item.Text);
                }
            }

            var finalAnswer = answerBuilder.ToString();

            EvaluationResult evalResult = await scenario.EvaluateAsync(
                [new ChatMessage(ChatRole.User, question.Question)],
                new ChatMessage(ChatRole.Assistant, finalAnswer),
                additionalContext: [new AnswerScoringEvaluator.Context(question.Answer)],
                cancellationToken);


            Assert.False(evalResult.Metrics.Values.Any(m => m.Interpretation?.Rating == EvaluationRating.Inconclusive), "Model response was inconclusive");
            Assert.False(evalResult.ContainsDiagnostics(d => d.Severity == EvaluationDiagnosticSeverity.Error), "Evaluation had errors.");
        }
    }
}
