using Duende.IdentityServer;
using Duende.IdentityServer.Models;

namespace IdentityServer;

public static class Config
{
    public static IEnumerable<IdentityResource> IdentityResources { get; } =
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
    ];

    public static IEnumerable<ApiScope> ApiScopes { get; } =
    [
        new ApiScope(name: "staff-api", displayName: "Staff API")
    ];

    public static IEnumerable<Client> GetClients(IConfiguration configuration) =>
    [
        new Client
        {
            ClientId = "staff-webui",
            ClientSecrets = { new Secret("staff-webui-secret".Sha256()) },

            AllowedGrantTypes = GrantTypes.Code,
            
            RedirectUris = { $"{configuration["StaffWebUIEndpoint"]}/signin-oidc" },

            // where to redirect to after logout
            PostLogoutRedirectUris = { $"{configuration["StaffWebUIEndpoint"]}/signout-callback-oidc" },

            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId,
                IdentityServerConstants.StandardScopes.Profile,
                "staff-api"
            }
        }
    ];
}
