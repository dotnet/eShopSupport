namespace eShopSupport.Backend.Data;

internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> asyncEnumerable)
    {
        var result = new List<T>();
        await foreach (var item in asyncEnumerable)
        {
            result.Add(item);
        }
        return result;
    }
}
