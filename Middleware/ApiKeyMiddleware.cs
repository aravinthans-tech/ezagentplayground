using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace QRCodeAPI.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiKeyMiddleware> _logger;
    private const string API_KEY_HEADER = "X-API-Key";

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        
        // Skip API key check for playground and static files
        if (path.StartsWith("/index.html") ||
            path.StartsWith("/apikey.html") ||
            path.StartsWith("/examples.html") ||
            path.StartsWith("/filesummary.html") ||
            path.StartsWith("/kycagent.html") ||
            (path.StartsWith("/") && !path.StartsWith("/api")))
        {
            await _next(context);
            return;
        }

        // Skip API key check for API key generation endpoints (users need to generate API key first)
        if (path.StartsWith("/api/Client/apiKey") ||
            path.StartsWith("/api/ApiKey/apiKey"))
        {
            await _next(context);
            return;
        }

        // Check for API key in header (required for all other API endpoints)
        if (!context.Request.Headers.TryGetValue(API_KEY_HEADER, out var extractedApiKey))
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"message\":\"API Key was not provided. Please provide X-API-Key header.\"}");
            return;
        }

        // Validate API key against database table
        var apiKey = extractedApiKey.ToString();
        if (!await IsValidApiKeyAsync(apiKey))
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"message\":\"Invalid API Key.\"}");
            return;
        }

        await _next(context);
    }

    private async Task<bool> IsValidApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("API key is null or empty");
            return false;
        }

        try
        {
            // Get connection string from configuration
            string connectionString = _configuration.GetConnectionString("eZApiTenantContext") ?? "";

            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("Connection string 'eZApiTenantContext' not found in configuration. API key validation failed.");
                return false;
            }

            // Check if API key exists in database table
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                _logger.LogDebug("Database connection opened for API key validation");

                using (SqlCommand cmd = new SqlCommand(
                    "SELECT COUNT(1) FROM tenantuserApiKey WHERE apikey = @apikey",
                    connection))
                {
                    cmd.Parameters.AddWithValue("@apikey", apiKey);

                    var result = await cmd.ExecuteScalarAsync();
                    var count = Convert.ToInt32(result ?? 0);

                    bool isValid = count > 0;
                    if (isValid)
                    {
                        _logger.LogInformation("API key validated successfully from database");
                    }
                    else
                    {
                        _logger.LogWarning("API key not found in database. Count: {Count}", count);
                    }
                    
                    return isValid;
                }
            }
        }
        catch (SqlException sqlEx)
        {
            _logger.LogError(sqlEx, "SQL error validating API key from database. Rejecting API key.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating API key from database. Rejecting API key.");
            return false;
        }
    }
}

