using IdentityModel.Client;

namespace eShopSupport.ServiceDefaults.Clients.Backend;

public static class DevToolBackendClient
{
    /// <summary>
    /// Returns a <see cref="StaffBackendClient"/> that is pre-authenticated for use in development and testing tools.
    /// Do not use this in application code.
    /// </summary>
    public static async Task<StaffBackendClient> GetDevToolStaffBackendClientAsync(HttpClient identityServerHttpClient, HttpClient backendHttpClient)
    {
        var identityServerDisco = await identityServerHttpClient.GetDiscoveryDocumentAsync();
        if (identityServerDisco.IsError)
        {
            throw new InvalidOperationException(identityServerDisco.Error);
        }

        var tokenResponse = await identityServerHttpClient.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
        {
            Address = identityServerDisco.TokenEndpoint,
            ClientId = "dev-and-test-tools",
            ClientSecret = "dev-and-test-tools-secret",
            Scope = "staff-api"
        });

        backendHttpClient.SetBearerToken(tokenResponse.AccessToken!);
        return new StaffBackendClient(backendHttpClient);
    }
}
