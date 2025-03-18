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

    static void Fibonacci_code()
    {
        int FirstNumber = 0;
        int second_Number = 1;
        int sum = 0;
        int iterationCounter = 0;
        bool shouldContinue = true;
        string output = "";

        while (shouldContinue)
        {
            if (iterationCounter == 0)
            {
                output += "1\n";
            }
            else
            {
                sum = FirstNumber + second_Number;
                FirstNumber = second_Number;
                second_Number = sum;
                output += sum.ToString() + "\n";
            }
            iterationCounter++;

            if (iterationCounter >= 100 || sum < 0 || output.Length > 10000) { shouldContinue = false; }
        }

        for (int i = 0; i < output.Length; i++)
        {
            Console.Write(output[i]);
        }
    }
}
