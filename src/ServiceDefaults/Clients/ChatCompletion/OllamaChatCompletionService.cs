using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Experimental.AI.LanguageModels;

namespace eShopSupport.ServiceDefaults.Clients.ChatCompletion;

internal class OllamaChatCompletionService : IChatService
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

    public async Task<IReadOnlyList<ChatMessage>> CompleteChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat");
        request.Content = PrepareChatRequestContent(messages, options, false);
        var json = options.ResponseFormat == ChatResponseFormat.JsonObject;
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var responseText = "ERROR: The configured model isn't available. Perhaps it's still downloading.";
            return [new ChatMessage(ChatMessageRole.Assistant, json ? JsonSerializer.Serialize(responseText) : responseText)];
        }

        var responseContent = await response.Content.ReadFromJsonAsync<OllamaResponseStreamEntry>(_jsonSerializerOptions, cancellationToken);
        return [new ChatMessage(ChatMessageRole.Assistant, responseContent!.Message!.Content)];
    }

    public async IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat");
        request.Content = PrepareChatRequestContent(messages, options, true);
        var json = options.ResponseFormat == ChatResponseFormat.JsonObject;

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var responseText = "ERROR: The configured model isn't available. Perhaps it's still downloading.";
            yield return new ChatMessageChunk(ChatMessageRole.Assistant, json ? JsonSerializer.Serialize(responseText) : responseText);
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
                yield return new ChatMessageChunk(FromOllamaRole(message.Role), message.Content);
            }
            else
            {
                throw new InvalidOperationException("Invalid response entry from Ollama");
            }
        }
    }

    private JsonContent PrepareChatRequestContent(IReadOnlyList<ChatMessage> messages, ChatOptions options, bool streaming)
    {
        return JsonContent.Create(new
        {
            Model = _modelName,
            Messages = messages.Select(m => new
            {
                Role = ToOllamaRole(m.Role),
                Content = m.Content,
            }),
            Format = options.ResponseFormat == ChatResponseFormat.JsonObject ? "json" : null,
            Options = new
            {
                Temperature = options.Temperature ?? 0.5,
                NumPredict = options.MaxTokens,
                TopP = options.TopP ?? 1.0,
                Stop = options.StopSequences,
            },
            Stream = streaming,
        }, options: _jsonSerializerOptions);
    }

    private static string ToOllamaRole(ChatMessageRole role)
    {
        if (role == ChatMessageRole.Assistant)
        {
            return "assistant";
        }
        else if (role == ChatMessageRole.User)
        {
            return "user";
        }
        else if (role == ChatMessageRole.System)
        {
            return "system";
        }
        else
        {
            return role.ToString().ToLower();
        }
    }

    private static ChatMessageRole FromOllamaRole(string role) => role switch
    {
        "assistant" => ChatMessageRole.Assistant,
        "user" => ChatMessageRole.User,
        "system" => ChatMessageRole.System,
        _ => throw new NotSupportedException($"Unsupported message role: {role}"),
    };

    public ChatFunction CreateChatFunction<T>(string name, string description, T @delegate) where T : Delegate
        => new OllamaChatFunction(name, description);

    private class OllamaChatFunction(string name, string description) : ChatFunction(name, description) { }

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
