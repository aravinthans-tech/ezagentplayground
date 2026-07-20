using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using V6Playground.Configuration;
using V6Playground.Services;

namespace V6Playground.Controllers;

[ApiController]
[Route("api/Client")]
public sealed class ApiKeyController : ControllerBase
{
    private readonly PlaygroundKeyService _keyService;
    private readonly V6ApiClient _v6Api;
    private readonly V6ApiOptions _v6Options;
    private readonly SocialAuthOptions _socialAuth;
    private readonly ILogger<ApiKeyController> _logger;

    public ApiKeyController(
        PlaygroundKeyService keyService,
        V6ApiClient v6Api,
        IOptions<V6ApiOptions> v6Options,
        IOptions<SocialAuthOptions> socialAuth,
        ILogger<ApiKeyController> logger)
    {
        _keyService = keyService;
        _v6Api = v6Api;
        _v6Options = v6Options.Value;
        _socialAuth = socialAuth.Value;
        _logger = logger;
    }

    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        return Ok(new
        {
            playgroundName = "API Playground",
            v6ApiBaseUrl = _v6Options.BaseUrl.TrimEnd('/'),
            storage = "hosted-v6-api",
            authModes = new[] { "password", "google", "microsoft" },
            socialAuth = new
            {
                googleClientId = _socialAuth.GoogleClientId,
                msalClientId = _socialAuth.MsalClientId
            }
        });
    }

    /// <summary>
    /// Login (password or Google/Microsoft social) and create a playground API key.
    /// Pass either password OR provider=google|microsoft.
    /// </summary>
    [HttpPost("apiKey")]
    public async Task<IActionResult> GenerateApiKey(
        [FromQuery] string userName,
        [FromQuery] string? password = null,
        [FromQuery] string? provider = null,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] int daysValid = 0,
        [FromQuery] string? keyLabel = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return BadRequest("UserName is required.");

        var socialProvider = V6ApiClient.NormalizeSocialProvider(provider);
        var useSocial = socialProvider != null;
        if (!useSocial && string.IsNullOrWhiteSpace(password))
            return BadRequest("Password is required for password login, or pass provider=google|microsoft for social login.");

        try
        {
            var resolvedTenant = await ResolveTenantAsync(userName, tenantId, cancellationToken);
            if (resolvedTenant is IActionResult errorResult)
                return errorResult;

            var tenant = ((TenantResolutionResult)resolvedTenant!).TenantId;
            var login = useSocial
                ? await _v6Api.SocialLoginAsync(userName, socialProvider!, tenant, cancellationToken)
                : await _v6Api.LoginAsync(userName, password!, tenant, cancellationToken);
            var tokenExpiresAt = DateTime.UtcNow.AddSeconds(login.ExpiresIn);

            var record = await _keyService.GenerateAsync(
                userName,
                useSocial ? null : password,
                tenant,
                ((TenantResolutionResult)resolvedTenant!).TenantName,
                login.AccessToken,
                tokenExpiresAt,
                daysValid,
                keyLabel,
                socialProvider,
                cancellationToken);

            if (record == null)
                return StatusCode(500, "Failed to create playground API key.");

            return Ok(new
            {
                tenantId = record.TenantId,
                tenantName = ((TenantResolutionResult)resolvedTenant!).TenantName,
                apiKey = record.ApiKey,
                apiKeyId = record.Id,
                expiresAt = record.ExpiresAtUtc,
                authMode = useSocial ? socialProvider : "password",
                isNew = true
            });
        }
        catch (V6ApiException ex)
        {
            return StatusCode(ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating API key");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    /// <summary>Re-login (password or social) and return the latest active playground API key.</summary>
    [HttpGet("apiKey")]
    public async Task<IActionResult> GetApiKey(
        [FromQuery] string userName,
        [FromQuery] string? password = null,
        [FromQuery] string? provider = null,
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return BadRequest("UserName is required.");

        var socialProvider = V6ApiClient.NormalizeSocialProvider(provider);
        var useSocial = socialProvider != null;
        if (!useSocial && string.IsNullOrWhiteSpace(password))
            return BadRequest("Password is required for password login, or pass provider=google|microsoft for social login.");

        try
        {
            var resolvedTenant = await ResolveTenantAsync(userName, tenantId, cancellationToken);
            if (resolvedTenant is IActionResult errorResult)
                return errorResult;

            var tenant = ((TenantResolutionResult)resolvedTenant!).TenantId;
            var login = useSocial
                ? await _v6Api.SocialLoginAsync(userName, socialProvider!, tenant, cancellationToken)
                : await _v6Api.LoginAsync(userName, password!, tenant, cancellationToken);
            var record = await _keyService.GetLatestForEmailAndTenantAsync(userName, tenant, cancellationToken);
            if (record == null)
                return NotFound("No active API key found. Generate a new key to continue.");

            var tokenExpiresAt = DateTime.UtcNow.AddSeconds(login.ExpiresIn);
            await _keyService.UpdateAccessTokenAsync(record.ApiKey, login.AccessToken, tokenExpiresAt, cancellationToken);

            return Ok(new
            {
                tenantId = record.TenantId,
                tenantName = ((TenantResolutionResult)resolvedTenant!).TenantName,
                apiKey = record.ApiKey,
                apiKeyId = record.Id,
                expiresAt = record.ExpiresAtUtc,
                authMode = useSocial ? socialProvider : "password"
            });
        }
        catch (V6ApiException ex)
        {
            return StatusCode(ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving API key");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    /// <summary>Generate access token only (password or social). No new API key.</summary>
    [HttpPost("token")]
    public async Task<IActionResult> GenerateToken(
        [FromQuery] string userName,
        [FromQuery] string? password = null,
        [FromQuery] string? provider = null,
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return BadRequest("UserName is required.");

        var socialProvider = V6ApiClient.NormalizeSocialProvider(provider);
        var useSocial = socialProvider != null;
        if (!useSocial && string.IsNullOrWhiteSpace(password))
            return BadRequest("Password is required for password login, or pass provider=google|microsoft for social login.");

        try
        {
            var resolvedTenant = await ResolveTenantAsync(userName, tenantId, cancellationToken);
            if (resolvedTenant is IActionResult errorResult)
                return errorResult;

            var tenant = ((TenantResolutionResult)resolvedTenant!).TenantId;
            var login = useSocial
                ? await _v6Api.SocialLoginAsync(userName, socialProvider!, tenant, cancellationToken)
                : await _v6Api.LoginAsync(userName, password!, tenant, cancellationToken);
            return Ok(new
            {
                tenantId = tenant,
                tenantName = ((TenantResolutionResult)resolvedTenant!).TenantName,
                userId = login.UserId,
                token = login.AccessToken,
                tokenType = login.TokenType,
                expiresIn = login.ExpiresIn,
                authMode = useSocial ? socialProvider : "password"
            });
        }
        catch (V6ApiException ex)
        {
            return StatusCode(ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating token");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpGet("apiKeys")]
    public async Task<IActionResult> ListApiKeys(
        [FromQuery] string userName,
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return BadRequest("UserName is required.");

        var keys = await _keyService.ListForEmailAsync(userName, tenantId, cancellationToken);
        return Ok(new
        {
            totalKeys = keys.Count,
            keys = keys.Select(k => new
            {
                id = k.Id,
                apiKey = MaskKey(k.ApiKey),
                apiKeyFull = k.ApiKey,
                keyLabel = k.Label,
                tenantId = k.TenantId,
                tenantName = k.TenantName,
                isExpired = k.ExpiresAtUtc.HasValue && k.ExpiresAtUtc.Value < DateTime.UtcNow,
                expiresAt = k.ExpiresAtUtc,
                createdAt = k.CreatedAtUtc
            })
        });
    }

    [HttpGet("apiUsage")]
    public async Task<IActionResult> GetApiUsage(
        [FromQuery] string userName,
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return BadRequest("UserName is required.");

        var summary = await _keyService.GetUsageSummaryAsync(userName, tenantId, cancellationToken);
        return Ok(new
        {
            summary.TenantId,
            tenantName = summary.TenantName,
            summary.TotalKeys,
            summary.ActiveKeys,
            summary.ExpiredKeys,
            summary.TotalRequests,
            summary.SuccessfulRequests,
            summary.FailedRequests,
            summary.LastUsedAtUtc,
            recentRequests = summary.RecentRequests.Select(r => new
            {
                r.Id,
                r.ApiKeyId,
                r.ApiKey,
                r.Email,
                endpoint = r.Endpoint,
                method = r.Method,
                r.StatusCode,
                durationMs = r.DurationMs,
                requestedAtUtc = r.RequestedAtUtc
            })
        });
    }

    [HttpGet("tenants")]
    public async Task<IActionResult> GetTenants(
        [FromQuery] string userName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return BadRequest("UserName is required.");

        var lookup = await _v6Api.LookupTenantsAsync(userName, cancellationToken);
        return Ok(lookup);
    }

    /// <summary>List system emails registered for a tenant (or all) for social account picker.</summary>
    [HttpGet("emails")]
    public async Task<IActionResult> ListEmails(
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var emails = await _v6Api.ListEmailsAsync(tenantId, cancellationToken);
            return Ok(new { tenantId, emails });
        }
        catch (V6ApiException ex)
        {
            return StatusCode(ex.StatusCode, ex.Message);
        }
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length <= 8) return "••••••••";
        return key[..4] + "••••" + key[^4..];
    }

    private async Task<object> ResolveTenantAsync(string userName, Guid? tenantId, CancellationToken cancellationToken)
    {
        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            return new TenantResolutionResult(tenantId.Value, null);

        var lookup = await _v6Api.LookupTenantsAsync(userName, cancellationToken);
        if (lookup.Tenants.Count == 0)
            return NotFound("No tenant found for this email.");

        if (lookup.Tenants.Count > 1)
        {
            return Conflict(new
            {
                message = "Multiple tenants found for this email. Select one tenant and try again.",
                requiresTenantSelection = true,
                tenants = lookup.Tenants.Select(t => new { tenantId = t.TenantId, name = t.Name, role = t.Role })
            });
        }

        var tenant = lookup.Tenants[0];
        return new TenantResolutionResult(tenant.TenantId, tenant.Name);
    }

    private sealed record TenantResolutionResult(Guid TenantId, string? TenantName);
}
