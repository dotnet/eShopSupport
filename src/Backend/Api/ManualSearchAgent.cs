using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoGen.Core;
using AutoGen.OpenAI.Extension;
using AutoGen.SemanticKernel;
using AutoGen.SemanticKernel.Extension;
using eShopSupport.Backend.Services;
using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Memory;

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
    [Description("Search the manual. To Search a manual for pecific product, provide the product ID. Otherwise, search all manuals.")]
    public async Task<string> SearchManualAsync(
        [Description("product ID.")][DefaultValue(null)] int? productID,
        [Description("search phrase")] string searchPhrase)
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
        IChatCompletionService chatCompletionService,
        ProductManualSemanticSearch manualSearch,
        HttpResponse? httpResponse = null)
    {
        _manualSearch = manualSearch;
        _httpResponse = httpResponse;
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton(chatCompletionService);
        var kernel = kernelBuilder.Build();
        _kernelChatAgent = new SemanticKernelAgent(
            kernel: kernel,
            name: this.Name,
            systemMessage: "You are a helpful manual search assistant. You always respond in JSON object.")
            .RegisterMessageConnector()
            .RegisterStreamingMiddleware(new EventMessageMiddleware())
            .RegisterPrintMessage();
    }

    public string Name => "manual_search";

    public async Task<IMessage> GenerateReplyAsync(IEnumerable<IMessage> messages, GenerateReplyOptions? options = null, CancellationToken cancellationToken = default)
    {
        var prompt = """
            Determine if more information is needed. If so, search the manual for the information.
            To search a manual for all products, reply the following JSON object.
            ```json
            {"searchPhrase": string}
            ```

            To search a manual for a specific product, reply the following JSON object.
            ```json
            {"productID": number, "searchPhrase": string}
            ```
            """;
        // replace /r/n with /n if any
        prompt = prompt.Replace("\r\n", "\n");
        var message = new TextMessage(Role.User, prompt);
        var reply = await _kernelChatAgent.GenerateReplyAsync(messages.Concat([message]), new GenerateReplyOptions()
        {
            Temperature = 0,
        }, cancellationToken);

        // try parse the reply as a function call
        try
        {
            var content = reply.GetContent();

            // for llama3.1 only, remove <|python_tag|>
            content = content.Replace("<|python_tag|>", "");
            // for llama3.1 only, remove <|eom_id|>
            content = content.Replace("<|eom_id|>", "");
            // if the json is wrapped between ```json and ```, get the content inside
            if (content?.IndexOf("```json") is int start && content.IndexOf("```", start + 6) is int end && start >= 0 && end >= 0)
            {
                content = content.Substring(start + 7, end - start - 7);
            }

            var obj = JsonSerializer.Deserialize<SearchManualAsyncSchema>(content);

            if (obj?.searchPhrase is not null)
            {
                var searchResult = await SearchManualAsync(obj.productID, obj.searchPhrase);

                return new TextMessage(Role.Assistant, searchResult, from: this.Name).WithEvent(AssistantEvent.CompleteStep);
            }
        }
        catch (JsonException)
        {
            return new TextMessage(Role.Assistant, "fail to search manual, please modify the search phrase and try again", from: this.Name);
            // ignore
        }

        return new TextMessage(Role.Assistant, "no information found from manual", from: this.Name).WithEvent(AssistantEvent.CompleteStep);
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
