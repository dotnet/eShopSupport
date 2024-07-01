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
        request.Content = PrepareChatRequestContent(messages, options, false, allowTools: true);
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
        const int MaxIterations = 3;
        for (var iteration = 1; iteration <= MaxIterations; iteration++)
        {
            var toolCallBuilder = default(StringBuilder);
            var allowTools = iteration < MaxIterations;
            await foreach (var chunk in ProcessStreamingMessagesAsync(messages, options, allowTools, cancellationToken))
            {
                if (chunk.ContentUpdate is { } contentUpdate)
                {
                    yield return new ChatMessageChunk(ChatMessageRole.Assistant, chunk.ContentUpdate);
                }
                else if (chunk.ToolUpdate is { } toolUpdate)
                {
                    toolCallBuilder ??= new();
                    toolCallBuilder.Append(toolUpdate);
                }
            }

            var didCallTool = toolCallBuilder is { Length: > 0 };
            if (!didCallTool)
            {
                break;
            }

            var toolCallsJson = toolCallBuilder!.ToString().Trim();
            var toolCalls = JsonSerializer.Deserialize<OllamaChatMessageToolCall[]>(toolCallsJson, _jsonSerializerOptions)!;
            foreach (var toolCall in toolCalls)
            {
                var function = options.Tools?.FirstOrDefault(t => t.Name == toolCall.name);
                if (function is OllamaChatFunction ollamaChatFunction)
                {
                    toolCall.result = await ReflectionChatFunction.InvokeAsync(ollamaChatFunction.Delegate, toolCall.arguments);
                }
            }

            messages = new List<ChatMessage>(messages)
            {
                new ChatMessage(ChatMessageRole.Assistant, null) { ToolCalls = toolCalls },
            };
        }
    }

    private async IAsyncEnumerable<OllamaStreamingChunk> ProcessStreamingMessagesAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        bool allowTools,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // We have to use the "generate" endpoint, not "chat", because function calling requires raw mode
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/generate");
        request.Content = PrepareChatRequestContent(messages, options, true, allowTools);
        var json = options.ResponseFormat == ChatResponseFormat.JsonObject;

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var responseText = "ERROR: The configured model isn't available. Perhaps it's still downloading.";
            yield return OllamaStreamingChunk.Content(json ? JsonSerializer.Serialize(responseText) : responseText);
            yield break;
        }

        var responseStream = await response.Content.ReadAsStreamAsync();
        using var streamReader = new StreamReader(responseStream);
        var line = (string?)null;
        var isProcessingToolCalls = false;
        var capturingLeadingChunks = string.Empty;
        while ((line = await streamReader.ReadLineAsync()) is not null)
        {
            var entry = JsonSerializer.Deserialize<OllamaResponseStreamEntry>(line, _jsonSerializerOptions);
            if (entry is { Done: true })
            {
                break;
            }
            else if (entry is { Response: { } chunkText })
            {
                if (capturingLeadingChunks is not null)
                {
                    capturingLeadingChunks += chunkText;
                    const string explicitToolCallPrefix = "[TOOL_CALLS]";
                    if (capturingLeadingChunks.StartsWith(explicitToolCallPrefix))
                    {
                        isProcessingToolCalls = true;
                        yield return OllamaStreamingChunk.Tool(capturingLeadingChunks.Substring(explicitToolCallPrefix.Length));
                        capturingLeadingChunks = null;
                    }
                    else if (capturingLeadingChunks.TrimStart().StartsWith("[{\"name\":\"") && options.ResponseFormat != ChatResponseFormat.JsonObject)
                    {
                        // Mistral often forgets to prefix the tool call with [TOOL_CALLS], but it's still
                        // recognizable as one if it starts with this JSON when we didn't ask for JSON
                        isProcessingToolCalls = true;
                        yield return OllamaStreamingChunk.Tool(capturingLeadingChunks);
                        capturingLeadingChunks = null;
                    }
                    else if (capturingLeadingChunks.Length > 15)
                    {
                        // Give up on capturing the leading chunks. If it was going to be a tool call, we'd know by now.
                        yield return OllamaStreamingChunk.Content(capturingLeadingChunks);
                        capturingLeadingChunks = null;
                    }
                }
                else if (isProcessingToolCalls)
                {
                    if (chunkText.StartsWith('\n'))
                    {
                        // Tool call blocks can't include newlines because they would be escaped for JSON.
                        // This must signal the end of the tool call. Note that Mistral 0.3 might occasionally
                        // emit [/TOOL_CALLS] but it more often ends the tool call with a pair of newlines.
                        yield break;
                    }

                    yield return OllamaStreamingChunk.Tool(chunkText);
                }
                else
                {
                    yield return OllamaStreamingChunk.Content(chunkText);
                }
            }
            else
            {
                throw new InvalidOperationException("Invalid response entry from Ollama");
            }
        }

        // If the response is so short we were still capturing leading chunks, yield them now
        if (capturingLeadingChunks is not null)
        {
            yield return OllamaStreamingChunk.Content(capturingLeadingChunks);
        }
    }

    private JsonContent PrepareChatRequestContent(IReadOnlyList<ChatMessage> messages, ChatOptions options, bool streaming, bool allowTools)
    {
        return JsonContent.Create(new
        {
            Model = _modelName,
            Prompt = FormatRawPrompt(messages, allowTools ? options.Tools : null),
            Format = options.ResponseFormat == ChatResponseFormat.JsonObject ? "json" : null,
            Options = new
            {
                Temperature = options.Temperature ?? 0.5,
                NumPredict = options.MaxTokens,
                TopP = options.TopP ?? 1.0,
                Stop = (options.StopSequences ?? Enumerable.Empty<string>()).Concat(["[/TOOL_CALLS]"]),
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
                    if (!string.IsNullOrWhiteSpace(message.Content))
                    {
                        sb.Append(message.Content);
                        sb.Append("</s> "); // That's right, there's no matching <s>. See https://discuss.huggingface.co/t/skew-between-mistral-prompt-in-docs-vs-chat-template/66674/2
                    }
                    if (message.ToolCalls is not null)
                    {
                        // Note that when JSON-serializing here, we don't use any property name conversions
                        // because the "result" property names are defined by the app developer, not us.
                        sb.Append("[TOOL_CALLS] ");
                        sb.Append(JsonSerializer.Serialize(message.ToolCalls.OfType<OllamaChatMessageToolCall>()));
                        sb.Append(" [/TOOL_CALLS]\n\n");
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

        public required ToolDescriptor Tool { get; init; }
        public required Delegate Delegate { get; init; }

        public record ToolDescriptor(string Type, FunctionDescriptor Function);
        public record FunctionDescriptor(string Name, string Description, FunctionParameters Parameters);
        public record FunctionParameters(string Type, Dictionary<string, ParameterDescriptor> Properties, string[] Required);
        public record ParameterDescriptor(string Type, string? Description, string[]? Enum);

        public static OllamaChatFunction Create<T>(string name, string description, T @delegate) where T : Delegate
            => new OllamaChatFunction(name, description)
            {
                Tool = new ToolDescriptor("function", new FunctionDescriptor(name, description, ToFunctionParameters(@delegate))),
                Delegate = @delegate,
            };

        private static FunctionParameters ToFunctionParameters<T>(T @delegate) where T : Delegate
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
        // Use of lowercase names here is because when we serialize this, we need "result" to be serialized with
        // its property names unchanged, as they are defined by the app developer, whereas name/argument/results
        // need to be lowercase to match Mistral's expectations.
        public required string name { get; set; }
        public required Dictionary<string, JsonElement> arguments { get; set; }
        public object? result { get; set; }
    }

    private record struct OllamaStreamingChunk(string? ContentUpdate, string? ToolUpdate)
    {
        public static OllamaStreamingChunk Content(string text) => new OllamaStreamingChunk(text, null);
        public static OllamaStreamingChunk Tool(string text) => new OllamaStreamingChunk(null, text);
    }
}
