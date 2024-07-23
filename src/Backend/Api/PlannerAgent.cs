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
            You are Planner.
            Given a task, please determine what step is needed to complete the task.
            Please only suggest steps that can be done by others. You can assgin the steps to others explicitly.
            Remember, YOU DON't DO ANYTHING. YOU JUST PLAN.

            After each step is done by others, check the progress and instruct the remaining steps.
            If a step fails, try to work around it.
            If the task is completed, say 'task completed'.
            """)
            .RegisterMessageConnector();
    }

    public Task<IMessage> GenerateReplyAsync(IEnumerable<IMessage> messages, GenerateReplyOptions? options = null, CancellationToken cancellationToken = default)
    {
        return innerAgent.GenerateReplyAsync(messages, options, cancellationToken);
    }
}
