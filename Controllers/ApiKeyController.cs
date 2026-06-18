using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QRCodeAPI.Configuration;
using QRCodeAPI.Models;
using QRCodeAPI.Services;

namespace QRCodeAPI.Controllers;

[ApiController]
[Route("api/Client")]
public class ApiKeyController : ControllerBase
{
    private readonly ApiKeyService _apiKeyService;
    private readonly ILogger<ApiKeyController> _logger;
    private readonly PlaygroundDemoOptions _demoOptions;

    public ApiKeyController(
        ApiKeyService apiKeyService,
        ILogger<ApiKeyController> logger,
        IOptions<PlaygroundDemoOptions> demoOptions)
    {
        _apiKeyService = apiKeyService;
        _logger = logger;
        _demoOptions = demoOptions.Value;
    }

    private (string Email, string? Password)? GetDemoConfig()
    {
        if (!_demoOptions.Enabled || string.IsNullOrWhiteSpace(_demoOptions.UserEmail))
            return null;

        var email = _demoOptions.UserEmail.Trim();
        var password = string.IsNullOrWhiteSpace(_demoOptions.UserPassword)
            ? null
            : _demoOptions.UserPassword;
        return (email, password);
    }

    /// <summary>Create a new API key (always inserts a new row).</summary>
    [HttpPost("apiKey")]
    public async Task<IActionResult> PostGenerateApikey(
        [FromQuery] string userName,
        [FromQuery] string password,
        [FromQuery] int daysValid = 0,
        [FromQuery] string? keyLabel = null)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            return BadRequest("UserName and Password are required");

