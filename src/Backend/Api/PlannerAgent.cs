using AutoGen.Core;
using AutoGen.SemanticKernel;
using AutoGen.SemanticKernel.Extension;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace eShopSupport.Backend.Api;

public class PlannerAgent : IAgent
{
    private readonly IAgent innerAgent;

    public string Name => "planner";

    public PlannerAgent(IChatCompletionService chatCompletionService)
    {
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton(chatCompletionService);
        var kernel = kernelBuilder.Build();

        innerAgent = new SemanticKernelAgent(
            kernel: kernel,
            name: this.Name,
            systemMessage: """
            You are helping user resolve customer tickets. Based on the context, suggest the next step for the following agents to handle.
            """)
            .RegisterMessageConnector()
            .RegisterPrintMessage();
    }

    public async Task<IMessage> GenerateReplyAsync(IEnumerable<IMessage> messages, GenerateReplyOptions? options = null, CancellationToken cancellationToken = default)
    {
        var taskCompleteCheckPrompt = """
            observe the conversation and determine if task from user is done. Reply with the following JSON object:
            {
                "reason": "<a short description of the reason why the task is done>",
                "task_done": true or false
            }
            """;
        var taskCompleteCheck = await innerAgent.SendAsync(taskCompleteCheckPrompt, messages);

        if (taskCompleteCheck.GetContent()?.ToLower().Contains("\"task_done\": true") is true)
        {
            return new TextMessage(Role.Assistant, "The task is done.", from: this.Name);
        }

        var suggestNextStepPrompt = """
            Suggest the next step for the following agents to handle. Below are available agents:
            - manual_search: Help you search the manual for information.
            - writer: Help you write responses or answers on given context.

            Use the following JSON format to suggest the next step, create ONE SINGLE STEP only:
            {
                "next_step": "<a short description of the next step>",
                "assigned_agent": "<the agent to handle the next step>"
            }
            """;

        var message = new TextMessage(Role.User, suggestNextStepPrompt);

        var jsonReply = await innerAgent.GenerateReplyAsync(messages.Concat([message]), options, cancellationToken);

        // prompt it nicely
        return await innerAgent.SendAsync("prompt it in the format of @<assigned_agent>, <step>, make your answer short.", chatHistory: [jsonReply]);
    }
}
