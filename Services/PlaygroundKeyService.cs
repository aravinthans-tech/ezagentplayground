using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using V6Playground.Configuration;

namespace V6Playground.Services;

public sealed class PlaygroundKeyService
{
    public const string SocialCredentialPrefix = "social:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly V6ApiOptions _options;
    private readonly ILogger<PlaygroundKeyService> _logger;

    public PlaygroundKeyService(
        IHttpClientFactory httpClientFactory,
        IOptions<V6ApiOptions> options,
        ILogger<PlaygroundKeyService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    private string BaseUrl => _options.BaseUrl.TrimEnd('/');

    public async Task<PlaygroundKeyRecord?> GenerateAsync(
        string email,
        string? password,
        Guid tenantId,
        string? tenantName,
        string accessToken,
        DateTime? accessTokenExpiresAtUtc,
        int daysValid,
        string? keyLabel,
        string? socialProvider = null,
        CancellationToken cancellationToken = default)
    {
        var key = "pg_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var now = DateTime.UtcNow;
        var expiresAt = daysValid > 0 ? now.AddDays(daysValid) : (DateTime?)null;

        string? protectedCredential = null;
        var normalizedProvider = V6ApiClient.NormalizeSocialProvider(socialProvider);
        if (normalizedProvider != null)
            protectedCredential = Protect(SocialCredentialPrefix + normalizedProvider);
        else if (!string.IsNullOrWhiteSpace(password))
            protectedCredential = Protect(password);

        var payload = new
        {
            email = email.Trim(),
            apiKey = key,
            keyLabel = string.IsNullOrWhiteSpace(keyLabel)
                ? (normalizedProvider != null ? $"Playground key ({normalizedProvider})" : "Playground key")
                : keyLabel.Trim(),
            protectedPassword = protectedCredential,
            expiresAtUtc = expiresAt
        };

        var created = await SendJsonAsync<RemoteKeyDto>(
            HttpMethod.Post,
            "/api/playground/api-keys",
            tenantId,
            payload,
            cancellationToken);

        if (created == null)
            return null;

        return MapRecord(created, tenantId, tenantName, accessToken, accessTokenExpiresAtUtc);
    }

    public async Task<PlaygroundKeyValidation> ValidateAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new PlaygroundKeyValidation(false, "API Key is empty.", null);

        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{BaseUrl}/api/playground/api-keys/by-key?apiKey={Uri.EscapeDataString(apiKey.Trim())}");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new PlaygroundKeyValidation(false, "Invalid API Key.", null);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return new PlaygroundKeyValidation(false, TryReadMessage(body) ?? "API Key has expired.", null);

        if (!response.IsSuccessStatusCode)
            return new PlaygroundKeyValidation(false, TryReadMessage(body) ?? "API key validation failed.", null);

        var remote = JsonSerializer.Deserialize<RemoteKeyDto>(body, JsonOptions);
        if (remote == null || string.IsNullOrWhiteSpace(remote.ApiKey))
            return new PlaygroundKeyValidation(false, "Invalid API Key.", null);

        return new PlaygroundKeyValidation(true, null, MapRecord(remote, remote.TenantId, null, null, null));
    }

    public async Task<PlaygroundKeyRecord?> GetLatestForEmailAndTenantAsync(
        string email,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var keys = await ListForEmailAsync(email, tenantId, cancellationToken);
        return keys
            .Where(k => !k.ExpiresAtUtc.HasValue || k.ExpiresAtUtc.Value > DateTime.UtcNow)
            .OrderByDescending(k => k.CreatedAtUtc)
            .FirstOrDefault();
    }

    public Task UpdateAccessTokenAsync(
        string apiKey,
        string accessToken,
        DateTime? accessTokenExpiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>Returns password for Ezofis users, or social provider name for Google/Microsoft keys.</summary>
    public PlaygroundAuthCredentials GetCredentials(PlaygroundKeyRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.ProtectedPassword))
            return new PlaygroundAuthCredentials(null, null);

        try
        {
            var raw = Unprotect(record.ProtectedPassword);
            if (raw.StartsWith(SocialCredentialPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var provider = V6ApiClient.NormalizeSocialProvider(raw[SocialCredentialPrefix.Length..]);
                return new PlaygroundAuthCredentials(null, provider);
            }

            return new PlaygroundAuthCredentials(raw, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unprotect credentials for API key {ApiKeyId}", record.Id);
            return new PlaygroundAuthCredentials(null, null);
        }
    }

    public string? GetPassword(PlaygroundKeyRecord record) => GetCredentials(record).Password;

    public async Task<List<PlaygroundKeyRecord>> ListForEmailAsync(
        string email,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
            return new List<PlaygroundKeyRecord>();

        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{BaseUrl}/api/playground/api-keys?email={Uri.EscapeDataString(email.Trim())}");
        request.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantId.Value.ToString());

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to list playground keys: {Body}", body);
            return new List<PlaygroundKeyRecord>();
        }

