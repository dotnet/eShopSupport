using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;

namespace Microsoft.Extensions.Hosting;

public static class DiskCachingChatClientBuilderExtensions
{
    public static ChatClientBuilder UseDiskCaching(this ChatClientBuilder builder, string? cacheDir)
    {
        return !string.IsNullOrEmpty(cacheDir)
            ? builder.UseDistributedCache(new DiskCache(cacheDir))
            : builder;
    }

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
