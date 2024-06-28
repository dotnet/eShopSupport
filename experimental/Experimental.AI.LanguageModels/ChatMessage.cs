namespace Experimental.AI.LanguageModels;

public class ChatMessage(ChatMessageRole role, string content)
{
    public ChatMessageRole Role => role;
    public string Content => content;

    public IReadOnlyList<ChatMessageToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }

    // Should also contain stats about token usage, duration, etc. Ollama will return that.
    // (and in the case of streaming, that info is on the final chunk).

    public static ChatMessage FromChunks(IEnumerable<ChatMessageChunk> chunks)
        => throw new NotImplementedException();
}

public abstract class ChatMessageToolCall { }

public class ChatMessageChunk(string content)
{
    public string Content => content;
}

public enum ChatMessageRole { User, Assistant, System, Tool };
