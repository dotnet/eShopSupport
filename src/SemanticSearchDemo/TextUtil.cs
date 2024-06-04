using System.Text;

internal class TextUtil
{
    internal static string Embedding(ReadOnlyMemory<float> embedding)
        => $"[{string.Join(", ", embedding.Span.Slice(0, 5).ToArray().Select(f => $"{f:F3}"))}, ... (length: {embedding.Length})]";

    internal static string Indent(string text, int maxLineLength = 80)
    {
        var sb = new StringBuilder();
        var indent = "    ";
        var remaining = text.AsSpan();
        while (remaining.Length > 0)
        {
            // Emit the next line
            sb.Append(indent);

            if (remaining.Length <= maxLineLength - indent.Length && remaining.IndexOf('\n') < 0)
            {
                sb.Append(remaining);
                break;
            }

            var candidateText = remaining.Slice(0, Math.Min(maxLineLength - indent.Length + 1, remaining.Length));
            var linebreak = candidateText.IndexOf('\n');
            if (candidateText.IndexOf('\n') is int linebreakPos && linebreakPos >= 0)
            {
                sb.Append(candidateText.Slice(0, linebreakPos));
                remaining = remaining.Slice(linebreakPos + 1);
            }
            else if (candidateText.LastIndexOf(' ') is int lastSpacePos && lastSpacePos >= 0)
            {
                sb.Append(candidateText.Slice(0, lastSpacePos));
                remaining = remaining.Slice(lastSpacePos + 1);
            }
            else
            {
                sb.Append(candidateText.Slice(0, maxLineLength - indent.Length));
                remaining = remaining.Slice(maxLineLength - indent.Length);
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }
}
