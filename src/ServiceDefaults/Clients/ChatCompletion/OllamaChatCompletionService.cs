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

        /*
        if (toolCallBuilder is { Length: > 0 })
        {
            var toolCallsJson = toolCallBuilder!.ToString().Trim();
            var toolCalls = JsonSerializer.Deserialize<OllamaChatMessageToolCall[]>(toolCallsJson, _jsonSerializerOptions)!;
            foreach (var toolCall in toolCalls)
            {
                yield return new ChatMessageChunk(ChatMessageRole.Assistant, $"[Tool call: {toolCall.Name}]", toolCall);
            }
        }
        */
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
            Prompt = FormatRawPrompt(messages, kernel?.Plugins),
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

    private static string FormatRawPrompt(ChatHistory messages, IEnumerable<KernelPlugin>? plugins)
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
            if (index == indexOfLastUserOrSystemMessage)
            {
                /*
                if (tools is not null)
                {
                    sb.Append("[AVAILABLE_TOOLS] ");
                    sb.Append(JsonSerializer.Serialize(tools.OfType<OllamaChatFunction>().Select(t => t.Tool), _jsonSerializerOptions));
                    sb.Append("[/AVAILABLE_TOOLS]");
                }
                */
            }

            if (message.Role == AuthorRole.User || message.Role == AuthorRole.System)
            {
                sb.Append("[INST] ");
                sb.Append(message.Content);
                sb.Append(" [/INST]");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(message.Content))
                {
                    sb.Append(message.Content);
                    sb.Append("</s> "); // That's right, there's no matching <s>. See https://discuss.huggingface.co/t/skew-between-mistral-prompt-in-docs-vs-chat-template/66674/2
                }
                /*
                if (message.ToolCalls is not null)
                {
                    // Note that when JSON-serializing here, we don't use any property name conversions
                    // because the "result" property names are defined by the app developer, not us.
                    sb.Append("[TOOL_CALLS] ");
                    sb.Append(JsonSerializer.Serialize(message.ToolCalls.OfType<OllamaChatMessageToolCall>().Select(call => new
                    {
                        name = call.Name,
                        arguments = call.Arguments,
                        result = call.Result,
                    })));
                    sb.Append(" [/TOOL_CALLS]\n\n");
                }
                */
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

    private record struct OllamaStreamingChunk(string? ContentUpdate, string? ToolUpdate)
    {
        public static OllamaStreamingChunk Content(string text) => new OllamaStreamingChunk(text, null);
        public static OllamaStreamingChunk Tool(string text) => new OllamaStreamingChunk(null, text);
    }
}
