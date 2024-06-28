using System.Data;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Experimental.AI.LanguageModels;

namespace eShopSupport.ServiceDefaults.Clients.ChatCompletion;

public class OpenAIChatService(OpenAIClient client, string deploymentName) : IChatService
{
    public async Task<IReadOnlyList<ChatMessage>> CompleteChatAsync(IReadOnlyList<ChatMessage> messages, ChatOptions options, CancellationToken cancellationToken = default)
    {
        var completionOptions = BuildCompletionOptions(deploymentName, messages, options, allowTools: false);
        var result = await client.GetChatCompletionsAsync(completionOptions, cancellationToken);
        return result.Value.Choices.Select(m => new ChatMessage(MapOpenAIRole(m.Message.Role), m.Message.Content)).ToList();
    }

    public async IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingAsync(IReadOnlyList<ChatMessage> messages, ChatOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const int maxIterations = 3;
        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            var completionOptions = BuildCompletionOptions(deploymentName, messages, options, allowTools: iteration < maxIterations);
            var chunks = await client.GetChatCompletionsStreamingAsync(completionOptions, cancellationToken);
            var contentBuilder = default(StringBuilder);
            var functionToolName = default(string);
            var functionToolArgs = default(StringBuilder);
            var toolCallId  = default(string);
            var finishReason = default(CompletionsFinishReason);

            // Process and capture chunks until the end of the current message
            await foreach (var chunk in chunks)
            {
                if (chunk is { ChoiceIndex: 0, ContentUpdate: { Length: > 0 } })
                {
                    contentBuilder ??= new();
                    contentBuilder.Append(chunk.ContentUpdate);
                    yield return new ChatMessageChunk(chunk.ContentUpdate);
                }
                else if (chunk.ToolCallUpdate is StreamingFunctionToolCallUpdate { ToolCallIndex: 0 } toolCallUpdate)
                {
                    // TODO: Handle parallel tool calls
                    toolCallId ??= toolCallUpdate.Id;
                    functionToolName ??= toolCallUpdate.Name;
                    functionToolArgs ??= new();
                    functionToolArgs.Append(toolCallUpdate.ArgumentsUpdate);
                }

                if (chunk.FinishReason is { } finishReasonValue)
                {
                    finishReason = finishReasonValue;
                }
            }

            // Now decide whether to loop again or just stop here
            if (finishReason == CompletionsFinishReason.ToolCalls && functionToolArgs is not null)
            {
                var argsString = functionToolArgs.ToString();
                var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsString)!;
                var function = options.Tools?.FirstOrDefault(t => t.Name == functionToolName);
                if (function is OpenAIChatFunction openAiFunction)
                {
                    var callResult = await openAiFunction.InvokeAsync(args);
                    var toolCall = new OpenAiFunctionToolCall(new ChatCompletionsFunctionToolCall(toolCallId, functionToolName, argsString));
                    messages = new List<ChatMessage>(messages)
                    {
                        new ChatMessage(ChatMessageRole.Assistant, contentBuilder?.ToString() ?? string.Empty) { ToolCalls = [toolCall] },
                        new ChatMessage(ChatMessageRole.Tool, JsonSerializer.Serialize(callResult)) { ToolCallId = toolCallId },
                    };
                }

                continue;
            }

