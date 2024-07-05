using System.Runtime.CompilerServices;

namespace Experimental.AI.LanguageModels;

public class StandardFunctionCallOrchestrator : IChatOrchestrator
{
    public static StandardFunctionCallOrchestrator Instance { get; } = new();

    public Task<IReadOnlyList<ChatMessage>> ChatAsync(ChatClient client, IReadOnlyList<ChatMessage> messages, ChatOptions options, CancellationToken cancellationToken)
    {
        // TODO: support tool calls
        return client.CompleteChatAsync(messages, options, cancellationToken);
    }

    public async IAsyncEnumerable<ChatMessageChunk> ChatStreamingAsync(ChatClient client, IReadOnlyList<ChatMessage> messages, ChatOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const int maxIterations = 3;
        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            var childOptions = iteration < maxIterations
                ? options
                : options with { Tools = null };

            var toolCalls = new List<ChatToolCall>();
            await foreach (var chunk in client.CompleteChatStreamingAsync(messages, childOptions, cancellationToken))
            {
                if (chunk.ToolCall is { } toolCall)
                {
                    toolCalls.Add(toolCall);
                }
                else if (chunk.Content is not null)
                {
                    yield return chunk;
                }
            }

            if (toolCalls.Any())
            {
                foreach (var toolCall in toolCalls)
                {
                    await client.ExecuteChatFunctionAsync(toolCall, options);
                }

                messages = new List<ChatMessage>(messages)
                {
                    new ChatMessage(ChatMessageRole.Assistant, string.Empty) { ToolCalls = toolCalls },
                };
            }
            else
            {
                break;
            }
        }
    }
}
