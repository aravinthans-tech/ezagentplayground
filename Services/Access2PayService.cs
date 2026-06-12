using System.Text;
using System.Text.Json;
using QRCodeAPI.Models;

namespace QRCodeAPI.Services;

/// <summary>
/// Proxies Ezofis Access2Pay external APIs.
/// Token header is hardcoded to "tenantId 2" (no Bearer JWT).
/// Callers only need X-API-Key.
/// </summary>
public class Access2PayService
{
    private const string DefaultTokenHeader = "tenantId 2";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<Access2PayService> _logger;
    private readonly string _ezofisBaseUrl;
    private readonly string _tokenHeader;

    public Access2PayService(
        IHttpClientFactory httpClientFactory,
        ILogger<Access2PayService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _ezofisBaseUrl = (configuration["ExternalApis:Ezofis:BaseUrl"] ?? "https://eztapi.ezofis.com").TrimEnd('/');
        _tokenHeader = configuration["ExternalApis:Access2Pay:TokenHeader"] ?? DefaultTokenHeader;
    }

    public async Task<ResultForHttpsCode> ConnectorInsertAsync(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return Error("Request body is required");

        var url = $"{_ezofisBaseUrl}/api/external/access2payConnectorInsert";
        var innerJson = payload.ValueKind == JsonValueKind.String
            ? payload.GetString() ?? ""
            : payload.GetRawText();
        if (string.IsNullOrWhiteSpace(innerJson))
            return Error("Request body is required");

        var wireBody = JsonSerializer.Serialize(innerJson);
        return await SendAsync(HttpMethod.Post, url, wireBody, "application/json");
    }

    public Task<ResultForHttpsCode> GetAsync(JsonElement query)
    {
        if (query.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return Task.FromResult(Error("Request body is required"));

        var url = $"{_ezofisBaseUrl}/api/external/access2payGet";
        return SendAsync(HttpMethod.Post, url, query.GetRawText(), "application/json");
    }

    public async Task<ResultForHttpsCode> UpdateAsync(string id, string body)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Error("id is required");
        if (body == null)
            return Error("Request body is required");

        var url = $"{_ezofisBaseUrl}/api/external/access2payUpdate/{Uri.EscapeDataString(id.Trim())}";
        var content = JsonSerializer.Serialize(body);
        return await SendAsync(HttpMethod.Put, url, content, "application/json");
    }

    private async Task<ResultForHttpsCode> SendAsync(HttpMethod method, string url, string? body, string? contentType)
    {
        var result = new ResultForHttpsCode();

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(method, url);
            request.Headers.TryAddWithoutValidation("Token", _tokenHeader);
            request.Headers.TryAddWithoutValidation("Accept", "*/*");

            if (!string.IsNullOrEmpty(body) && contentType != null)
                request.Content = new StringContent(body, Encoding.UTF8, contentType);

            var response = await httpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                result.id = 0;
                result.EncryptOutput = $"Access2Pay error {(int)response.StatusCode}: {text}";
                return result;
            }

            result.id = 1;
            result.output = text;
            result.EncryptOutput = null;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Access2Pay request failed for {Url}", url);
            result.id = 0;
            result.EncryptOutput = "ERROR: " + ex.Message;
            return result;
        }
    }

    private static ResultForHttpsCode Error(string message) => new()
    {
        id = 0,
        EncryptOutput = message
    };
}
