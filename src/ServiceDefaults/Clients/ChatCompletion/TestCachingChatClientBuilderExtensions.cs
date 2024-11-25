using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// This is an experimental approach to making E2E tests deterministic even when depending on nondeterministic LLMs.
/// If we detect that the application is running in a test environment, we attach a disk cache to the chat client, so
/// that it always replays the same responses for the same inputs. The cache data is stored in source control and can
/// be refreshed by deleting it and re-running the tests.
/// 
/// This has the benefit that:
///  - E2E tests run faster
///  - E2E tests are more reliable
///  - E2E tests can run in CI without having to disclose any API keys/secrets to the CI servers
/// 
/// However it also has drawbacks:
///  - Developers must manage a directory of cached responses. Whenever prompts are modified, some existing cache
///    entries become redundant, but it's impractical to work out which of them are now redundant. So developers
///    usually need to delete and regenerate all cache entries, which can result in large source control diffs that
///    are hard to merge together if multiple developers do this concurrently.
///  - It can mask flaky tests. An E2E test may appear to work reliably but only does because the LLM response
///    happened to be good on the first run. If the cache entry is deleted, the test may fail on subsequent runs
///    because the LLM response may be different. To avoid problems, it's still important to author tests in a way
///    that isn't sensitive to the exact wording of the LLM response.
/// </summary>

public static class TestCachingChatClientBuilderExtensions
{
    public static ChatClientBuilder UseCachingForTest(this ChatClientBuilder builder)
    {
        return builder.Use((client, serviceProvider) => {
            var cacheDir = serviceProvider.GetRequiredService<IConfiguration>()["E2E_TEST_CHAT_COMPLETION_CACHE_DIR"];
            if (!string.IsNullOrEmpty(cacheDir))
            {
                builder.UseDistributedCache(new DiskCache(cacheDir));
            }
            return client;
        });
    }

    /// <summary>
    /// An <see cref="IDistributedCache"/> that stores data in the filesystem.
    /// </summary>
    private class DiskCache(string cacheDir) : IDistributedCache
    {
        public byte[]? Get(string key)
        {
            var path = FilePath(key);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            var path = FilePath(key);
            return File.Exists(path) ? await File.ReadAllBytesAsync(path, token) : null;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
            => Task.CompletedTask;

        public void Remove(string key)
            => File.Delete(FilePath(key));

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            var path = FilePath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, value);
        }

        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            var path = FilePath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, value, token);
        }

        private string FilePath(string key)
            => Path.Combine(cacheDir, $"{key}.json");
    }
}
