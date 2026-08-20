using DnsWeaverApi.Configuration;

namespace DnsWeaverApi.Security;

/// <summary>
/// Authenticates the X-API-Key header and resolves which route (DNS zone
/// write, or a specific Sophos WAF rule) it's authorized for. The resolved
/// ApiKeyRoute is stashed in HttpContext.Items for endpoint handlers to
/// branch on — one key means exactly one mode, never both.
/// </summary>
public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    public ApiKeyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AuthOptions authOptions, IReadOnlyDictionary<string, ApiKeyRoute> apiKeys)
    {
        if (!context.Request.Headers.TryGetValue(authOptions.HeaderName, out var provided))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var matched = apiKeys.FirstOrDefault(kv => SecureCompare.Equals(provided.ToString(), kv.Key));
        if (matched.Key is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.Items["Route"] = matched.Value;
        await _next(context);
    }
}

public static class ApiKeyMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyAuth(this IApplicationBuilder app) =>
        app.UseMiddleware<ApiKeyMiddleware>();
}
