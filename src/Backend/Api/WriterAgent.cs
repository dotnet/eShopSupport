using System.Text.Json;
using AutoGen.Core;
using AutoGen.SemanticKernel;
using AutoGen.SemanticKernel.Extension;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.AspNetCore.Http;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Npgsql.Internal;
using static eShopSupport.Backend.Api.AssistantApi;

namespace eShopSupport.Backend.Api;

public class WriterAgent : IAgent
{
    private readonly IStreamingAgent innerAgent;
    private readonly HttpResponse? _httpResponse;

    public WriterAgent(
        IChatCompletionService chatCompletionService,
        HttpResponse? httpResponse = null)
    {
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton(chatCompletionService);
        var kernel = kernelBuilder.Build();
        _httpResponse = httpResponse;

        innerAgent = new SemanticKernelAgent(
            kernel: kernel,
            name: this.Name,
            systemMessage: """
            You are writer. Based on the context, provide an answer to the user's question.
            """)
            .RegisterMessageConnector()
            .RegisterPrintMessage();
    }
    public string Name => "writer";

    public async Task<IMessage> GenerateReplyAsync(IEnumerable<IMessage> messages, GenerateReplyOptions? options = null, CancellationToken cancellationToken = default)
    {
        var containSearchResult = false;
        if (messages.Any(m => m.From == "manual_search"))
        {
            containSearchResult = true;
        }

        IMessage? reply = null;
        if (containSearchResult)
        {
            var prompt = """
                Based on this context, provide an answer to the user's question.
                ALWAYS justify your answer by citing the most relevant one of the above search results. Do this by including this syntax in your reply:
                <cite searchResultId=number>shortVerbatimQuote</cite>
                shortVerbatimQuote must be a very short, EXACT quote (max 10 words) from whichever search result you are citing.
                Only give one citation per answer. Always give a citation because this is important to the business.
                """;

            reply = await innerAgent.SendAsync(prompt, messages);
        }
        else
        {
            var prompt = """
                Based on this context, provide an answer to the user's question.
                """;

            reply = await innerAgent.SendAsync(prompt, messages);
        }

        // if the final answer address the user by name?
        var addressFinalAnswerPrompt = """
                Consider the answer you just gave and decide whether it is addressed to the customer by name as a reply to them.
                Reply as a JSON object in this form: { "isAddressedByNameToCustomer": trueOrFalse }.
                """;

        var addressedByNameToCustomer = await innerAgent.SendAsync(addressFinalAnswerPrompt, chatHistory: messages.Concat([reply]));

        if (addressedByNameToCustomer.GetContent() is string replyText && _httpResponse is not null)
        {
            var isAddressedToCustomer = JsonSerializer.Deserialize<IsAddressedToCustomerReply>(replyText, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (isAddressedToCustomer?.IsAddressedByNameToCustomer == true)
            {
                await _httpResponse.WriteAsync(",\n");
                await _httpResponse.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(AssistantChatReplyItemType.IsAddressedToCustomer, "true", From: this.Name)));
            }
        }

        return reply;
    }
}
