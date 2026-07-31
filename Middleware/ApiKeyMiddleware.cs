using System.Diagnostics;
using QRCodeAPI.Services;

namespace QRCodeAPI.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;
    private const string API_KEY_HEADER = "X-API-Key";

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ApiKeyService apiKeyService)
    {
        var path = context.Request.Path.Value ?? "";

        if (ShouldSkipApiKeyCheck(path))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(API_KEY_HEADER, out var extractedApiKey))
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"message\":\"API Key was not provided. Please provide X-API-Key header.\"}");
            return;
        }

        var apiKey = extractedApiKey.ToString();
        var validation = await apiKeyService.ValidateApiKeyAsync(apiKey);
        if (!validation.Valid)
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            var msg = validation.Message ?? "Invalid API Key.";
            await context.Response.WriteAsync($"{{\"message\":\"{msg.Replace("\"", "\\\"")}\"}}");
            return;
        }

        var sw = Stopwatch.StartNew();
        await _next(context);
        sw.Stop();

        if (validation.ApiKeyId > 0)
        {
            var ip = context.Connection.RemoteIpAddress?.ToString();
            _ = apiKeyService.LogUsageAsync(
                validation.ApiKeyId,
                apiKey,
                path,
                context.Request.Method,
                context.Response.StatusCode,
                (int)sw.ElapsedMilliseconds,
                ip);
        }
    }

    private static bool ShouldSkipApiKeyCheck(string path)
    {
        if (path.StartsWith("/index.html") ||
            path.StartsWith("/apikey.html") ||
            path.StartsWith("/my-api-keys.html") ||
            path.StartsWith("/examples.html") ||
            path.StartsWith("/filesummary.html") ||
            path.StartsWith("/formdetails.html") ||
            path.StartsWith("/subformdetails.html") ||
            path.StartsWith("/subformfields.html") ||
            path.StartsWith("/subformsubmitarchive.html") ||
            path.StartsWith("/getdatafromsalesforce.html") ||
            path.StartsWith("/access2pay.html") ||
            path.StartsWith("/access2pay-documentation.html") ||
            path.StartsWith("/access2pay-documentation.md") ||
            path.StartsWith("/invoiceocr.html") ||
            path.StartsWith("/usage-report.html") ||
            path.StartsWith("/playground-documentation.html") ||
            path.StartsWith("/kycagent.html") ||
            path.StartsWith("/kyc-documentation.html") ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            (path.StartsWith("/") && !path.StartsWith("/api")))
        {
            return true;
        }

        if (path.StartsWith("/api/Client/apiKey", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/Client/apiKeys", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/Client/playground-demo", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/ApiKey/apiKey", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/Access2Pay/StorageCallback", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
