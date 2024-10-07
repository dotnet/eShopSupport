using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// This is used only with Ollama, because the current version of Ollama doesn't support streaming with tool calls.
/// To work around this Ollama limitation, if the call involves tools, we always resolve it using the non-streaming endpoint.
/// </summary>
public static class PreventStreamingWithFunctionsExtensions
{
    public static ChatClientBuilder UsePreventStreamingWithFunctions(this ChatClientBuilder builder)
    {
        return builder.Use(inner => new PreventStreamingWithFunctions(inner));
    }

    private class PreventStreamingWithFunctions(IChatClient innerClient) : DelegatingChatClient(innerClient)
    {
        public override IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteStreamingAsync(IList<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return options?.Tools is null or []
                ? base.CompleteStreamingAsync(chatMessages, options, cancellationToken)
                : TreatNonstreamingAsStreaming(chatMessages, options, cancellationToken);
        }

        private async IAsyncEnumerable<StreamingChatCompletionUpdate> TreatNonstreamingAsStreaming(IList<ChatMessage> chatMessages, ChatOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var result = await CompleteAsync(chatMessages, options, cancellationToken);
            for (var choiceIndex = 0; choiceIndex < result.Choices.Count; choiceIndex++)
            {
                var choice = result.Choices[choiceIndex];
                yield return new StreamingChatCompletionUpdate
                {
                    AuthorName = choice.AuthorName,
                    ChoiceIndex = choiceIndex,
                    CompletionId = result.CompletionId,
                    Contents = choice.Contents,
                    CreatedAt = result.CreatedAt,
                    FinishReason = result.FinishReason,
                    RawRepresentation = choice.RawRepresentation,
                    Role = choice.Role,
                    AdditionalProperties = result.AdditionalProperties,
                };
            }
        }
    }
}
