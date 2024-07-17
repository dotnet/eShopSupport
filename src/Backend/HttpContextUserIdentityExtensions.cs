using System.Security.Claims;

namespace CustomerWebUI;

public static class HttpContextUserIdentityExtensions
{
    public static int GetCustomerId(this HttpContext httpContext)
    {
        if (httpContext.User.IsInRole("staff"))
        {
            throw new InvalidOperationException("The current user is not a customer; they are in 'staff' role.");
        }

        if (httpContext.User.Identity is { IsAuthenticated: true } and ClaimsIdentity claimsIdentity
            && claimsIdentity.FindFirst("sub") is { Value: string subscriberIdString })
        {
            return int.Parse(subscriberIdString);
        }

        throw new InvalidOperationException("User is not authenticated or missing 'sub' claim");
    }
}
