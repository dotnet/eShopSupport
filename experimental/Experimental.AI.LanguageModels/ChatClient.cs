namespace Experimental.AI.LanguageModels;

// Main principles:
// - Statelessness. While it's initially nice to think about having a single ChatContext that tracks
//   the message history, options, attached functions, etc., this makes it hard/impossible for people
//   to use the technique of wrapping one IChatService in another, because the developer writing the
//   wrapper needs to know about all possible mutations that happen on storage in the outer layer, and
//   somehow reflect them to the inner layer. Hence IChatService being an interface not a base class.
// - Prior art.
//   - HttpClient/HttpClientFactory/HttpMessageHandler
//     is a good example of a way to preconfigure default clients at the DI layer while being able to
//     further configure them at the consumption site, plus have a fairly small API surface for backend
//     implementors. If IChatService was going to be stateful (including having some kind of registry
//     of middleware/filters), it would make sense to copy this pattern.
//     However, we can't afford to be anywhere near as opinionated as HttpClient because what we're
//     modelling is nowhere near as mature and standardized as HTTP requests (HTTP is actually a true
//     spec and as such the backends vary very little in features). As much as possible, IChatService
//     needs to avoid having baked-in logic for anything.
//   - IDistributedCache
//     is more similar to IChatService in that it abstracts over a true variety of backends and has to
//     be unopinionated. 
//   - ILogger/ILoggerFactory/ILoggerProvider

// You might even want to have an IChatService interface this implements, and not have middleware be on
// the interface. Then you have a distinction between building/configuring and consuming.
public class ChatClient
{
    public ChatClient(ChatCompletionHandler handler)
    {
        Handler = handler;
    }

    private ChatCompletionHandler Handler { get; }

    public Task<IReadOnlyList<ChatMessage>> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default)
        => Handler.CompleteChatAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatMessageChunk> ChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default)
        => Handler.CompleteChatStreamingAsync(messages, options, cancellationToken);

    public ChatFunction DefineChatFunction<T>(string name, string description, T @delegate) where T : Delegate
        => Handler.DefineChatFunction(name, description, @delegate);

    // We could define a model for middleware or pre/post function filters, but it's unclear we should bake that in
    // to the core abstraction. What would be the use cases? Wanting to control that through IChatService implies
    // doing so on arbitrary backends. But use cases around logging/telemetry would are more typically handled inside
    // each concrete implementation, since they actually know what they are doing, and the backend-specific logic
    // for function calling. And so that can be configured at the DI level when registering the implementation.
    // If it's for SK to do anything pre/post function calls, it can do that in the way it maps its Kernel functions
    // into ChatFunctions.
    // Commonly, e.g., in IDistributedCache or Aspire components, we expect cross-cutting concerns like logging to
    // be handled in each concrete implementation, not as extra API surface on the core abstraction.
    // If we do want to add a middleware concept to the core IChatService, we can do so but then we also need some kind
    // of factory you can control at the DI level to add middleware globally. And then we need universal logic for
    // how the middleware gets called. It's doable but forces quite a bit more opinionation.

    // ---

    // The following saves us from having to define a schema for functions (which could involve arbitrarily deep parameter types).
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
}
