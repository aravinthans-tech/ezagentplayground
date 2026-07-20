using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using V6Playground.Configuration;

namespace V6Playground.Services;

public sealed class V6ApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly V6ApiOptions _options;
    private readonly ILogger<V6ApiClient> _logger;

    public V6ApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<V6ApiOptions> options,
        ILogger<V6ApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string BaseUrl => _options.BaseUrl.TrimEnd('/');

    public async Task<TenantLookupResponse> LookupTenantsAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        var encodedEmail = Uri.EscapeDataString(email.Trim());
        using var response = await client.GetAsync($"{BaseUrl}/api/auth/tenants?email={encodedEmail}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(body) ?? $"Tenant lookup failed ({(int)response.StatusCode}).";
            throw new V6ApiException((int)response.StatusCode, error);
        }

        return JsonSerializer.Deserialize<TenantLookupResponse>(body, JsonOptions)
            ?? new TenantLookupResponse(Array.Empty<TenantLookupItem>(), false);
    }

    public async Task<IReadOnlyList<TenantEmailItem>> ListEmailsAsync(
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        var url = $"{BaseUrl}/api/auth/emails";
        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            url += $"?tenantId={tenantId.Value}";

        using var response = await client.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(body) ?? $"Email list failed ({(int)response.StatusCode}).";
            throw new V6ApiException((int)response.StatusCode, error);
        }

        var data = JsonSerializer.Deserialize<TenantEmailListResponse>(body, JsonOptions);
        return data?.Emails ?? Array.Empty<TenantEmailItem>();
    }

    public async Task<V6LoginResult> LoginAsync(
        string email,
        string password,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/auth/ezofis/login");
        request.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantId.ToString());
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { email = email.Trim(), password }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(body) ?? $"Login failed ({(int)response.StatusCode}).";
            _logger.LogWarning("V6 login failed for {Email}: {Error}", email, error);
            throw new V6ApiException((int)response.StatusCode, error);
        }

        var login = JsonSerializer.Deserialize<V6LoginResponse>(body, JsonOptions)
            ?? throw new V6ApiException(500, "Invalid login response from V6 API.");

        if (string.IsNullOrWhiteSpace(login.AccessToken))
            throw new V6ApiException(500, "V6 API did not return an access token.");

        return new V6LoginResult(
            login.UserId,
            login.AccessToken,
            login.TokenType ?? "Bearer",
            login.ExpiresIn > 0 ? login.ExpiresIn : 3600);
    }

    /// <summary>Social login for Google / Microsoft users (no password).</summary>
    public async Task<V6LoginResult> SocialLoginAsync(
        string email,
        string provider,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeSocialProvider(provider);
        if (normalized is null)
            throw new V6ApiException(400, "Provider must be google or microsoft.");

        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/auth/social/login");
        request.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantId.ToString());
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { email = email.Trim(), provider = normalized }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(body) ?? $"Social login failed ({(int)response.StatusCode}).";
            _logger.LogWarning("V6 social login failed for {Email}/{Provider}: {Error}", email, normalized, error);
            throw new V6ApiException((int)response.StatusCode, error);
        }

        var login = JsonSerializer.Deserialize<V6LoginResponse>(body, JsonOptions)
            ?? throw new V6ApiException(500, "Invalid social login response from V6 API.");

        if (string.IsNullOrWhiteSpace(login.AccessToken))
            throw new V6ApiException(500, "V6 API did not return an access token.");

        return new V6LoginResult(
            login.UserId,
            login.AccessToken,
            login.TokenType ?? "Bearer",
            login.ExpiresIn > 0 ? login.ExpiresIn : 3600);
    }

    public static string? NormalizeSocialProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider)) return null;
        var value = provider.Trim();
        if (value.Equals("google", StringComparison.OrdinalIgnoreCase))
            return "google";
        if (value.Equals("microsoft", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("office365", StringComparison.OrdinalIgnoreCase))
            return "microsoft";
        return null;
    }

    public async Task<HttpProxyResult> SendAsync(
        HttpMethod method,
        string relativePath,
        string accessToken,
        string? jsonBody = null,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        var path = relativePath.StartsWith('/') ? relativePath : "/" + relativePath;
        using var request = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            request.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (!string.IsNullOrWhiteSpace(jsonBody))
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/json";

        return new HttpProxyResult((int)response.StatusCode, body, contentType);
    }

    public async Task<HttpProxyResult> SendMultipartAsync(
        string relativePath,
        string accessToken,
        string? context,
        string? envType,
        string? fileName,
        string? contentType,
        byte[]? fileBytes,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        var path = relativePath.StartsWith('/') ? relativePath : "/" + relativePath;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{path}");
        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            request.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var form = new MultipartFormDataContent();
        if (!string.IsNullOrWhiteSpace(context))
            form.Add(new StringContent(context), "context");
        if (!string.IsNullOrWhiteSpace(envType))
            form.Add(new StringContent(envType), "envType");
        if (fileBytes is { Length: > 0 } && !string.IsNullOrWhiteSpace(fileName))
        {
            var fileContent = new ByteArrayContent(fileBytes);
            if (!string.IsNullOrWhiteSpace(contentType))
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(fileContent, "file", fileName);
        }

        request.Content = form;

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseContentType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
        return new HttpProxyResult((int)response.StatusCode, body, responseContentType);
    }

    private static string? TryReadError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.GetString();
        }
        catch
        {
            // ignore
        }

        return string.IsNullOrWhiteSpace(body) ? null : body;
    }
}

public sealed class V6LoginResponse
{
    public Guid UserId { get; set; }
    public string? AccessToken { get; set; }
    public string? TokenType { get; set; }
    public int ExpiresIn { get; set; }
}

public sealed record V6LoginResult(Guid UserId, string AccessToken, string TokenType, int ExpiresIn);

public sealed record TenantLookupResponse(IReadOnlyList<TenantLookupItem> Tenants, bool RequiresOrgSelection = false);

public sealed record TenantLookupItem(Guid TenantId, string Name, string Role);

public sealed record TenantEmailListResponse(Guid? TenantId, IReadOnlyList<TenantEmailItem> Emails);

public sealed record TenantEmailItem(
    string Email,
    string DisplayName,
    string Role,
    Guid TenantId,
    string TenantName);

public sealed record HttpProxyResult(int StatusCode, string Body, string ContentType);

public sealed class V6ApiException : Exception
{
    public int StatusCode { get; }

    public V6ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
