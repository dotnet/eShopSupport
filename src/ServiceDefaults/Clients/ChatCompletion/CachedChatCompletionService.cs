using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace eShopSupport.ServiceDefaults.Clients.ChatCompletion;

internal class CachedChatCompletionService(IChatCompletionService underlying, string cacheDir, ILoggerFactory loggerFactory) : IChatCompletionService
{
    public IReadOnlyDictionary<string, object?> Attributes => throw new NotImplementedException();

    private readonly ChatCompletionResponseCache _cache = new(cacheDir, loggerFactory);
    private readonly static JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetCachedResponse(chatHistory, executionSettings, out var cachedJson))
        {
            return JsonSerializer.Deserialize<ChatMessageContent[]>(cachedJson, JsonOptions)!;
        }

        var response = await underlying.GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
        _cache.SetCachedResponse(chatHistory, executionSettings, JsonSerializer.Serialize(response, JsonOptions));
        return response;
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetCachedResponse(chatHistory, executionSettings, out var cachedJson))
        {
            var chunks = JsonSerializer.Deserialize<StreamingChatMessageContent[]>(cachedJson, JsonOptions)!;
            foreach (var chunk in chunks)
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

        _cache.SetCachedResponse(chatHistory, executionSettings, JsonSerializer.Serialize(capturedChunks, JsonOptions));
    }
}
