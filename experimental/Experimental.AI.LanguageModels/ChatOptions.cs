namespace Experimental.AI.LanguageModels;

public record ChatOptions
{
    public IChatOrchestrator FunctionCallOrchestrator { get; set; } = StandardFunctionCallOrchestrator.Instance;
    public ChatResponseFormat ResponseFormat { get; set; } = ChatResponseFormat.Text;
    public string? ToolExecutionMode { get; set; } // TODO: Enum
    public List<ChatTool>? Tools { get; set; }
    public int? Seed { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public IReadOnlyCollection<string>? StopSequences { get; set; }
    public int? MaxTokens { get; set; }
}

public enum ChatResponseFormat { Text, JsonObject };
