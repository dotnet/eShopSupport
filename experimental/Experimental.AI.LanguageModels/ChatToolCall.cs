namespace Experimental.AI.LanguageModels;

public abstract class ChatToolCall(string toolName)
{
    // Right now we don't need anything here, because each IChatService implements its
    // own subclass that tracks whatever state it needs to track.
    // However, it probably is necessary to put enough structure or at least storage
    // into the abstraction so that you could round-trip through a serialized representation.
    // Going further still it may be necessary not to use polymorphism at all, since in
    // general that can't round-trip through JSON.
    // This is fine if we're willing to assert that tool calls can be reduced to:
    // - Tool name
    // - Arguments (as dictionary of string->JsonElement, or just a JsonObject)
    // - Return value (as string, since it has to get injected into the prompt as a string ultimately))
    // - AdditionalData (e.g., as JsonObject, for example so that OpenAI can track its ToolCallId data

    public string Name => toolName;

    public object? Result { get; set; }
}
