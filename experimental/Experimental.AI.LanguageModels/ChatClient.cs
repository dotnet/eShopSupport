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
public abstract class ChatClient
{
    // This approach of setting the handler in the constructor and being readonly thereafter is just
    // like HttpClient, and it's good that it distinguishes the "building" phase from the "usage" phase,
    // keeping it immutable once built.
    // However it doesn't comply with the goal of having subclasses of ChatClient that people can just
    // instantiate directly without knowing anything about handlers. Ideally you'd have things like
    // "new OpenAiChatClient()" that sets up a correspondingly-typed handler. But then who decides which
    // kind of function execution logic to attach to it? We want a sensible function execution mechanism
    // by default, but still allow SK or others to swap it for their own implementation.
    //
    // If your goal is to enable this pattern:
    //
    // var chatClient = new OpenAiChatClient(...);
    // var result = await chatClient.ChatAsync(messages, options, kernel, cancellationToken);
    // (extension method provided by SK that calls in the context of the supplied kernel)
    //
    // ... then you need to be able to attach the function execution logic on a per-call basis, most likely
    // as a property on ChatOptions (so the above extension can use "options with { ... }").
    // Technically it would suffice to expose Handler as a gettable property on ChatClient and a get/set
    // property on ChatOptions (and if left as null by default, uses the Handler from the ChatClient).
    //
    // Actually, is any of this even needed? SK's extension method could already wrap everything in its
    // own function-calling logic. It just needs a way to suppress the default function execution logic.
    // You still need to be able to suppress that even if could wrap the handler on a per-call basis.
    //
    // What if ChatClient was abstract and had a mutable DefaultOptions property?
    // - We want the subclass to be able to set up default function execution logic for itself
    // - But we want to be able to override that on a per-completion-call basis
    // - ... and we need this to be hard to get wrong. For example when defining "default function execution logic",
    //   we do not want the service author to just hardcode something specific to their backend without implementing
    //   ExecuteChatFunctionAsync properly, because then you wouldn't be able to swap out that logic.
    // It would be OK to have FunctionExecutor property that people can default to "new StandardFunctionExecutor()"
    // as it will save them time to do this properly instead of hardcode their own thing. In fact ChatOptions could
    // globally default to "FunctionExecutor = StandardFunctionExecutor.Instance" so in docs we don't even say you
    // have to think about that - you just implement DefineFunction and ExecuteFunctionAsync.
    // But this can't be a middleware system in general as it wouldn't be correct to replace *or* wrap middleware
    // in general (replacing is losing functionality, wrapping would duplicate the function execution logic).
    // So rethink this without middleware and just as a FunctionExecutor concept on ChatOptions.
    //
    // A deeper problem though: what even makes it legitimate for SK to override the function calling logic
    // inside a particular chat service implementation? Can't a service implementation have additional requirements
    // around function calling that SK doesn't know about? The only reason that's not a problem today is that all
    // the concrete service implementations are built for and by SK. It simply doesn't make sense if you think the
    // service implementations work independently of SK.
    // - So I think you need to re-examine what SK is doing in its function calling implementations and to what
    //   extent it's what a service implementation would just naturally do anyway.
    // - Also look at what something else like Rystem.OpenAI does. Is it equivalent?
    //   - I did look into this and what Rystem.OpenAI seems to do is hardcode logic that it follows up on each
    //     tool call by invoking it, then making exactly one further chat completion request (which does not actually
    //     appear to be recursive but I might be misreading it).
    //   - So it's very much not equivalent but also doesn't really look right in general.
    //   - https://github.com/KeyserDSoze/Rystem.OpenAi/blob/8a2fa5ed9da4f0e6754f5565fa4a4304a0741f65/src/Rystem.OpenAi.Api/Endpoints/Chat/Builder/ChatRequestBuilder.cs#L59
    //
    // I think there's a bit of a fundamental mismatch here then:
    // 1. There could be backend-specific requirements about how function execution should be structured
    // 2. People (or SK) may want to implement their own function execution logic that works with all backends
    // The resolutions I can think of are:
    // * Like SK today, hardcode the execution flow separately in each backend, in such a way that it does precisely
    //   what SK (and nothing else) wants it to do
    // * Or, assert that no, there can't be backend-specific requirements around function execution, and it's
    //   the job of each backend to:
    //   - Have CompleteChatAsync/etc translate the LLM output into a series of ChatMessage instances that represent
    //     desired tool calls
    //   - Have CompleteChatAsync/etc translate the supplied "messages" list (including tool calls with results)
    //     into whatever format the LLM expects, which may be multiple "messages" per tool call in the case of OpenAI
    // TBH I think the latter is the only way it will be viable to have a common abstraction and implementations that
    // can work with SK as well as other frontends.
    //
    // So if that is the case, go back to your idea above about having FunctionCallResolver as a property on ChatOptions.

    public Task<IReadOnlyList<ChatMessage>> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default)
        => options.FunctionCallOrchestrator.ChatAsync(this, messages, options, cancellationToken);

    public IAsyncEnumerable<ChatMessageChunk> ChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default)
        => options.FunctionCallOrchestrator.ChatStreamingAsync(this, messages, options, cancellationToken);

    public abstract ChatFunction DefineChatFunction<T>(string name, string description, T @delegate) where T : Delegate;

    // TODO: Obviously these should not be public. Maybe I have to define a handler concept.
    public abstract Task<IReadOnlyList<ChatMessage>> CompleteChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default);

    public abstract IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default);

    public abstract Task ExecuteChatFunctionAsync(ChatToolCall toolCall, ChatOptions options);

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
