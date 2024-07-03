namespace Experimental.AI.LanguageModels;

public class ChatMessageChunk(ChatMessageRole role, string? content, ChatToolCall? toolCall)
{
    public ChatMessageRole Role => role;
    public string? Content => content;
    public ChatToolCall? ToolCall => toolCall;
}