            break;
        }
    }

    public ChatFunction CreateChatFunction<T>(string name, string description, T @delegate) where T : Delegate
        => OpenAIChatFunction.Create(name, description, @delegate);

    private static ChatCompletionsOptions BuildCompletionOptions(string deploymentName, IReadOnlyList<ChatMessage> messages, ChatOptions options, bool allowTools)
    {
        var result = new ChatCompletionsOptions(deploymentName, messages.Select(ToChatRequestMessage))
        {
            ResponseFormat = options.ResponseFormat switch
            {
                ChatResponseFormat.Text => ChatCompletionsResponseFormat.Text,
                ChatResponseFormat.JsonObject => ChatCompletionsResponseFormat.JsonObject,
                _ => default
            },
            Temperature = options.Temperature,
            Seed = options.Seed,
        };

        if (allowTools && options.Tools is not null)
        {
            foreach (var tool in options.Tools)
            {
                if (tool is OpenAIChatFunction { OpenAIDefinition: var definition })
                {
                    result.Tools.Add(definition);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported tool type {tool.GetType()}");
                }
            }
        }

        return result;
    }

    private ChatMessageRole MapOpenAIRole(ChatRole role)
    {
        if (role == ChatRole.User)
        {
            return ChatMessageRole.User;
        }
        else if (role == ChatRole.Assistant)
        {
            return ChatMessageRole.Assistant;
        }
        else if (role == ChatRole.System)
        {
            return ChatMessageRole.System;
        }
        else if (role == ChatRole.Tool)
        {
            return ChatMessageRole.Tool;
        }
        else
        {
            throw new NotSupportedException($"Unknown message role: {role}");
        }
    }

    private static ChatRequestMessage ToChatRequestMessage(ChatMessage message)
    {
        ChatRequestMessage result = message switch
        {
            { Role: ChatMessageRole.User } => new ChatRequestUserMessage(message.Content),
            { Role: ChatMessageRole.Assistant } => new ChatRequestAssistantMessage(message.Content),
            { Role: ChatMessageRole.System } => new ChatRequestSystemMessage(message.Content),
            { Role: ChatMessageRole.Tool } => new ChatRequestToolMessage(message.Content, message.ToolCallId),
            _ => throw new NotSupportedException($"Unknown message role '{message.Role}'")
        };

        if (message.ToolCalls is not null && result is ChatRequestAssistantMessage { } assistantMessage)
        {
            foreach (var toolCall in message.ToolCalls.Cast<OpenAiFunctionToolCall>())
            {
                assistantMessage.ToolCalls.Add(toolCall.Value);
            }
        }

        return result;
    }

    private class OpenAiFunctionToolCall(ChatCompletionsToolCall value) : ChatMessageToolCall
    {
        public ChatCompletionsToolCall Value => value;
    }

    private class OpenAIChatFunction : ChatFunction
    {
        private readonly Delegate _delegate;
        public ChatCompletionsFunctionToolDefinition OpenAIDefinition { get; }

        private OpenAIChatFunction(string name, string description, Delegate @delegate, ChatCompletionsFunctionToolDefinition definition) : base(name, description)
        {
            _delegate = @delegate;
            OpenAIDefinition = definition;
        }

        public static OpenAIChatFunction Create<T>(string name, string description, T @delegate) where T: Delegate
        {
            // Use reflection for now, but could use a source generator
            var definition = new FunctionDefinition(name);
            definition.Description = description;
            definition.Parameters = BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new
                {
                    productId = new
                    {
                        type = "number",
                        description = "ID for the product whose manual to search",
                    },
                    searchPhrase = new
                    {
                        type = "string",
                        description = "A phrase to use when searching the manual",
                    },
                },
                required = new string[] { "productId", "searchPhrase" },
            });

            return new OpenAIChatFunction(name, description, @delegate, new ChatCompletionsFunctionToolDefinition(definition));
        }

        public async Task<object> InvokeAsync(Dictionary<string, JsonElement> args)
        {
            // TODO: So much error handling
            var parameters = _delegate.Method.GetParameters();
            var argsInOrder = parameters.Select(p => MapParameterType(p.ParameterType, args[p.Name!]));
            var result = _delegate.DynamicInvoke(argsInOrder.ToArray());
            if (result is Task task)
            {
                await task;
                var resultProperty = task.GetType().GetProperty("Result");
                return resultProperty?.GetValue(task)!;
            }
            else
            {
                return Task.FromResult(result);
            }
        }

        private object? MapParameterType(Type targetType, JsonElement receivedValue)
        {
            if (targetType == typeof(int) && receivedValue.ValueKind == JsonValueKind.Number)
            {
                return receivedValue.GetInt32();
            }
            else if (targetType == typeof(string) && receivedValue.ValueKind == JsonValueKind.String)
            {
                return receivedValue.GetString();
            }

            throw new InvalidOperationException($"JSON value of kind {receivedValue.ValueKind} cannot be converted to {targetType}");
        }
    }
}
