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
    // implementation to have its own tool-execution logic based on how the underlying LLM indicates its tool-calling intent.
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

// If we want a standard way to attach filters, we can define this interface and have the IChatService implementation
// do this if they want to participate.
public interface IChatFunctionFilters
{
    void OnFunctionInvocation(IChatFunctionFilter filter);
}

public interface IChatFunctionFilter
{
    Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next);

    class FunctionInvocationContext { }
}

public class ChatOptions
{
    public ChatResponseFormat ResponseFormat { get; set; } = ChatResponseFormat.Text;
    public string? ToolExecutionMode { get; set; } // TODO: Enum
    public List<ChatTool> Tools { get; } = new List<ChatTool>();
    public int Seed { get; set; }
    public int Temperature { get; set; }
}

public enum ChatResponseFormat { Text, JsonObject };

public abstract class ChatTool(string name, string description)
{
    public string Name => name;
    public string Description => description;
}

public abstract class ChatFunction(string name, string description) : ChatTool(name, description)
{
    public abstract Task<object> InvokeAsync(IReadOnlyDictionary<string, object> args);
}

internal class ReflectionChatFunction<T>(string name, string description, T @delegate)
    : ChatFunction(name, description) where T : Delegate
{
    public override Task<object> InvokeAsync(IReadOnlyDictionary<string, object> args)
    {
        // Obviously not right
        return (Task<object>)@delegate.DynamicInvoke(args.Values)!;
    }
}

public class ChatMessage(ChatMessageRole role, string content)
{
    public ChatMessageRole Role => role;
    public string Content => content;
    public string? ToolCallId { get; set; }

    // Should also contain stats about token usage, duration, etc. Ollama will return that.
    // (and in the case of streaming, that info is on the final chunk).

    public static ChatMessage FromChunks(IEnumerable<ChatMessageChunk> chunks)
        => throw new NotImplementedException();
}

public class ChatMessageChunk(string content)
{
    public string Content => content;
}

public enum ChatMessageRole { User, Assistant, System, Tool };
