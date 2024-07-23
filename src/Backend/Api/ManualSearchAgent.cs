using eShopSupport.Backend.Services;
using AutoGen.Core;
using System.Text;
using Microsoft.SemanticKernel.Memory;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;
using AutoGen.SemanticKernel;
using System.ComponentModel;
using AutoGen.SemanticKernel.Extension;
using AutoGen.OpenAI;
using Azure.AI.OpenAI;
using AutoGen.OpenAI.Extension;
using eShopSupport.ServiceDefaults.Clients.Backend;

namespace eShopSupport.Backend.Api;

public partial class ManualSearchAgent : IAgent
{
    private readonly ProductManualSemanticSearch _manualSearch;
    private readonly IAgent _kernelChatAgent;
    private readonly HttpResponse? _httpResponse;


    /// <summary>
    /// Search the manual. To Search a manual for pecific product, provide the product ID.
    /// Otherwise, search all manuals.
    /// </summary>
    /// <param name="productID">product ID.</param>
    /// <param name="searchPhrase">search prase</param>
    /// <returns></returns>
    [Function]
    [KernelFunction]
    [Description("earch the manual. To Search a manual for pecific product, provide the product ID. Otherwise, search all manuals.")]
    public async Task<string> SearchManualAsync(
        [Description("product ID.")] [DefaultValue(null)] int? productID,
        [Description("search prase")] string searchPhrase)
    {
        if (_httpResponse != null)
        {
            await _httpResponse.WriteAsync(",\n");
            await _httpResponse.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(AssistantChatReplyItemType.Search, searchPhrase, From: this.Name)));
        }

        var results = await _manualSearch.SearchAsync(productID, searchPhrase);

        var sb = new StringBuilder();
        sb.AppendLine($"Search results for '{searchPhrase}'");

        foreach (var item in results)
        {
            var searchResultPrompt = $"<search_result productId=\"{GetProductId(item)}\" searchResultId=\"{item.Metadata.Id}\">{item.Metadata.Text}</search_result>";
            sb.AppendLine(searchResultPrompt);

            if (_httpResponse != null)
            {
                await _httpResponse.WriteAsync(",\n");
                await _httpResponse.WriteAsync(JsonSerializer.Serialize(new AssistantChatReplyItem(
                    AssistantChatReplyItemType.SearchResult,
                    string.Empty,
                    int.Parse(item.Metadata.Id),
                    GetProductId(item),
                    GetPageNumber(item),
                    From: this.Name)));
            }
        }

        return sb.ToString();
    }

    public ManualSearchAgent(
        OpenAIClient client,
        ProductManualSemanticSearch manualSearch,
        HttpResponse? httpResponse = null)
    {
        _manualSearch = manualSearch;
        var functionCallMiddleware = new FunctionCallMiddleware(
            functions: [this.SearchManualAsyncFunctionContract],
            functionMap: new Dictionary<string, Func<string, Task<string>>>
            {
                { this.SearchManualAsyncFunctionContract.Name!, this.SearchManualAsyncWrapper },
            });
        _httpResponse = httpResponse;
        _kernelChatAgent = new OpenAIChatAgent(
            openAIClient: client,
            name: this.Name,
            modelName: "gpt-4o",
            systemMessage: "You are a helpful manual search assistant.")
            .RegisterMessageConnector()
            .RegisterMiddleware(functionCallMiddleware);
    }

    public string Name => "manual_search";

    public async Task<IMessage> GenerateReplyAsync(IEnumerable<IMessage> messages, GenerateReplyOptions? options = null, CancellationToken cancellationToken = default)
    {
        var reply = await _kernelChatAgent.GenerateReplyAsync(messages, options, cancellationToken);

        return new TextMessage(Role.Assistant, reply.GetContent(), from: this.Name);
    }

    private static int? GetProductId(MemoryQueryResult result)
    {
        var match = Regex.Match(result.Metadata.ExternalSourceName, @"productid:(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    private static int? GetPageNumber(MemoryQueryResult result)
    {
        var match = Regex.Match(result.Metadata.AdditionalMetadata, @"pagenumber:(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }
}
