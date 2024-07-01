using System.Text;

namespace Experimental.AI.LanguageModels;

public class ChatMessage(ChatMessageRole role, string? content)
{
    public ChatMessageRole Role => role;
    public string? Content => content;

    public IReadOnlyList<ChatToolCall>? ToolCalls { get; set; }

    // Should also contain stats about token usage, duration, etc. Ollama will return that.
    // (and in the case of streaming, that info is on the final chunk).

    public static IEnumerable<ChatMessage> FromChunks(IEnumerable<ChatMessageChunk> chunks)
    {
        var contentBuilder = new StringBuilder();
        var role = (ChatMessageRole?)null;
        foreach (var chunk in chunks)
        {
            // TODO: Also collate statistics from the chunks, e.g., token usage, duration, etc.
            // Ollama will include that in the final chunk.
            contentBuilder.Append(chunk.Content);

            if (role != chunk.Role)
            {
                if (role.HasValue && contentBuilder.Length > 0)
                {
                    yield return new ChatMessage(role.Value, contentBuilder.ToString());
                    contentBuilder.Clear();
                }

                role = chunk.Role;
            }
        }

        if (role.HasValue && contentBuilder.Length > 0)
        {
            yield return new ChatMessage(role.Value, contentBuilder.ToString());
        }
    }
}
