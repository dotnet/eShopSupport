using Azure.AI.OpenAI;
using eShopSupport.ServiceDefaults.Clients.Backend;
using eShopSupport.StaffWebUI.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SmartComponents.Inference;
using SmartComponents.Infrastructure;
using SmartComponents.StaticAssets.Inference;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFluentUIComponents();

builder.Services.AddHttpClient<BackendClient>(client =>
    client.BaseAddress = new Uri("http://backend/"));

builder.Services.AddSmartComponents().WithInferenceBackend<NotImplementedInferenceBackend>();
builder.Services.AddScoped<SmartTextAreaInference, BackendSmartTextAreaInference>();

//builder.AddOllamaChatCompletionService("eshopsupport-ollama");

builder.AddAzureOpenAIClient("eshopsupport-openai");
builder.Services.AddScoped<IChatCompletionService>(s =>
{
    var client = s.GetRequiredService<OpenAIClient>();
    return new OpenAIChatCompletionService("gpt-35-1106", client);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/manual", async (string file, BackendClient backend, CancellationToken cancellationToken) =>
{
    var result = await backend.ReadManualAsync(file, cancellationToken);
    return result is null ? Results.NotFound() : Results.Stream(result, "application/pdf");
});

app.Run();

class BackendSmartTextAreaInference(IChatCompletionService chatCompletionService) : SmartTextAreaInference
{
    public override async Task<string> GetInsertionSuggestionAsync(IInferenceBackend inference, SmartTextAreaConfig config, string textBefore, string textAfter)
    {
        ChatParameters prompt = BuildPrompt(config, textBefore, textAfter);
        var chatHistory = new ChatHistory();
        foreach (var message in prompt.Messages ?? Enumerable.Empty<ChatMessage>())
        {
            var role = message.Role switch
            {
                ChatMessageRole.User => AuthorRole.User,
                ChatMessageRole.Assistant => AuthorRole.Assistant,
                ChatMessageRole.System => AuthorRole.System,
                _ => throw new NotImplementedException($"Unknown role: " + message.Role)
            };
            chatHistory.Add(new ChatMessageContent(role, message.Text));
        }

        var result = await chatCompletionService.GetChatMessageContentAsync(chatHistory, new OpenAIPromptExecutionSettings
        {
            Temperature = prompt.Temperature ?? 1.0,
            TopP = prompt.TopP ?? 1.0,
            MaxTokens = prompt.MaxTokens,
            FrequencyPenalty = prompt.FrequencyPenalty ?? 0,
            PresencePenalty = prompt.PresencePenalty ?? 0,
            StopSequences = prompt.StopSequences,
        });

        var resultString = result.Content!;
        return resultString.StartsWith("OK:[") && resultString.EndsWith("]") ? resultString[4..^1] : string.Empty;
    }
}

class NotImplementedInferenceBackend : IInferenceBackend
{
    public Task<string> GetChatResponseAsync(ChatParameters options)
        => throw new NotImplementedException();
}
