using Azure.Core;
using Azure.Core.Pipeline;
using Microsoft.Extensions.Azure;

internal static class SelfHostedOpenAiClientExtensions
{
    public static void AddAzureOpenAIClientWithSelfHosting(this IHostApplicationBuilder builder, string connectionName)
    {
        var connectionString = builder.Configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException($"Connection string '{connectionName}' not found.");

        // TODO: parse the connection string properly
        if (!connectionString.Contains("SelfHosted=true", StringComparison.OrdinalIgnoreCase))
        {
            // In the non-self-hosted case (OpenAI or Azure OpenAI), we don't need to change anything
            builder.AddAzureOpenAIClient(connectionName);
        }
        else
        {
            // For self-hosted, we have to change the transport
            Uri? endpoint = null; 
            builder.AddAzureOpenAIClient("openAiConnection",
                settings =>
                {
                    endpoint = settings.Endpoint;

                    // By having a key but no endpoint, we cause the underlying code to
                    // behave as if this is regular OpenAI (not Azure OpenAI), which matches
                    // what self-hosted LLMs will expect, e.g., Ollama
                    settings.Endpoint = null;
                    settings.Key = "ignored";
                },
                clientBuilder => clientBuilder.ConfigureOptions(clientOptions =>
                {
                    if (endpoint is null)
                    {
                        throw new InvalidOperationException("Endpoint not found in configuration.");
                    }

                    clientOptions.Transport = new SelfHostedLlmTransport(endpoint);
                }));
        }
    }

    private class SelfHostedLlmTransport(Uri endpoint) : HttpClientTransport
    {
        public override ValueTask ProcessAsync(HttpMessage message)
        {
            message.Request.Uri.Scheme = endpoint.Scheme;
            message.Request.Uri.Host = endpoint.Host;
            message.Request.Uri.Port = endpoint.Port;
            return base.ProcessAsync(message);
        }
    }
}
