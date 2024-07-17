using System.Security.Claims;

namespace CustomerWebUI;

public static class HttpContextUserIdentityExtensions
{
    public static int? GetCustomerIdIfNotStaff(this HttpContext httpContext)
    {
        if (httpContext.User.IsInRole("staff"))
        {
            return null;
        }

        if (httpContext.User.Identity is { IsAuthenticated: true } and ClaimsIdentity claimsIdentity
            && claimsIdentity.FindFirst("sub") is { Value: string subscriberIdString })
        {
            return int.Parse(subscriberIdString);
        }

        throw new InvalidOperationException("User is not authenticated or missing 'sub' claim");
    }
}
