using System.Text.Json;
using eShopSupport.Backend.Data;
using Microsoft.AI.Agents.Abstractions;
using Microsoft.AI.Agents.Orleans;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;

namespace eShopSupport.Backend.Agents;

public class Auditor([PersistentState("state", "messages")] IPersistentState<AgentState<object>> state, ISemanticTextMemory memory,[FromKeyedServices("oagents")] Kernel kernel, ILogger<Auditor> logger)
 : AiAgent<object>(state, memory, kernel), IAuditReponses
{
    public async Task<AuditResult> AuditResponse(string response)
    {
        try
        {
            var context = new KernelArguments { ["input"] = AppendChatHistory(response) };
            var settings = new OpenAIPromptExecutionSettings{
                 ResponseFormat = "json_object",
                 MaxTokens = 4096, 
                 Temperature = 0.8,
                 TopP = 1 
            };
            var result = await CallFunction(AuditorSkills.Respond, context, settings);
            return JsonSerializer.Deserialize<AuditResult>(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error responding to user");
            return default;
        }
    }

    public override Task HandleEvent(Event item)
    {
        return Task.CompletedTask;
    }
}

public interface IAuditReponses : IGrainWithStringKey
{
    Task<AuditResult> AuditResponse(string response);
}