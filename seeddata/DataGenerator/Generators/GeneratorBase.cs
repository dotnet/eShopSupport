using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace eShopSupport.DataGenerator.Generators;

public abstract class GeneratorBase<T>
{
    protected abstract string DirectoryName { get; }

    protected abstract object GetId(T item);

    public static string OutputDirRoot => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "output");

    protected string OutputDirPath => Path.Combine(OutputDirRoot, DirectoryName);

    protected JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public GeneratorBase(IServiceProvider services)
    {
        ChatCompletionService = services.GetRequiredService<IChatClient>();
    }

    public async Task<IReadOnlyList<T>> GenerateAsync()
    {
        if (!Directory.Exists(OutputDirPath))
        {
            Directory.CreateDirectory(OutputDirPath);
        }

        var sw = Stopwatch.StartNew();
        await foreach (var item in GenerateCoreAsync())
        {
            sw.Stop();
            Console.WriteLine($"Writing {item!.GetType().Name} {GetId(item)} [generated in {sw.Elapsed.TotalSeconds}s]");
            var path = GetItemOutputPath(GetId(item).ToString()!);
            await WriteAsync(path, item);
            sw.Restart();
        }

        var existingFiles = Directory.GetFiles(OutputDirPath);
        return existingFiles.Select(Read).ToList();
    }

    protected string GetItemOutputPath(string id)
        => Path.Combine(OutputDirPath, $"{id}{FilenameExtension}");

    protected abstract IAsyncEnumerable<T> GenerateCoreAsync();

    protected IChatClient ChatCompletionService { get; }

    protected async Task<string> GetChatCompletion(string prompt)
    {
        // Instructing it to end the content with END_OF_CONTENT is beneficial because it often tries to add a suffix like
        // "I have done the task, hope this helps!". We can avoid that by making it stop before that.
        var chatOptions = new ChatOptions { Temperature = 0.9f, StopSequences = ["END_OF_CONTENT"] };
        var chatHistory = new List<ChatMessage>() { new ChatMessage(ChatRole.User, prompt) };
        var response = await ChatCompletionService.CompleteAsync(chatHistory, chatOptions);
        return response.Message.Text ?? string.Empty;
    }

    protected async Task<TResponse> GetAndParseJsonChatCompletion<TResponse>(string prompt, int? maxTokens = null, IList<AITool>? tools = null)
    {
        var options = new ChatOptions
        {
            // MaxOutputTokens = maxTokens, // TODO: Re-enable when https://github.com/dotnet/temp-ai-abstractions/issues/294 is fixed
            Temperature = 0.9f,
            ResponseFormat = ChatResponseFormat.Json,
            Tools = tools,
        };

        var chatHistory = new List<ChatMessage>() { new ChatMessage(ChatRole.User, prompt) };
        var response = await RunWithRetries(() => ChatCompletionService.CompleteAsync(chatHistory, options));
        var responseString = response.Message.Text ?? string.Empty;

        // Due to what seems like a server-side bug, when asking for a json_object response and with tools enabled,
        // it often replies with two or more JSON objects concatenated together (duplicates or slight variations).
        // As a workaround, just read the first complete JSON object from the response.
        var parsed = ReadAndDeserializeSingleValue<TResponse>(responseString, SerializerOptions)!;
        return parsed;
    }

    private static async Task<TResult> RunWithRetries<TResult>(Func<Task<TResult>> operation)
    {
        var delay = TimeSpan.FromSeconds(5);
        var maxAttempts = 5;
        for (var attemptIndex = 1; ; attemptIndex++)
        {
            try
            {
                return await operation();
            }
            catch (Exception e) when (attemptIndex < maxAttempts)
            {
                Console.WriteLine($"Exception on attempt {attemptIndex}: {e.Message}. Will retry after {delay}");
                await Task.Delay(delay);
                delay += TimeSpan.FromSeconds(15);
            }
        }
    }

    private static TResponse? ReadAndDeserializeSingleValue<TResponse>(string json, JsonSerializerOptions options)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json).AsSpan());
        return JsonSerializer.Deserialize<TResponse>(ref reader, options);
    }

    private static async Task<List<T>> CollectAsyncEnumerable(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (var item in source)
        {
            result.Add(item);
        }
        return result;
    }

    protected virtual string FilenameExtension => ".json";

    protected virtual Task WriteAsync(string path, T item)
    {
        var itemJson = JsonSerializer.Serialize(item, SerializerOptions);
        return File.WriteAllTextAsync(path, itemJson);
    }

    protected virtual T Read(string path)
    {
        try
        {
            using var existingJson = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(existingJson, SerializerOptions)!;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading {path}: {ex.Message}");
            throw;
        }
    }

    protected IAsyncEnumerable<V> MapParallel<U, V>(IEnumerable<U> source, Func<U, Task<V>> map)
    {
        var outputs = Channel.CreateUnbounded<V>();
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 5 };
        var mapTask = Parallel.ForEachAsync(source, parallelOptions, async (sourceItem, ct) =>
        {
            try
            {
                var mappedItem = await map(sourceItem);
                await outputs.Writer.WriteAsync(mappedItem);
            }
            catch (Exception ex)
            {
                outputs.Writer.TryComplete(ex);
            }
        });

        mapTask.ContinueWith(_ => outputs.Writer.TryComplete());

        return outputs.Reader.ReadAllAsync();
    }
}
