using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace eShopSupport.ServiceDefaults.Clients.ChatCompletion;

// This is primarily so that E2E tests running in CI don't have to call LLMs for real, so that:
// [1] We don't have to make the API keys available to CI
// [2] There's no risk of random failures due to network issues or the nondeterminism of the AI responses
// It will not be used in real apps in production. Its other benefit is reducing API call costs during local development.

internal class ChatCompletionResponseCache(string cacheDir, ILoggerFactory loggerFactory)
{
    private readonly ILogger<ChatCompletionResponseCache> _logger = loggerFactory.CreateLogger<ChatCompletionResponseCache>();
    private readonly static JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool TryGetCachedResponse<T>(ChatHistory chatHistory, PromptExecutionSettings? executionSettings, [NotNullWhen(true)] out T? response)
    {
        var request = new CacheFileRequest(chatHistory, executionSettings);
        var filePath = GetCacheFilePath(request);
        if (File.Exists(filePath))
        {
            _logger.LogInformation("Using cached response for {Path}", Path.GetFileName(filePath));
            var contents = File.ReadAllText(filePath);
            response = JsonSerializer.Deserialize<CacheFileContents<T>>(contents, JsonOptions)!.Response!;
            return true;
        }
        else
        {
            _logger.LogInformation("Did not find cached response for {Path}", Path.GetFileName(filePath));
            response = default;
            return false;
        }
    }

    public void SetCachedResponse<T>(ChatHistory chatHistory, PromptExecutionSettings? executionSettings, T response)
    {
        var contents = new CacheFileContents<T>(new(chatHistory, executionSettings), response);

        var filePath = GetCacheFilePath(contents.Request);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(contents, JsonOptions));
    }

    private string GetCacheFilePath(CacheFileRequest request)
        => GetCacheFilePath(request, request.ChatHistory.LastOrDefault()?.ToString() ?? "no_messages");

    private string GetCacheFilePath(CacheFileRequest request, string summary)
        => Path.Combine(cacheDir, $"{GetCacheKey(request, summary)}.json");

    private static string GetCacheKeyInput(CacheFileRequest request)
        => JsonSerializer.Serialize(new object?[] { request.ChatHistory, request.ExecutionSettings }, JsonOptions).Replace("\\r", "");

    private static string GetCacheKey(CacheFileRequest request, string summary)
    {
        var json = GetCacheKeyInput(request);
        var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));

        var sb = new StringBuilder();
        for (var i = 0; i < 8; i++)
        {
            sb.Append(hash[i].ToString("x2"));
        }

        sb.Append("_");
        sb.Append(ToShortSafeString(summary));

        return sb.ToString();
    }

    private static string ToShortSafeString(string summary)
    {
        // This is just to make the cache filenames more recognizable. Won't help much if there's a common long prefix.
        var sb = new StringBuilder();
        foreach (var c in summary)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (c == ' ')
            {
                sb.Append('_');
            }

            if (sb.Length >= 30)
            {
                break;
            }
        }
        return sb.ToString();
    }

    private record CacheFileContents<T>(CacheFileRequest Request, T Response);
    private record CacheFileRequest(ChatHistory ChatHistory, PromptExecutionSettings? ExecutionSettings);
}
