using Duende.IdentityServer;
using Duende.IdentityServer.Models;

namespace IdentityServer;

public static class Config
{
    public static IEnumerable<IdentityResource> IdentityResources { get; } =
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResource("role", ["role"]),
    ];

    public static IEnumerable<ApiScope> ApiScopes { get; } =
    [
        new ApiScope(name: "staff-api", displayName: "Staff API", ["role"])
    ];

    public static IEnumerable<Client> GetClients(IConfiguration configuration) =>
    [
        new Client
        {
            // This is used by E2E test and evaluation
            ClientId = "dev-and-test-tools",
            ClientSecrets = { new Secret("dev-and-test-tools-secret".Sha256()) },
            AllowedGrantTypes = GrantTypes.ClientCredentials,
            AllowedScopes = { "staff-api" },
        },
        new Client
        {
            ClientId = "customer-webui",
            ClientSecrets = { new Secret("customer-webui-secret".Sha256()) },

            AllowedGrantTypes = GrantTypes.Code,

            RedirectUris = { $"{configuration["CustomerWebUIEndpoint"]}/signin-oidc" },
            PostLogoutRedirectUris = { $"{configuration["CustomerWebUIEndpoint"]}/signout-callback-oidc" },

            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId,
                IdentityServerConstants.StandardScopes.Profile,
            },
        },
        new Client
        {
            ClientId = "staff-webui",
            ClientSecrets = { new Secret("staff-webui-secret".Sha256()) },

            AllowedGrantTypes = GrantTypes.Code,

            RedirectUris = { $"{configuration["StaffWebUIEndpoint"]}/signin-oidc" },
            PostLogoutRedirectUris = { $"{configuration["StaffWebUIEndpoint"]}/signout-callback-oidc" },

            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId,
                IdentityServerConstants.StandardScopes.Profile,
                "staff-api",
                "role",
            },
        }
    ];
}
