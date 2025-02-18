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
        public override Task<ChatResponse> GetResponseAsync(IList<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            // Temporary workaround for an issue in CompleteAsync<T>. Although OpenAI models are happy to
            // receive system messages at the end of the conversation, it causes a lot of problems for
            // Llama 3. So replace the schema prompt role with User. We'll update CompleteAsync<T> to
            // do this natively in the next update.
            if (chatMessages.Count > 1
                && chatMessages.LastOrDefault() is { } lastMessage
                && lastMessage.Role == ChatRole.System
                && lastMessage.Text?.Contains("$schema") is true)
            {
                lastMessage.Role = ChatRole.User;
            }

            return base.GetResponseAsync(chatMessages, options, cancellationToken);
        }

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IList<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return options?.Tools is null or []
                ? base.GetStreamingResponseAsync(chatMessages, options, cancellationToken)
                : TreatNonstreamingAsStreaming(chatMessages, options, cancellationToken);
        }

        private async IAsyncEnumerable<ChatResponseUpdate> TreatNonstreamingAsStreaming(IList<ChatMessage> chatMessages, ChatOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var result = await GetResponseAsync(chatMessages, options, cancellationToken);
            for (var choiceIndex = 0; choiceIndex < result.Choices.Count; choiceIndex++)
            {
                var choice = result.Choices[choiceIndex];
                yield return new ChatResponseUpdate
                {
                    AuthorName = choice.AuthorName,
                    ChoiceIndex = choiceIndex,
                    ResponseId = result.ResponseId,
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
