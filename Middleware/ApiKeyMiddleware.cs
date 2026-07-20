using System.Diagnostics;
using V6Playground.Services;

namespace V6Playground.Middleware;

public sealed class ApiKeyMiddleware
{
    public const string ValidatedKeyItemKey = "PlaygroundValidatedKey";
    private const string ApiKeyHeader = "X-API-Key";
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, PlaygroundKeyService keyService)
    {
        var path = context.Request.Path.Value ?? "";

        if (ShouldSkip(path))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var extracted))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"message\":\"API Key was not provided. Please provide X-API-Key header.\"}");
            return;
        }

        var validation = await keyService.ValidateAsync(extracted.ToString(), context.RequestAborted);
        if (!validation.Valid)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            var msg = (validation.Message ?? "Invalid API Key.").Replace("\"", "\\\"");
            await context.Response.WriteAsync($"{{\"message\":\"{msg}\"}}");
            return;
        }

        if (validation.Record != null)
            context.Items[ValidatedKeyItemKey] = validation.Record;

        var sw = Stopwatch.StartNew();
        await _next(context);
        sw.Stop();

        if (validation.Record != null)
        {
            _ = keyService.RecordUsageAsync(
                validation.Record,
                context.Request.Method,
                path,
                context.Response.StatusCode,
                sw.ElapsedMilliseconds,
                context.RequestAborted);
        }
    }

    private static bool ShouldSkip(string path)
    {
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWith("/api/Client/apiKey", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWith("/api/Client/token", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWith("/api/Client/info", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWith("/api/Client/tenants", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWith("/api/Client/apiKeys", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWith("/api/Client/apiUsage", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
