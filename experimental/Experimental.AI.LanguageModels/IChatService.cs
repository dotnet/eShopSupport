namespace Experimental.AI.LanguageModels;

public interface IChatService
{
    Task<IReadOnlyList<ChatMessage>> CompleteChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default);

    // This saves us from having to define a schema for functions (which could involve arbitrarily deep parameter types).
    // OpenAI uses JSON schemas; Gemini uses OpenAPI schemas.
    //
    // We want there to be a common API by which any IChatService consumer can attach functions, but we don't want to
    // model functions themselves or determine how .NET delegates map to them. So, we let each IChatService implementation
    // have its own way to represent functions internally and decide how they map to .NET delegates and parameter types.
    // We don't keep track of what functions exist either: it's up to the developer to attach whatever functions they want
    // on a per-call basis.
    // The following is the common API by which the consumer can construct a ChatFunction that makes sense to this IChatService.
    // The IChatService implementation can use either reflection or a source generator to map the .NET delegate to its own schema.
    //
    // Similarly, the core abstraction doesn't force any particular algorithm for calling functions. It's up to the IChatService
    // implementation to have its own tool-execution logic based on how the underlying LLM indicates its tool-calling intent,
    // and any other backend-specific rules about how to represent the tool call in the chat history (e.g., with IDs, how call
    // results should be formatted, and so on).
    // If they want to let the developer customize the rules (e.g., how many calls are allowed) they have to do that in their
    // own concrete API, e.g., via an "options" passed to the constructor.
    //
    // This also means we don't have a single place from which we can trigger before/after call filters or logging. Each
    // IChatService implementation either does that itself or doesn't do it at all. Arguably that should be enough because it
    // follows the same pattern as other Aspire components, in that each of them is expected to do its own telemetry etc.
    // To retain SK's functionality around filters, it can either:
    // - ... be the implementor of IChatService, and hence entirely control how functions are called
    //       (optionally as a wrapper around some other IChatService backend that gets passed in)
    // - ... or, to work with an arbitrary IChatService supplied from outside, have some Kernel method like
    //   kernel.GetChatFunctions(chatService) that works by calling chatService.CreateChatFunction for each function
    //   to get a version that has the filters on the inside, plus they could be pre-attached to other SK kernel facilities.
    // 
    // This latter approach is almost identical to what SK already does today (when calling IChatCompletionService.GetCompletionAsync,
    // you optionally pass in a "kernel" parameter - this would just change to passing in the kernelFunctions object that is returned).
    ChatFunction CreateChatFunction<T>(string name, string description, T @delegate) where T : Delegate;
}
