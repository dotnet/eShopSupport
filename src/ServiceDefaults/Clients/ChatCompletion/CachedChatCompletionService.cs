using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace eShopSupport.ServiceDefaults.Clients.ChatCompletion;

internal class CachedChatCompletionService(IChatCompletionService underlying, string cacheDir, ILoggerFactory loggerFactory) : IChatCompletionService
{
    public IReadOnlyDictionary<string, object?> Attributes => throw new NotImplementedException();

    private readonly ChatCompletionResponseCache _cache = new(cacheDir, loggerFactory);

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetCachedResponse<ChatMessageContent[]>(chatHistory, executionSettings, out var cached))
        {
            return cached;
        }

        var response = await underlying.GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
        _cache.SetCachedResponse(chatHistory, executionSettings, response);
        return response;
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetCachedResponse<StreamingChatMessageContent[]>(chatHistory, executionSettings, out var cached))
        {
            foreach (var chunk in cached)
            {
                yield return chunk;
            }

            yield break;
        }

        var response = underlying.GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
        var capturedChunks = new List<StreamingChatMessageContent>();
        await foreach (var chunk in response)
        {
            capturedChunks.Add(chunk);
            yield return chunk;
        }

        _cache.SetCachedResponse(chatHistory, executionSettings, capturedChunks);
    }
}
