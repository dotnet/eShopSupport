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
    public static void UseStandardFunctionExecution<T>(this T chatService)
        where T : ChatService, IChatServiceWithFunctions
    {
        chatService.UseMiddleware((context, cancellationToken, next) =>
            StandardFunctionExecutionMiddleware(chatService, context, cancellationToken, next));
    }

    private static async IAsyncEnumerable<ChatMessageChunk> StandardFunctionExecutionMiddleware(
        IChatServiceWithFunctions chatService,
        ChatContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        Func<ChatContext, CancellationToken, IAsyncEnumerable<ChatMessageChunk>> next)
    {
        const int maxIterations = 3;
        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            var childContext = iteration < maxIterations
                ? context
                : context with
                {
                    Options = context.Options with { Tools = null },
                };

            var toolCalls = new List<ChatToolCall>();
            await foreach (var chunk in next(context, cancellationToken))
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
                    await chatService.ExecuteToolCallAsync(toolCall, context.Options);
                }

                context = context with {
                    Messages = new List<ChatMessage>(context.Messages)
                    {
                        new ChatMessage(ChatMessageRole.Assistant, string.Empty) { ToolCalls = toolCalls },
                    }
                };
            }
            else
            {
                break;
            }
        }
    }
}
