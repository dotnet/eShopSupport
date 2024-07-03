using System.Runtime.CompilerServices;

namespace Experimental.AI.LanguageModels;

// Generally I like the concept of function execution being external to IChatService, and being shared
// across backend implementations. It should be possible for SK to layer its own rules around function
// calling, logging, etc., and be better than how it works today (it duplicates the code for each backend).
//
// What I'm less pleased with is the mechanism of actually wrapping IChatService. Doing that takes away
// the option for app developers to upcast to the concrete type to access additional features/options.
// Another way to do it would be a middleware pattern. We'd have to change IChatService to be an abstract
// base class so it can hold the logic for invoking middleware before/after invoking its own protected method
// that calls the external service. Not sure on the overall usability of that. Or instead of the ABC, you
// could have each concrete type accept "middleware" as a ctor param and be expected to call it, though that
// impacts application code and is weird.

public static class FunctionExecutorExtensions
{
    public static ChatService WithStandardFunctionExecution<T>(this T chatService)
        where T : ChatService, IChatServiceWithFunctions
    {
        return new ChatServiceWithFunctions<T>(chatService);
    }

    private class ChatServiceWithFunctions<TChatService>(TChatService underlying) : ChatService where TChatService : ChatService, IChatServiceWithFunctions
    {
        public override Task<IReadOnlyList<ChatMessage>> CompleteChatAsync(IReadOnlyList<ChatMessage> messages, ChatOptions options, CancellationToken cancellationToken = default)
            // TOOD: Add tool execution
            => underlying.CompleteChatAsync(messages, options, cancellationToken);

        public override async IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingAsync(IReadOnlyList<ChatMessage> messages, ChatOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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

        public override ChatFunction DefineChatFunction<T>(string name, string description, T @delegate)
            => underlying.DefineChatFunction(name, description, @delegate);
    }
}
