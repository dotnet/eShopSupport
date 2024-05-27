using System.Security.Claims;

namespace CustomerWebUI;

public static class HttpContextUserIdentityExtensions
{
    public static int? GetCustomerId(this HttpContext httpContext) =>
        httpContext.User.Identity is { IsAuthenticated: true } and ClaimsIdentity claimsIdentity
        && claimsIdentity.FindFirst("sub") is { Value: string subscriberIdString }
        ? int.Parse(subscriberIdString)
        : null;
}
