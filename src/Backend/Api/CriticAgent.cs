using AutoGen.Core;
using AutoGen.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;
using AutoGen.SemanticKernel.Extension;

namespace eShopSupport.Backend.Api;

public class CriticAgent : IAgent
{
    private readonly IStreamingAgent innerAgent;

    public CriticAgent(IChatCompletionService chatCompletionService)
    {
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton(chatCompletionService);
        var kernel = kernelBuilder.Build();

        innerAgent = new SemanticKernelAgent(
            kernel: kernel,
            name: this.Name,
            systemMessage: """
            You are critic agent, you review the customer support agent's response and provide feedback.
            """)
            .RegisterMessageConnector()
            .RegisterStreamingMiddleware(new EventMessageMiddleware())
            .RegisterPrintMessage();
    }

    public string Name => "critic";

    public async Task<IMessage> GenerateReplyAsync(IEnumerable<IMessage> messages, GenerateReplyOptions? options = null, CancellationToken cancellationToken = default)
    {
        var prompt = """
            Carefully review the reply from the customer support agent and determine if the response contains any offensive information.
            If there is no offensive or inappropriate information, say 'the reply from customer support has been reviewed and approved'. Otherwise, ask the customer support agent to revise the reply.
            """;

        var reply = await innerAgent.SendAsync(prompt, messages);

        if (reply.GetContent()?.ToLower().Contains("the reply from customer support has been reviewed and approved") is true)
        {
            return reply.WithEvent(AssistantEvent.CompleteStep);
        }

        return reply.WithEvent(AssistantEvent.ReplyNeedsRevision);
    }
}
