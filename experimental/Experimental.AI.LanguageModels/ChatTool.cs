namespace Experimental.AI.LanguageModels;

public abstract class ChatTool(string name, string description)
{
    public string Name => name;
    public string Description => description;
}

public abstract class ChatFunction(string name, string description) : ChatTool(name, description)
{
}
