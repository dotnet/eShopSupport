using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Experimental.AI.LanguageModels;

namespace eShopSupport.ServiceDefaults.Clients.ChatCompletion;

internal class OllamaChatCompletionService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly string _modelName;
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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
        // We have to use the "generate" endpoint, not "chat", because function calling requires raw mode
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/generate");
        request.Content = PrepareChatRequestContent(messages, options, false);
        var json = options.ResponseFormat == ChatResponseFormat.JsonObject;
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var responseText = "ERROR: The configured model isn't available. Perhaps it's still downloading.";
            return [new ChatMessage(ChatMessageRole.Assistant, json ? JsonSerializer.Serialize(responseText) : responseText)];
        }

        var responseContent = await response.Content.ReadFromJsonAsync<OllamaResponseStreamEntry>(_jsonSerializerOptions, cancellationToken);
        return [new ChatMessage(ChatMessageRole.Assistant, responseContent!.Response!)];
    }

    public async IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // We have to use the "generate" endpoint, not "chat", because function calling requires raw mode
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/generate");
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
            else if (entry is { Response: { } chunkText })
            {
                yield return new ChatMessageChunk(ChatMessageRole.Assistant, chunkText);
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
            Prompt = FormatRawPrompt(messages, options.Tools),
            Format = options.ResponseFormat == ChatResponseFormat.JsonObject ? "json" : null,
            Options = new
            {
                Temperature = options.Temperature ?? 0.5,
                NumPredict = options.MaxTokens,
                TopP = options.TopP ?? 1.0,
                Stop = (options.StopSequences ?? Enumerable.Empty<string>()).Concat(["\n\n", "[/TOOL_CALLS]"]),
            },
            Raw = true,
            Stream = streaming,
        }, options: _jsonSerializerOptions);
    }

    private static string FormatRawPrompt(IReadOnlyList<ChatMessage> messages, IEnumerable<ChatTool>? tools)
    {
        // TODO: First fetch the prompt template for the model via /api/show, and then use
        // that to format the messages. Currently this is hardcoded to the Mistral prompt,
        // i.e.: [INST] {{ if .System }}{{ .System }} {{ end }}{{ .Prompt }} [/INST]
        var sb = new StringBuilder();
        var indexOfLastUserOrSystemMessage = IndexOfLast(messages, m => m.Role is ChatMessageRole.User or ChatMessageRole.System);

        // IMPORTANT: The whitespace in the prompt is significant. Do not add or remove extra spaces/linebreaks,
        // as this affects tokenization. Mistral's function calling is useless unless you get this exactly right.

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];

            // Emit tools descriptor immediately before the final [INST]
            if (index == indexOfLastUserOrSystemMessage)
            {
                if (tools is not null)
                {
                    sb.Append("[AVAILABLE_TOOLS] ");
                    sb.Append(JsonSerializer.Serialize(tools.OfType<OllamaChatFunction>().Select(t => t.Tool), _jsonSerializerOptions));
                    sb.Append("[/AVAILABLE_TOOLS]");
                }
            }

            switch (message.Role)
            {
                case ChatMessageRole.User:
                case ChatMessageRole.System:
                    sb.Append("[INST] ");
                    sb.Append(message.Content);
                    sb.Append(" [/INST]");
                    break;
                case ChatMessageRole.Assistant:
                    sb.Append(message.Content);
                    sb.Append("</s> "); // That's right, there's no matching <s>. See https://discuss.huggingface.co/t/skew-between-mistral-prompt-in-docs-vs-chat-template/66674/2
                    break;
                case ChatMessageRole.Tool:
                    if (message.ToolCalls is not null)
                    {
                        sb.Append("[TOOL_CALLS] ");
                        sb.Append(JsonSerializer.Serialize(message.ToolCalls.OfType<OllamaChatMessageToolCall>(), _jsonSerializerOptions));
                        sb.Append(" [/TOOL_CALLS]");
                    }
                    break;
            }
        }

        return sb.ToString();
    }

    private static int IndexOfLast<T>(IReadOnlyList<T> messages, Func<T, bool> value)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (value(messages[i]))
            {
                return i;
            }
        }

        return -1;
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
        => OllamaChatFunction.Create(name, description, @delegate);

    private class OllamaChatFunction(string name, string description) : ChatFunction(name, description)
    {
        // When JSON-serialized, needs to produce a structure like this:
        // {
        //   "type": "function",
        //   "function": {
        //     "name": "get_current_weather",
        //     "description": "Get the current weather",
        //     "parameters": {
        //       "type": "object",
        //       "properties": {
        //         "location": {
        //           "type": "string",
        //           "description": "The city or country name"
        //         },
        //         "format": {
        //           "type": "string",
        //           "enum": ["celsius", "fahrenheit"],
        //           "description": "The temperature unit to use. Infer this from the users location."
        //         }
        //       },
        //       "required": ["location", "format"]
        //     }
        //   }
        // }

        public required ToolDescriptor Tool { get; set; }

        public record ToolDescriptor(string Type, FunctionDescriptor Function);
        public record FunctionDescriptor(string Name, string Description, FunctionParameters Parameters);
        public record FunctionParameters(string Type, Dictionary<string, ParameterDescriptor> Properties, string[] Required);
        public record ParameterDescriptor(string Type, string? Description, string[]? Enum);

        public static OllamaChatFunction Create<T>(string name, string description, T @delegate) where T : Delegate
            => new OllamaChatFunction(name, description)
            { 
                Tool = new ToolDescriptor("function", new FunctionDescriptor(name, description, ToFunctionParameters(@delegate)))
            };

        private static FunctionParameters ToFunctionParameters<T>(T @delegate) where T: Delegate
        {
            var parameters = @delegate.Method.GetParameters();
            return new FunctionParameters(
                "object",
                parameters.ToDictionary(p => p.Name!, ToParameterDescriptor),
                parameters.Where(p => !p.IsOptional).Select(p => p.Name!).ToArray());
        }

        private static ParameterDescriptor ToParameterDescriptor(ParameterInfo parameterInfo)
            => new ParameterDescriptor(
                ToParameterType(parameterInfo.ParameterType),
                GetParameterDescription(parameterInfo),
                ToEnumValues(parameterInfo.ParameterType));

        private static string? GetParameterDescription(ParameterInfo parameter)
            => parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;

        private static string[]? ToEnumValues(Type type)
            => type.IsEnum ? Enum.GetNames(type) : null;

        private static string ToParameterType(Type parameterType)
        {
            parameterType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
            if (parameterType == typeof(int))
            {
                return "number";
            }
            else if (parameterType == typeof(string))
            {
                return "string";
            }
            else
            {
                throw new NotSupportedException($"Unsupported parameter type {parameterType}");
            }
        }
    }

    private class OllamaResponseStreamEntry
    {
        public bool Done { get; set; }
        public string? Response { get; set; }
    }

    private class OllamaChatMessageToolCall : ChatMessageToolCall
    {
        public required string Name { get; set; }
        public required object[] Arguments { get; set; }
        public object? Result { get; set; }
    }
}
