namespace Experimental.AI.LanguageModels;

public abstract class ChatMiddleware
{
    public virtual Task<IReadOnlyList<ChatMessage>> CompleteChatAsync(
        IChatMiddlewareCallable next,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken)
        => next.CompleteChatAsync(messages, options, cancellationToken);

    public virtual IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingAsync(
        IChatMiddlewareCallable next,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken)
        => next.CompleteChatStreamingAsync(messages, options, cancellationToken);

    public virtual ChatFunction DefineChatFunction<T>(IChatMiddlewareCallable next, string name, string description, T @delegate) where T : Delegate
        => next.DefineChatFunction(name, description, @delegate);

    public virtual Task ExecuteChatFunctionAsync(IChatMiddlewareCallable next, ChatToolCall toolCall, ChatOptions options)
        => next.ExecuteChatFunctionAsync(toolCall, options);
}

public interface IChatMiddlewareCallable
{
    Task<IReadOnlyList<ChatMessage>> CompleteChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken);

    ChatFunction DefineChatFunction<T>(string name, string description, T @delegate) where T : Delegate;

    Task ExecuteChatFunctionAsync(ChatToolCall toolCall, ChatOptions options);
}

public class ChatMiddlewareBuilder(IChatMiddlewareCallable innerMiddleware)
{
    IChatMiddlewareCallable outer = innerMiddleware;

    public IChatMiddlewareCallable Build() => outer;

    public void Use(ChatMiddleware middleware)
    {
        outer = Wrap(middleware, outer);
    }

    private IChatMiddlewareCallable Wrap(ChatMiddleware middleware, IChatMiddlewareCallable next)
        => new WrappedMiddleware(middleware, next);

    private class WrappedMiddleware(ChatMiddleware middleware, IChatMiddlewareCallable next) : IChatMiddlewareCallable
    {
        public Task<IReadOnlyList<ChatMessage>> CompleteChatAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatOptions options,
            CancellationToken cancellationToken)
            => middleware.CompleteChatAsync(next, messages, options, cancellationToken);

        public IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatOptions options,
            CancellationToken cancellationToken)
            => middleware.CompleteChatStreamingAsync(next, messages, options, cancellationToken);

        public ChatFunction DefineChatFunction<T>(string name, string description, T @delegate) where T : Delegate
            => middleware.DefineChatFunction(next, name, description, @delegate);

        public Task ExecuteChatFunctionAsync(ChatToolCall toolCall, ChatOptions options)
            => middleware.ExecuteChatFunctionAsync(next, toolCall, options);
    }
}

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
public abstract class ChatService : IChatMiddlewareCallable
{
    private IChatMiddlewareCallable _middleware;

    public ChatService(Action<ChatMiddlewareBuilder> builder)
    {
        var middlewareBuilder = new ChatMiddlewareBuilder(this);
        builder(middlewareBuilder);
        _middleware = middlewareBuilder.Build();
    }

    public Task<IReadOnlyList<ChatMessage>> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default)
    {
        return _middleware.CompleteChatAsync(messages, options, cancellationToken);
    }

    public IAsyncEnumerable<ChatMessageChunk> ChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default)
        => _middleware.CompleteChatStreamingAsync(messages, options, cancellationToken);

    public ChatFunction DefineChatFunction<T>(string name, string description, T @delegate) where T : Delegate
        => _middleware.DefineChatFunction(name, description, @delegate);

    protected abstract Task<IReadOnlyList<ChatMessage>> CompleteChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken);

    protected abstract IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken);

    protected abstract ChatFunction DefineChatFunctionCore<T>(string name, string description, T @delegate) where T: Delegate;

    protected abstract Task ExecuteChatFunctionAsync(ChatToolCall toolCall, ChatOptions options);

    Task<IReadOnlyList<ChatMessage>> IChatMiddlewareCallable.CompleteChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken)
        => CompleteChatAsync(messages, options, cancellationToken);

    IAsyncEnumerable<ChatMessageChunk> IChatMiddlewareCallable.CompleteChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken)
        => CompleteChatStreamingAsync(messages, options, cancellationToken);

    ChatFunction IChatMiddlewareCallable.DefineChatFunction<T>(string name, string description, T @delegate)
        => DefineChatFunctionCore(name, description, @delegate);

    Task IChatMiddlewareCallable.ExecuteChatFunctionAsync(ChatToolCall toolCall, ChatOptions options)
        => ExecuteChatFunctionAsync(toolCall, options);
}
