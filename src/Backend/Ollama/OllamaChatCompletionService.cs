using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace eShopSupport.Backend;

public class OllamaChatCompletionService : IChatCompletionService
{
    private readonly HttpClient _httpClient;
    private readonly string _modelName;
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public OllamaChatCompletionService(HttpClient httpClient, string modelName)
    {
        _httpClient = httpClient;
        _modelName = modelName;
    }

    public IReadOnlyDictionary<string, object?> Attributes => throw new NotImplementedException();

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat");
        var json = executionSettings is OpenAIPromptExecutionSettings { ResponseFormat: "json_object" };
        request.Content = JsonContent.Create(new
        {
            Model = _modelName,
            Messages = chatHistory.Select(m => new
            {
                Role = ToOllamaRole(m.Role),
                Content = m.ToString(),
            }),
            Format = json ? "json" : null,
        }, options: _jsonSerializerOptions);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
