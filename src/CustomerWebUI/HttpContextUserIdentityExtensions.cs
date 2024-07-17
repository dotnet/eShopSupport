using System.Security.Claims;

namespace CustomerWebUI;

public static class HttpContextUserIdentityExtensions
{
    public static int? GetCustomerId(this HttpContext httpContext) =>
        throw new InvalidOperationException("Stop using this");
}
