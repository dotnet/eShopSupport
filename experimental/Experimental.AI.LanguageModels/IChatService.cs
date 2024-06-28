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
    // and any other backend-specific rules about how to represent the tool call in the chat history (e.g., with IDs).
    // If they want to let the developer customize the rules (e.g., how many calls are allowed) they have to do that in their
    // own concrete API, e.g., via an "options" passed to the constructor.
    //
    // This also means we don't have a single place from which we can trigger before/after call filters or logging. Each
    // IChatService implementation either does that itself or doesn't do it at all. Arguably that should be enough because it
    // follows the same pattern as other Aspire components, in that each of them is expected to do its own telemetry etc.
    // But is that enough? What are the use cases for function invocation filters? If there are clear reasons why someone consuming
    // an IChatService would need to attach them regardless of the backend, we need to either:
    //  - Extend the IChatService interface to support them
    //  - Or, define some further interface like IChatFunctionFilters that the concrete type can implement if it wants to participate in this
    ChatFunction CreateChatFunction<T>(string name, string description, T @delegate) where T : Delegate;
}
