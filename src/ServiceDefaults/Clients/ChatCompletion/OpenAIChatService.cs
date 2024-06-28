using System.Data;
using System.Runtime.CompilerServices;
using Azure.AI.OpenAI;
using Experimental.AI.LanguageModels;

namespace eShopSupport.ServiceDefaults.Clients.ChatCompletion;

public class OpenAIChatService(OpenAIClient client, string deploymentName) : IChatService
{
    public async Task<IReadOnlyList<ChatMessage>> CompleteChatAsync(IReadOnlyList<ChatMessage> messages, ChatOptions options, CancellationToken cancellationToken = default)
    {
        var completionOptions = BuildCompletionOptions(deploymentName, messages, options);
        var result = await client.GetChatCompletionsAsync(completionOptions, cancellationToken);
        return result.Value.Choices.Select(m => new ChatMessage(MapOpenAIRole(m.Message.Role), m.Message.Content)).ToList();
    }

    public async IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingAsync(IReadOnlyList<ChatMessage> messages, ChatOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var completionOptions = BuildCompletionOptions(deploymentName, messages, options);
        var chunks = await client.GetChatCompletionsStreamingAsync(completionOptions, cancellationToken);
        await foreach (var chunk in chunks)
        {
            if (chunk is { ChoiceIndex: 0, ContentUpdate: { Length: > 0 } })
            {
                yield return new ChatMessageChunk(chunk.ContentUpdate);
            }
        }
    }

    public ChatFunction CreateChatFunction<T>(string name, string description, T @delegate) where T : Delegate
    {
        throw new NotImplementedException();
    }

    private static ChatCompletionsOptions BuildCompletionOptions(string deploymentName, IReadOnlyList<ChatMessage> messages, ChatOptions options)
    {
        return new ChatCompletionsOptions(deploymentName, messages.Select(ToChatRequestMessage))
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
    

    private static ChatRequestMessage ToChatRequestMessage(ChatMessage message) => message switch
    {
        { Role: ChatMessageRole.User } => new ChatRequestUserMessage(message.Content),
        { Role: ChatMessageRole.Assistant } => new ChatRequestAssistantMessage(message.Content),
        { Role: ChatMessageRole.System } => new ChatRequestSystemMessage(message.Content),
        { Role: ChatMessageRole.Tool } => new ChatRequestToolMessage(message.Content, message.ToolCallId),
        _ => throw new NotSupportedException($"Unknown message role '{message.Role}'")
    };
}
