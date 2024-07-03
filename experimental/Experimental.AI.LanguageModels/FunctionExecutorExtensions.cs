using System.Runtime.CompilerServices;

namespace Experimental.AI.LanguageModels;

public static class FunctionExecutorExtensions
{
    public static IChatService WithStandardFunctionExecution(this IChatServiceWithFunctions chatService)
    {
        return new ChatServiceWithFunctions(chatService);
    }

    private class ChatServiceWithFunctions(IChatServiceWithFunctions underlying) : IChatService
    {
        public Task<IReadOnlyList<ChatMessage>> CompleteChatAsync(IReadOnlyList<ChatMessage> messages, ChatOptions options, CancellationToken cancellationToken = default)
            // TOOD: Add tool execution
            => underlying.CompleteChatAsync(messages, options, cancellationToken);

        public async IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingAsync(IReadOnlyList<ChatMessage> messages, ChatOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            const int maxIterations = 3;
            for (var iteration = 1; iteration <= maxIterations; iteration++)
            {
                if (iteration == maxIterations)
                {
                    options = options with { Tools = null };
                }

                var toolCalls = new List<ChatToolCall>();
                await foreach (var chunk in underlying.CompleteChatStreamingAsync(messages, options, cancellationToken))
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
                        await underlying.ExecuteToolCallAsync(toolCall, options);
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

        public ChatFunction CreateChatFunction<T>(string name, string description, T @delegate) where T : Delegate
            => underlying.CreateChatFunction(name, description, @delegate);
    }
}
