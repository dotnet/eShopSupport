using eShopSupport.ServiceDefaults.Clients.Backend;

namespace eShopSupport.Backend.Api;

public static class Typeahead
{
    public static void MapTypeaheadEndpoints(this WebApplication app)
    {
        app.MapPost("/api/typeahead", (TypeaheadRequest request, CancellationToken cancellationToken) =>
        {
            return "Hello from the backend";
        });
    }
}
