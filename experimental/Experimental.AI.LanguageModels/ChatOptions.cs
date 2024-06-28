namespace Experimental.AI.LanguageModels;

public class ChatOptions
{
    public ChatResponseFormat ResponseFormat { get; set; } = ChatResponseFormat.Text;
    public string? ToolExecutionMode { get; set; } // TODO: Enum
    public List<ChatTool>? Tools { get; set; }
    public int Seed { get; set; }
    public int Temperature { get; set; }
}

public enum ChatResponseFormat { Text, JsonObject };
