using Experimental.AI.LanguageModels;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Hosting;

internal class FunctionCallLogger(ILogger<FunctionCallLogger> functionCallLogger) : ChatMiddleware
{
    public override async Task ExecuteChatFunctionAsync(IChatHandler next, ChatToolCall toolCall, ChatOptions options)
    {
        functionCallLogger.LogWarning("Begin executing tool call {ToolCall}", toolCall.Name);
        await base.ExecuteChatFunctionAsync(next, toolCall, options);
        functionCallLogger.LogWarning("End executing tool call {ToolCall} with result {Result}", toolCall.Name, toolCall.Result);
    }
}
