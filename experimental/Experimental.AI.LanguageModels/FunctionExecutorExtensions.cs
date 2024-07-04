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

// This middleware concept is fine but does point to value in splitting the backend "handler" from the frontend
// consumer API. The backend handler could be a pure interface to make it easier to retrofit onto existing
// chat service implementations. Then instead of middleware, the frontend could simply allow you to replace
// its handler with another that wraps the original. To retain the ability for app developers to upcast the
// instance to an implementation-specific type, the frontend type could be a subclass that sets up the default
// handler and optionally exposes any custom APIs around it.
// Swapping the handler is better than middleware because then you can wrap *anything* on the handler (e.g.,
// function call execution), not just specific chat completion methods. Hence it's usable if you want to attach
// logging of both completion inputs/outputs and function calls.

// Possible naming:
//  - IChatCompletionHandler (analogous to HttpMessageHandler)
//    - Can be exactly equivalent to IChatCompletionService
//    - With IChatFunctionsHandler being a further optional interface you can implement
//      and adds DefineChatFunction and ExecuteChatFunctionAsync methods
//  - ChatClient (analogous to HttpClient)
//    - This could hold ChatOptions as a property so at DI level you can preconfigure it,
//      though that means you can't change it per-request (e.g., in an extension method that
//      accepts an SK kernel and attaches its plugins as functions)

public static class FunctionExecutorExtensions
{
    public static void UseStandardFunctionExecution(this ChatClient chatService)
    {
        chatService.Handler = new StandardFunctionExecutionHandler(chatService.Handler);
    }

    private class StandardFunctionExecutionHandler(ChatCompletionHandler innerHandler)
        : ChatCompletionHandler(innerHandler)
    {
        public override async IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            const int maxIterations = 3;
            for (var iteration = 1; iteration <= maxIterations; iteration++)
            {
                var childOptions = iteration < maxIterations
                    ? options
                    : options with { Tools = null };

                var toolCalls = new List<ChatToolCall>();
                await foreach (var chunk in InnerHandler!.CompleteChatStreamingAsync(messages, childOptions, cancellationToken))
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
                        await InnerHandler!.ExecuteChatFunctionAsync(toolCall, options);
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
    }
}
