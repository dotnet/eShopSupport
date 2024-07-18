using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public OllamaChatCompletionService(HttpClient httpClient, string modelName)
    {
        _httpClient = httpClient;
        _modelName = modelName;
    }

    public IReadOnlyDictionary<string, object?> Attributes => throw new NotImplementedException();

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        // We have to use the "generate" endpoint, not "chat", because function calling requires raw mode
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/generate");
        request.Content = PrepareChatRequestContent(chatHistory, executionSettings, kernel, false, out var json);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var responseText = "ERROR: The configured model isn't available. Perhaps it's still downloading.";
            return [new ChatMessageContent(AuthorRole.Assistant, json ? JsonSerializer.Serialize(responseText) : responseText)];
        }

        var responseContent = await response.Content.ReadFromJsonAsync<OllamaResponseStreamEntry>(_jsonSerializerOptions, cancellationToken);
        return [new ChatMessageContent(AuthorRole.Assistant, responseContent!.Response!)];
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var iterationIndex = 0; iterationIndex < 3; iterationIndex++)
        {
            Exception? exception = null;
            var toolCallBuilder = default(StringBuilder);
            await foreach (var chunk in ProcessStreamingMessagesAsync(chatHistory, executionSettings, kernel, cancellationToken))
            {
                if (chunk.ContentUpdate is { } contentUpdate)
                {
                    yield return new StreamingChatMessageContent(AuthorRole.Assistant, chunk.ContentUpdate);
                }
                else if (chunk.ToolUpdate is { } toolUpdate)
                {
                    toolCallBuilder ??= new();
                    toolCallBuilder.Append(toolUpdate);
                }
            }

            if (toolCallBuilder is { Length: > 0 } && kernel is not null)
            {
                var toolCallsJson = toolCallBuilder!.ToString().Trim();
                try
                {
                    var toolCalls = JsonSerializer.Deserialize<OllamaChatMessageToolCall[]>(toolCallsJson, _jsonSerializerOptions)!;
                    foreach (var toolCall in toolCalls)
                    {
                        if (FindFunction(kernel, toolCall.Name) is { } function)
                        {
                            var args = new KernelArguments();
                            foreach (var param in function.Metadata.Parameters)
                            {
                                var receivedValue = toolCall.Arguments.TryGetValue(param.Name, out var jsonValue) ? jsonValue : default;
                                args.Add(param.Name, MapParameterType(param.ParameterType, receivedValue));
                            }

                            var callResult = await function.InvokeAsync(kernel, args, cancellationToken);
                            var message = new ChatMessageContent(AuthorRole.Tool, JsonSerializer.Serialize(new
                            {
                                name = toolCall.Name,
                                arguments = toolCall.Arguments,
                                result = callResult.GetValue<object>()
                            }));
                            chatHistory.Add(message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            }
            else
            {
                break;
            }

            if (exception is not null)
            {
                // TODO: Log the exception properly with ILogger
                Console.Error.WriteLine(exception.ToString());
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, "Sorry, there was a problem invoking a function.");
                break;
            }
        }
    }

    private KernelFunction? FindFunction(Kernel? kernel, string name)
    {
        foreach (var plugin in kernel?.Plugins ?? [])
        {
            if (plugin.TryGetFunction(name, out var function))
            {
                return function;
            }
        }

        return null;
    }

    private async IAsyncEnumerable<OllamaStreamingChunk> ProcessStreamingMessagesAsync(
        ChatHistory messages,
        PromptExecutionSettings? options,
        Kernel? kernel,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // We have to use the "generate" endpoint, not "chat", because function calling requires raw mode
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/generate");
        request.Content = PrepareChatRequestContent(messages, options, kernel, true, out var json);

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
                    else if (capturingLeadingChunks.TrimStart().StartsWith("[{\"name\":\"") && !json)
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

    private JsonContent PrepareChatRequestContent(ChatHistory messages, PromptExecutionSettings? options, Kernel? kernel, bool streaming, out bool json)
    {
        var openAiOptions = options as OpenAIPromptExecutionSettings;
        json = openAiOptions is { ResponseFormat: "json_object" };
        return JsonContent.Create(new
        {
            Model = _modelName,
            Prompt = FormatRawPrompt(messages, kernel, openAiOptions?.ToolCallBehavior == ToolCallBehavior.AutoInvokeKernelFunctions),
            Format = json ? "json" : null,
            Options = new
            {
                Temperature = openAiOptions?.Temperature ?? 0.5,
                NumPredict = openAiOptions?.MaxTokens,
                TopP = openAiOptions?.TopP ?? 1.0,
                Stop = (openAiOptions?.StopSequences ?? Enumerable.Empty<string>()).Concat(["[/TOOL_CALLS]"]),
            },
            Raw = true,
            Stream = streaming,
        }, options: _jsonSerializerOptions);
    }

    private static string FormatRawPrompt(ChatHistory messages, Kernel? kernel, bool autoInvokeFunctions)
    {
        // TODO: First fetch the prompt template for the model via /api/show, and then use
        // that to format the messages. Currently this is hardcoded to the Mistral prompt,
        // i.e.: [INST] {{ if .System }}{{ .System }} {{ end }}{{ .Prompt }} [/INST]
        var sb = new StringBuilder();
        var indexOfLastUserOrSystemMessage = IndexOfLast(messages, m => m.Role == AuthorRole.User || m.Role == AuthorRole.System);

        // IMPORTANT: The whitespace in the prompt is significant. Do not add or remove extra spaces/linebreaks,
        // as this affects tokenization. Mistral's function calling is useless unless you get this exactly right.

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];

            // Emit tools descriptor immediately before the final [INST]
            if (index == indexOfLastUserOrSystemMessage && autoInvokeFunctions && kernel is not null)
            {
                var tools = kernel.Plugins.SelectMany(p => p.GetFunctionsMetadata()).ToArray() ?? [];
                if (tools is { Length: > 0 })
                {
                    sb.Append("[AVAILABLE_TOOLS] ");
                    sb.Append(JsonSerializer.Serialize(tools.Select(OllamaChatFunction.Create), _jsonSerializerOptions));
                    sb.Append("[/AVAILABLE_TOOLS]");
                }
            }

            if (message.Role == AuthorRole.User || message.Role == AuthorRole.System)
            {
                sb.Append("[INST] ");
                sb.Append(message.Content);
                sb.Append(" [/INST]");
            }
            else if (message.Role == AuthorRole.Tool)
            {
                sb.Append("[TOOL_CALLS] ");
                sb.Append(message.Content);
                sb.Append(" [/TOOL_CALLS]\n\n");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(message.Content))
                {
                    sb.Append(message.Content);
                    sb.Append("</s> "); // That's right, there's no matching <s>. See https://discuss.huggingface.co/t/skew-between-mistral-prompt-in-docs-vs-chat-template/66674/2
                }
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

    private class OllamaResponseStreamEntry
    {
        public bool Done { get; set; }
        public string? Response { get; set; }
    }

    private class OllamaChatMessageToolCall
    {
        public required string Name { get; set; }
        public required Dictionary<string, JsonElement> Arguments { get; set; }
    }

    private record struct OllamaStreamingChunk(string? ContentUpdate, string? ToolUpdate)
    {
        public static OllamaStreamingChunk Content(string text) => new OllamaStreamingChunk(text, null);
        public static OllamaStreamingChunk Tool(string text) => new OllamaStreamingChunk(null, text);
    }

    private static class OllamaChatFunction
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

        public record ToolDescriptor(string Type, FunctionDescriptor Function);
        public record FunctionDescriptor(string Name, string Description, FunctionParameters Parameters);
        public record FunctionParameters(string Type, Dictionary<string, ParameterDescriptor> Properties, string[] Required);
        public record ParameterDescriptor(string Type, string? Description, string[]? Enum);

        public static ToolDescriptor Create(KernelFunctionMetadata metadata)
        {
            return new ToolDescriptor("function", new FunctionDescriptor(metadata.Name, metadata.Description, ToFunctionParameters(metadata)));
        }

        private static FunctionParameters ToFunctionParameters(KernelFunctionMetadata kernelFunction)
        {
            var parameters = kernelFunction.Parameters;
            return new FunctionParameters(
                "object",
                parameters.ToDictionary(p => p.Name!, ToParameterDescriptor),
                parameters.Where(p => p.IsRequired).Select(p => p.Name!).ToArray());
        }

        private static ParameterDescriptor ToParameterDescriptor(KernelParameterMetadata parameterInfo)
            => new ParameterDescriptor(
                ToParameterType(parameterInfo.ParameterType),
                parameterInfo.Description,
                ToEnumValues(parameterInfo?.ParameterType));

        private static string[]? ToEnumValues(Type? type)
            => type is not null && type.IsEnum ? Enum.GetNames(type) : null;

        private static string ToParameterType(Type? parameterType)
        {
            if (parameterType is null)
            {
                return "object";
            }

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

    private static object? MapParameterType(Type? targetType, JsonElement receivedValue)
    {
        if (targetType is null)
        {
            return null;
        }
        else if (targetType == typeof(int) && receivedValue.ValueKind == JsonValueKind.Number)
        {
            return receivedValue.GetInt32();
        }
        else if (targetType == typeof(int?) && receivedValue.ValueKind == JsonValueKind.Number)
        {
            return receivedValue.GetInt32();
        }
        else if (Nullable.GetUnderlyingType(targetType) is not null && (receivedValue.ValueKind == JsonValueKind.Null || receivedValue.ValueKind == JsonValueKind.Undefined))
        {
            return null;
        }
        else if (targetType == typeof(string) && receivedValue.ValueKind == JsonValueKind.String)
        {
            return receivedValue.GetString();
        }

        throw new InvalidOperationException($"JSON value of kind {receivedValue.ValueKind} cannot be converted to {targetType}");
    }
}
