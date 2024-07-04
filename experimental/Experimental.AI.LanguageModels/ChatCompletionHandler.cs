namespace Experimental.AI.LanguageModels;

public abstract class ChatCompletionHandler(ChatCompletionHandler? innerHandler = null)
{
    protected ChatCompletionHandler? InnerHandler => innerHandler;

    public virtual Task<IReadOnlyList<ChatMessage>> CompleteChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default)
        => innerHandler is null
        ? throw new NotSupportedException($"{GetType()} does not support {nameof(CompleteChatAsync)}, and no inner handler is defined.")
        : innerHandler.CompleteChatAsync(messages, options, cancellationToken);

    public virtual IAsyncEnumerable<ChatMessageChunk> CompleteChatStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken = default)
        => innerHandler is null
        ? throw new NotSupportedException($"{GetType()} does not support {nameof(CompleteChatStreamingAsync)}, and no inner handler is defined.")
        : innerHandler.CompleteChatStreamingAsync(messages, options, cancellationToken);

    // This delegate overload should probably be an extension method wrapping an underlying method that
    // takes structured metadata
    public virtual ChatFunction DefineChatFunction<T>(string name, string description, T @delegate) where T : Delegate
        => innerHandler is null
        ? throw new NotSupportedException($"{GetType()} does not support {nameof(DefineChatFunction)}, and no inner handler is defined.")
        : innerHandler.DefineChatFunction(name, description, @delegate);

    public virtual Task ExecuteChatFunctionAsync(ChatToolCall toolCall, ChatOptions options)
        => innerHandler is null
        ? throw new NotSupportedException($"{GetType()} does not support {nameof(ExecuteChatFunctionAsync)}, and no inner handler is defined.")
        : innerHandler.ExecuteChatFunctionAsync(toolCall, options);
}