        var data = JsonSerializer.Deserialize<RemoteKeyListResponse>(body, JsonOptions);
        return data?.Keys?.Select(k => MapRecord(k, tenantId.Value, null, null, null)).ToList()
            ?? new List<PlaygroundKeyRecord>();
    }

    public async Task RecordUsageAsync(
        PlaygroundKeyRecord record,
        string method,
        string endpoint,
        int statusCode,
        long durationMs,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            apiKeyId = record.Id,
            apiKey = record.ApiKey,
            email = record.Email,
            endpoint,
            httpMethod = method,
            statusCode,
            durationMs
        };

        await SendJsonAsync<object>(
            HttpMethod.Post,
            "/api/playground/api-usage",
            record.TenantId,
            payload,
            cancellationToken);
    }

    public async Task<PlaygroundUsageSummary> GetUsageSummaryAsync(
        string email,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
            return new PlaygroundUsageSummary();

        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{BaseUrl}/api/playground/api-usage?email={Uri.EscapeDataString(email.Trim())}");
        request.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantId.Value.ToString());

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to load playground usage: {Body}", body);
            return new PlaygroundUsageSummary { TenantId = tenantId };
        }

        return JsonSerializer.Deserialize<PlaygroundUsageSummary>(body, JsonOptions)
            ?? new PlaygroundUsageSummary { TenantId = tenantId };
    }

    private async Task<T?> SendJsonAsync<T>(
        HttpMethod method,
        string relativePath,
        Guid tenantId,
        object? payload,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        var path = relativePath.StartsWith('/') ? relativePath : "/" + relativePath;
        using var request = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        request.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantId.ToString());
        if (payload != null)
            request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("V6 playground API call failed {Method} {Path}: {Body}", method, path, body);
            return default;
        }

        if (typeof(T) == typeof(object))
            return default;

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private static PlaygroundKeyRecord MapRecord(
        RemoteKeyDto remote,
        Guid tenantId,
        string? tenantName,
        string? accessToken,
        DateTime? accessTokenExpiresAtUtc)
    {
        return new PlaygroundKeyRecord
        {
            Id = remote.Id,
            ApiKey = remote.ApiKey,
            Email = remote.Email,
            ProtectedPassword = remote.ProtectedPassword,
            TenantId = remote.TenantId != Guid.Empty ? remote.TenantId : tenantId,
            TenantName = tenantName,
            AccessToken = accessToken,
            AccessTokenExpiresAtUtc = accessTokenExpiresAtUtc,
            Label = remote.KeyLabel ?? "Playground key",
            CreatedAtUtc = remote.CreatedAtUtc,
            ExpiresAtUtc = remote.ExpiresAtUtc,
            IsActive = remote.IsActive
        };
    }

    private static string? TryReadMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.GetString();
            if (doc.RootElement.TryGetProperty("message", out var msg))
                return msg.GetString();
        }
        catch
        {
            // ignore
        }

        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    private static string Protect(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string Unprotect(string value) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private sealed class RemoteKeyListResponse
    {
        public List<RemoteKeyDto>? Keys { get; set; }
    }

    private sealed class RemoteKeyDto
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Email { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string? KeyLabel { get; set; }
        public string? ProtectedPassword { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public bool IsActive { get; set; }
        public bool IsExpired { get; set; }
    }
}

public sealed record PlaygroundAuthCredentials(string? Password, string? SocialProvider);

public sealed class PlaygroundKeyRecord
{
    public Guid Id { get; set; }
    public string ApiKey { get; set; } = "";
    public string Email { get; set; } = "";
    public string? ProtectedPassword { get; set; }
    public Guid TenantId { get; set; }
    public string? TenantName { get; set; }
    public string? AccessToken { get; set; }
    public DateTime? AccessTokenExpiresAtUtc { get; set; }
    public string Label { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; }
}

public sealed record PlaygroundKeyValidation(bool Valid, string? Message, PlaygroundKeyRecord? Record);

public sealed class PlaygroundApiUsageRecord
{
    public Guid Id { get; set; }
    public Guid ApiKeyId { get; set; }
    public string ApiKey { get; set; } = "";
    public string Email { get; set; } = "";
    public Guid TenantId { get; set; }
    public string? TenantName { get; set; }
    public string Method { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
    public DateTime RequestedAtUtc { get; set; }
}

public sealed class PlaygroundUsageSummary
{
    public Guid? TenantId { get; set; }
    public string? TenantName { get; set; }
    public int TotalKeys { get; set; }
    public int ActiveKeys { get; set; }
    public int ExpiredKeys { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public List<PlaygroundApiUsageRecord> RecentRequests { get; set; } = new();
}