        try
        {
            var result = await _apiKeyService.GenerateApiKeyAsync(userName, password, daysValid, keyLabel);
            if (result == null)
                return BadRequest("Tenant is not created so please create tenant");

            return Ok(new
            {
                tenantId = result.TenantId,
                token = result.Token,
                apiKey = result.ApiKey,
                apiKeyId = result.ApiKeyId,
                expiresAt = result.ExpiresAt,
                isNew = result.IsNew
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating API key");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    /// <summary>Get the most recently created API key for this user.</summary>
    [HttpGet("apiKey")]
    public async Task<IActionResult> GetApiKey([FromQuery] string userName, [FromQuery] string password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            return BadRequest("UserName and Password are required");

        try
        {
            var result = await _apiKeyService.GetLatestApiKeyAsync(userName, password);
            if (result == null)
                return NotFound("No active API key found for this user. Generate a new key to continue.");

            return Ok(new
            {
                tenantId = result.TenantId,
                token = result.Token,
                apiKey = result.ApiKey,
                apiKeyId = result.ApiKeyId,
                expiresAt = result.ExpiresAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving API key");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    /// <summary>List all API keys for this user with call counts.</summary>
    [HttpGet("apiKeys")]
    public async Task<IActionResult> ListApiKeys([FromQuery] string userName, [FromQuery] string password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            return BadRequest("UserName and Password are required");

        try
        {
            var keys = await _apiKeyService.ListApiKeysAsync(userName, password);
            return Ok(new
            {
                totalKeys = keys.Count,
                keys = keys.Select(k => new
                {
                    id = k.Id,
                    apiKey = MaskKey(k.ApiKey),
                    apiKeyFull = k.ApiKey,
                    keyLabel = k.KeyLabel,
                    tenantId = k.TenantId,
                    isEnabled = k.IsEnabled,
                    isExpired = k.IsExpired,
                    expiresAt = k.ExpiresAt,
                    createdAt = k.CreatedAt,
                    totalCalls = k.TotalCalls
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing API keys");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    /// <summary>Enable or disable an API key.</summary>
    [HttpPatch("apiKey/{id:int}/enabled")]
    public async Task<IActionResult> SetApiKeyEnabled(
        int id,
        [FromQuery] string userName,
        [FromQuery] string password,
        [FromQuery] bool enabled = true)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            return BadRequest("UserName and Password are required");

        try
        {
            var ok = await _apiKeyService.SetApiKeyEnabledAsync(userName, password, id, enabled);
            if (!ok) return NotFound("API key not found for this user");
            return Ok(new { id, enabled, message = enabled ? "API key enabled." : "API key disabled." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating API key status");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    /// <summary>Delete an API key and its usage history.</summary>
    [HttpDelete("apiKey/{id:int}")]
    public async Task<IActionResult> DeleteApiKey(
        int id,
        [FromQuery] string userName,
        [FromQuery] string password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            return BadRequest("UserName and Password are required");

        try
        {
            var ok = await _apiKeyService.DeleteApiKeyAsync(userName, password, id);
            if (!ok) return NotFound("API key not found for this user");
            return Ok(new { id, message = "API key deleted." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting API key");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    /// <summary>Usage history for this user's API keys.</summary>
    [HttpGet("apiKey/usage")]
    public async Task<IActionResult> GetApiKeyUsage(
        [FromQuery] string userName,
        [FromQuery] string password,
        [FromQuery] int? apiKeyId = null,
        [FromQuery] int days = 30)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            return BadRequest("UserName and Password are required");

        try
        {
            var logs = await _apiKeyService.GetUsageLogsAsync(userName, password, apiKeyId, days);
            var summary = await _apiKeyService.GetUsageSummaryByFunctionAsync(userName, password, apiKeyId, days);
            return Ok(new
            {
                apiKeyId,
                days,
                totalCalls = logs.Count,
                byFunction = summary,
                logs = logs.Select(l => new
                {
                    l.Id,
                    l.ApiKeyId,
                    l.FunctionName,
                    l.Endpoint,
                    httpMethod = l.HttpMethod,
                    statusCode = l.StatusCode,
                    latencyMs = l.LatencyMs,
                    clientIp = l.ClientIp,
                    calledAt = l.CalledAt
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving API key usage");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    /// <summary>Demo playground: whether auto-load is configured (no password returned).</summary>
    [HttpGet("playground-demo/info")]
    public IActionResult GetPlaygroundDemoInfo()
    {
        var config = GetDemoConfig();
        return Ok(new
        {
            enabled = config != null,
            email = config?.Email ?? _demoOptions.UserEmail ?? "seth@ezofis.com"
        });
    }

    [HttpGet("playground-demo/apiKeys")]
    public async Task<IActionResult> ListApiKeysDemo()
    {
        var config = GetDemoConfig();
        if (config == null)
            return NotFound(new { message = "Playground demo is disabled. Set PlaygroundDemo:Enabled to true in appsettings." });

        try
        {
            var keys = string.IsNullOrEmpty(config.Value.Password)
                ? await _apiKeyService.ListApiKeysByEmailAsync(config.Value.Email)
                : await _apiKeyService.ListApiKeysAsync(config.Value.Email, config.Value.Password);
            return Ok(new
            {
                totalKeys = keys.Count,
                keys = keys.Select(k => new
                {
                    id = k.Id,
                    apiKey = MaskKey(k.ApiKey),
                    apiKeyFull = k.ApiKey,
                    keyLabel = k.KeyLabel,
                    tenantId = k.TenantId,
                    isEnabled = k.IsEnabled,
                    isExpired = k.IsExpired,
                    expiresAt = k.ExpiresAt,
                    createdAt = k.CreatedAt,
                    totalCalls = k.TotalCalls
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing demo API keys");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpPatch("playground-demo/apiKey/{id:int}/enabled")]
    public async Task<IActionResult> SetApiKeyEnabledDemo(int id, [FromQuery] bool enabled = true)
    {
        var config = GetDemoConfig();
        if (config == null)
            return NotFound(new { message = "Playground demo is disabled." });

        try
        {
            var ok = string.IsNullOrEmpty(config.Value.Password)
                ? await _apiKeyService.SetApiKeyEnabledByEmailAsync(config.Value.Email, id, enabled)
                : await _apiKeyService.SetApiKeyEnabledAsync(config.Value.Email, config.Value.Password, id, enabled);
            if (!ok) return NotFound("API key not found for this user");
            return Ok(new { id, enabled, message = enabled ? "API key enabled." : "API key disabled." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating demo API key status");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpDelete("playground-demo/apiKey/{id:int}")]
    public async Task<IActionResult> DeleteApiKeyDemo(int id)
    {
        var config = GetDemoConfig();
        if (config == null)
            return NotFound(new { message = "Playground demo is disabled." });

        try
        {
            var ok = string.IsNullOrEmpty(config.Value.Password)
                ? await _apiKeyService.DeleteApiKeyByEmailAsync(config.Value.Email, id)
                : await _apiKeyService.DeleteApiKeyAsync(config.Value.Email, config.Value.Password, id);
            if (!ok) return NotFound("API key not found for this user");
            return Ok(new { id, message = "API key deleted." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting demo API key");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpGet("playground-demo/apiKey/usage")]
    public async Task<IActionResult> GetApiKeyUsageDemo([FromQuery] int? apiKeyId = null, [FromQuery] int days = 30)
    {
        var config = GetDemoConfig();
        if (config == null)
            return NotFound(new { message = "Playground demo is disabled." });

        try
        {
            IReadOnlyList<ApiUsageLogDto> logs;
            IReadOnlyList<ApiUsageSummaryDto> summary;
            if (string.IsNullOrEmpty(config.Value.Password))
            {
                logs = await _apiKeyService.GetUsageLogsByEmailAsync(config.Value.Email, apiKeyId, days);
                summary = await _apiKeyService.GetUsageSummaryByFunctionByEmailAsync(config.Value.Email, apiKeyId, days);
            }
            else
            {
                logs = await _apiKeyService.GetUsageLogsAsync(config.Value.Email, config.Value.Password, apiKeyId, days);
                summary = await _apiKeyService.GetUsageSummaryByFunctionAsync(config.Value.Email, config.Value.Password, apiKeyId, days);
            }
            return Ok(new
            {
                apiKeyId,
                days,
                totalCalls = logs.Count,
                byFunction = summary,
                logs = logs.Select(l => new
                {
                    l.Id,
                    l.ApiKeyId,
                    l.FunctionName,
                    l.Endpoint,
                    httpMethod = l.HttpMethod,
                    statusCode = l.StatusCode,
                    latencyMs = l.LatencyMs,
                    clientIp = l.ClientIp,
                    calledAt = l.CalledAt
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving demo API key usage");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length <= 8) return "••••••••";
        return key[..4] + "••••" + key[^4..];
    }
}
