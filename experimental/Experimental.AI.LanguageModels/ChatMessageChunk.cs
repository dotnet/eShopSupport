namespace Experimental.AI.LanguageModels;

public class ChatMessageChunk(ChatMessageRole role, string content)
{
    public ChatMessageRole Role => role;
    public string Content => content;
}
