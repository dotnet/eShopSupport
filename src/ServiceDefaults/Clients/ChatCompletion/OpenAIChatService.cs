using System.ComponentModel;
using System.Data;
using System.Reflection;
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
            var allowTools = iteration < maxIterations;
            var toolCalls = new List<ChatToolCall>();
            await foreach (var chunk in CompleteChatStreamingCoreAsync(messages, options, allowTools, cancellationToken))
            {
                if (chunk.ToolCall is { } toolCall)
                {
                    toolCalls.Add(toolCall);
                }
                else if (chunk.Content is not null)
                {
                    yield return chunk;
                }
            }

            if (toolCalls.Any())
            {
                foreach (var toolCall in toolCalls)
                {
                    await ExecuteToolCallAsync(toolCall, options);
                }

                messages = new List<ChatMessage>(messages)
                {
                    new ChatMessage(ChatMessageRole.Assistant, string.Empty) { ToolCalls = toolCalls },
                };
            }
            else
            {
                break;
            }
        }
    }

    public async Task ExecuteToolCallAsync(ChatToolCall toolCall, ChatOptions options)
    {
        var openAiChatToolCall = (OpenAiFunctionToolCall)toolCall;
        var functionToolCall = (ChatCompletionsFunctionToolCall)openAiChatToolCall.Value;
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(functionToolCall.Arguments)!;
        var function = options.Tools?.FirstOrDefault(t => t.Name == functionToolCall.Name);
        if (function is OpenAIChatFunction openAiFunction)
        {
            toolCall.Result = await ReflectionChatFunction.InvokeAsync(openAiFunction.Delegate, args);
        }
    }

    // Just invokes the backend. Doesn't invoke tools.
    private async IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingCoreAsync(IReadOnlyList<ChatMessage> messages, ChatOptions options, bool allowTools, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var completionOptions = BuildCompletionOptions(deploymentName, messages, options, allowTools);
        var chunks = await client.GetChatCompletionsStreamingAsync(completionOptions, cancellationToken);
        var contentBuilder = default(StringBuilder);
        var functionToolName = default(string);
        var functionToolArgs = default(StringBuilder);
        var toolCallId = default(string);
        var finishReason = default(CompletionsFinishReason);

        // Process and capture chunks until the end of the current message
        await foreach (var chunk in chunks)
        {
            if (chunk is { ChoiceIndex: 0, ContentUpdate: { Length: > 0 } })
            {
                contentBuilder ??= new();
                contentBuilder.Append(chunk.ContentUpdate);
                yield return new ChatMessageChunk(ChatMessageRole.Assistant, chunk.ContentUpdate, null);
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

        // Emit any tool calls
        if (finishReason == CompletionsFinishReason.ToolCalls && functionToolArgs is not null)
        {
            var argsString = functionToolArgs.ToString();
            var toolCall = new OpenAiFunctionToolCall(
                new ChatCompletionsFunctionToolCall(toolCallId, functionToolName, argsString));
            yield return new ChatMessageChunk(ChatMessageRole.Assistant, null, toolCall);
        }
    }

    public ChatFunction CreateChatFunction<T>(string name, string description, T @delegate) where T : Delegate
        => OpenAIChatFunction.Create(name, description, @delegate);

    private static ChatCompletionsOptions BuildCompletionOptions(string deploymentName, IReadOnlyList<ChatMessage> messages, ChatOptions options, bool allowTools)
    {
        var result = new ChatCompletionsOptions(deploymentName, messages.SelectMany(ToChatRequestMessages))
        {
            ResponseFormat = options.ResponseFormat switch
            {
                ChatResponseFormat.Text => ChatCompletionsResponseFormat.Text,
                ChatResponseFormat.JsonObject => ChatCompletionsResponseFormat.JsonObject,
                _ => default
            },
            Temperature = (float?)options.Temperature,
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
        else
        {
            throw new NotSupportedException($"Unknown message role: {role}");
        }
    }

    private static IEnumerable<ChatRequestMessage> ToChatRequestMessages(ChatMessage message)
    {
        ChatRequestMessage result = message switch
        {
            { Role: ChatMessageRole.User } => new ChatRequestUserMessage(message.Content),
            { Role: ChatMessageRole.Assistant } => new ChatRequestAssistantMessage(message.Content),
            { Role: ChatMessageRole.System } => new ChatRequestSystemMessage(message.Content),
            _ => throw new NotSupportedException($"Unknown message role '{message.Role}'")
        };

        if (message.ToolCalls is not null && result is ChatRequestAssistantMessage { } assistantMessage)
        {
            foreach (var toolCall in message.ToolCalls.Cast<OpenAiFunctionToolCall>())
            {
                assistantMessage.ToolCalls.Add(toolCall.Value);
            }

            yield return assistantMessage;
            foreach (var toolCall in message.ToolCalls.Cast<OpenAiFunctionToolCall>())
            {
                yield return new ChatRequestToolMessage(JsonSerializer.Serialize(toolCall.Result), toolCall.Value.Id);
            }
        }
        else
        {
            yield return result;
        }
    }

    private class OpenAiFunctionToolCall(ChatCompletionsToolCall value) : ChatToolCall
    {
        public ChatCompletionsToolCall Value => value;
    }

    private class OpenAIChatFunction : ChatFunction
    {
        public Delegate Delegate { get; }
        public ChatCompletionsFunctionToolDefinition OpenAIDefinition { get; }

        private OpenAIChatFunction(string name, string description, Delegate @delegate, ChatCompletionsFunctionToolDefinition definition) : base(name, description)
        {
            Delegate = @delegate;
            OpenAIDefinition = definition;
        }

        public static OpenAIChatFunction Create<T>(string name, string description, T @delegate) where T : Delegate
        {
            // Use reflection for now, but could use a source generator
            var definition = new FunctionDefinition(name);
            definition.Description = description;

            var delegateParameters = @delegate.Method.GetParameters();

            definition.Parameters = BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = delegateParameters.ToDictionary(p => p.Name!, p => ToParameterSchema(p)),
                required = delegateParameters.Where(p => !p.IsOptional).Select(p => p.Name!),
            });

            return new OpenAIChatFunction(name, description, @delegate, new ChatCompletionsFunctionToolDefinition(definition));
        }

        private static object ToParameterSchema(ParameterInfo parameter) => new
        {
            type = ToParameterType(parameter.ParameterType),
            description = GetParameterDescription(parameter),
        };

        private static string? GetParameterDescription(ParameterInfo parameter)
            => parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;

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
}
