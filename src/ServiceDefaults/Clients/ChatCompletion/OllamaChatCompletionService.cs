using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace eShopSupport.ServiceDefaults.Clients.ChatCompletion;

internal class OllamaChatCompletionService : IChatCompletionService
{
    private readonly HttpClient _httpClient;
    private readonly string _modelName;
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public OllamaChatCompletionService(HttpClient httpClient, string modelName)
    {
        _httpClient = httpClient;
        _modelName = modelName;
    }

    public IReadOnlyDictionary<string, object?> Attributes => throw new NotImplementedException();

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat");
        request.Content = PrepareChatRequestContent(chatHistory, executionSettings, false, out var json);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var responseText = "ERROR: The configured model isn't available. Perhaps it's still downloading.";
            return [new ChatMessageContent(AuthorRole.Assistant, json ? JsonSerializer.Serialize(responseText) : responseText)];
        }

        var responseContent = await response.Content.ReadFromJsonAsync<OllamaResponseStreamEntry>(_jsonSerializerOptions, cancellationToken);
        return [new ChatMessageContent(AuthorRole.Assistant, responseContent!.Message!.Content)];
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat");
        request.Content = PrepareChatRequestContent(chatHistory, executionSettings, true, out var json);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var responseText = "ERROR: The configured model isn't available. Perhaps it's still downloading.";
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, json ? JsonSerializer.Serialize(responseText) : responseText);
            yield break;
        }

        var responseStream = await response.Content.ReadAsStreamAsync();
        using var streamReader = new StreamReader(responseStream);
        var line = (string?)null;
        while ((line = await streamReader.ReadLineAsync()) is not null)
        {
            var entry = JsonSerializer.Deserialize<OllamaResponseStreamEntry>(line, _jsonSerializerOptions);
            if (entry is { Done: true })
            {
                break;
            }
            else if (entry is { Message: { } message })
            {
                yield return new StreamingChatMessageContent(FromOllamaRole(message.Role), message.Content);
            }
            else
            {
                throw new InvalidOperationException("Invalid response entry from Ollama");
            }
        }
    }

    private JsonContent PrepareChatRequestContent(ChatHistory chatHistory, PromptExecutionSettings? executionSettings, bool streaming, out bool json)
    {
        var openAiPromptExecutionSettings = executionSettings as OpenAIPromptExecutionSettings;
        json = openAiPromptExecutionSettings?.ResponseFormat is "json_object";
        return JsonContent.Create(new
        {
            Model = _modelName,
            Messages = chatHistory.Select(m => new
            {
                Role = ToOllamaRole(m.Role),
                Content = m.ToString(),
            }),
            Format = json ? "json" : null,
            Options = new
            {
                Temperature = openAiPromptExecutionSettings?.Temperature ?? 0.5,
                NumPredict = openAiPromptExecutionSettings?.MaxTokens ?? 200,
                TopP = openAiPromptExecutionSettings?.TopP ?? 1.0,
                Stop = openAiPromptExecutionSettings?.StopSequences,
            },
            Stream = streaming,
        }, options: _jsonSerializerOptions);
    }

    private static string ToOllamaRole(AuthorRole role)
    {
        if (role == AuthorRole.Assistant)
        {
            return "assistant";
        }
        else if (role == AuthorRole.User)
        {
            return "user";
        }
        else if (role == AuthorRole.System)
        {
            return "system";
        }
        else
        {
            return role.ToString().ToLower();
        }
    }

    private static AuthorRole FromOllamaRole(string role) => role switch
    {
        "assistant" => AuthorRole.Assistant,
        "user" => AuthorRole.User,
        "system" => AuthorRole.System,
        _ => new AuthorRole(role),
    };

    private class OllamaResponseStreamEntry
    {
        public bool Done { get; set; }
        public OllamaResponseStreamEntryMessage? Message { get; set; }
    }

    private class OllamaResponseStreamEntryMessage
    {
        public required string Role { get; set; }
        public required string Content { get; set; }
    }
}
