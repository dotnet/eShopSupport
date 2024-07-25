using AutoGen.Core;
using AutoGen.SemanticKernel;
using AutoGen.SemanticKernel.Extension;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace eShopSupport.Backend.Api;

public class PlannerAgent : IAgent
{
    private readonly IAgent innerAgent;
    private readonly string _task; // the task!

    public string Name => "task_planner";

    public PlannerAgent(IChatCompletionService chatCompletionService, string task)
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
        this._task = task;
    }

    public async Task<IMessage> GenerateReplyAsync(IEnumerable<IMessage> messages, GenerateReplyOptions? options = null, CancellationToken cancellationToken = default)
    {
        var taskCompleteCheckPrompt = $$"""
            observe the most recent conversation and determine if the following task has been resolved.
            
            task: {{_task}}

            Reply with the following JSON object:
            {
                "reason": "<a short description of the reason why the task is done>",
                "task_done": true or false
            }
            """;

        // only include the new messages when check if task is done
        var lastUserMessageIndex = messages.ToList().FindLastIndex(m => m.From == "user");
        var messageToInclude = messages.Skip(lastUserMessageIndex);
        if (messageToInclude.Count() > 1)
        {
            // only check when there are new messages
            messageToInclude = messageToInclude.Append(new TextMessage(Role.User, taskCompleteCheckPrompt));

            var taskCompleteCheck = await innerAgent.GenerateReplyAsync(messageToInclude, new GenerateReplyOptions { Temperature = 0, StopSequence = ["}"] });

            if (taskCompleteCheck.GetContent()?.ToLower().Contains("\"task_done\": true") is true)
            {
                return new TextMessage(Role.Assistant, "The task is done.", from: this.Name);
            }
        }

        var suggestNextStepPrompt = $$"""
            Given the task: <{{_task}}>, suggest the next step for the following agents to handle. Below are available agents:
            - manual_search: Help you search the manual for information.
            - customer_support: Help you write responses or summarize the conversation.

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
