using eShopSupport.Backend.Data;
using Microsoft.AI.Agents.Abstractions;
using Microsoft.AI.Agents.Orleans;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;

namespace eShopSupport.Backend.Agents;

public class Supporter([PersistentState("state", "messages")] IPersistentState<AgentState<object>> state, ISemanticTextMemory memory, [FromKeyedServices("oagents")] Kernel kernel, ILogger<Supporter> logger) 
: AiAgent<object>(state, memory, kernel), ISupportTickets
{
    public async Task<string> RespondToUser(string ask)
    {
        try
        {
            var context = new KernelArguments { ["input"] = ask };
            return await CallFunction(SupporterSkills.Respond, context);
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

    public async Task<string> ReviseResponse(string ask, string response, string feedback)
    {
        try
        {
            var context = new KernelArguments { ["input"] = ask, ["response"] = response, ["feedback"] = feedback };
            return await CallFunction(SupporterSkills.Revise, context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error responding to user");
            return default;
        }
    }
}

public interface ISupportTickets : IGrainWithStringKey
{
    Task<string> RespondToUser(string ask);
    Task<string> ReviseResponse(string ask, string response, string feedback);
}
