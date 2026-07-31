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

    /// <summary>
    /// Resolve ticket by referenceNo (reqNo), then call access2payUpdate/{id}.
    /// Body must include referenceNo; path id is not required from the client.
    /// </summary>
    public async Task<ResultForHttpsCode> UpdateByReferenceNoAsync(JsonElement body)
    {
        if (body.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return Error("Request body is required");

        string bodyText;
        JsonElement payloadEl;

        if (body.ValueKind == JsonValueKind.String)
        {
            bodyText = body.GetString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(bodyText))
                return Error("Request body is required");

            try
            {
                using var doc = JsonDocument.Parse(bodyText);
                payloadEl = doc.RootElement.Clone();
            }
            catch
            {
                return Error("Request body must be valid JSON containing referenceNo");
            }
        }
        else if (body.ValueKind == JsonValueKind.Object)
        {
            bodyText = body.GetRawText();
            payloadEl = body;
        }
        else
        {
            return Error("Request body must be a JSON object (or JSON string) with referenceNo");
        }

        var referenceNo = ReadStringProp(payloadEl, "referenceNo", "requestNo", "reqNo", "ReferenceNo");
        if (string.IsNullOrWhiteSpace(referenceNo))
            return Error("referenceNo is required in the request body");

        var lookupQuery = new
        {
            sortBy = new { criteria = "id", order = "DESC" },
            filterBy = new object[]
            {
                new
                {
                    groupCondition = "",
                    filters = new object[]
                    {
                        new
                        {
                            criteria = "referenceNo",
                            condition = "IS_EQUALS_TO",
                            value = referenceNo
                        }
                    }
                }
            },
            currentPage = 1,
            itemsPerPage = 1,
            mode = "browse"
        };

        var lookupJson = JsonSerializer.Serialize(lookupQuery);
        using var lookupDoc = JsonDocument.Parse(lookupJson);
        var lookup = await GetAsync(lookupDoc.RootElement);
        if (lookup.id == 0)
            return lookup;

        if (!TryExtractRecordId(lookup.output, out var recordId) || string.IsNullOrWhiteSpace(recordId))
            return Error($"No Access2Pay record found for referenceNo '{referenceNo}'");

        return await UpdateAsync(recordId, bodyText);
    }

    private static string? ReadStringProp(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        foreach (var name in names)
        {
            JsonElement el = default;
            var found = false;
            if (root.TryGetProperty(name, out el))
            {
                found = true;
            }
            else
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                    el = prop.Value;
                    found = true;
                    break;
                }
            }

            if (!found) continue;

            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
            else if (el.ValueKind == JsonValueKind.Number)
            {
                return el.GetRawText();
            }
        }

        return null;
    }

    private static bool TryExtractRecordId(string? getOutput, out string recordId)
    {
        recordId = "";
        if (string.IsNullOrWhiteSpace(getOutput)) return false;

        var text = getOutput.Trim();
        if (text.StartsWith('"') && text.EndsWith('"') && text.Length >= 2)
        {
            try { text = JsonSerializer.Deserialize<string>(text)?.Trim() ?? text; }
            catch { /* keep */ }
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (TryFindId(doc.RootElement, out recordId))
                return true;
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryFindId(JsonElement el, out string recordId)
    {
        recordId = "";

        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "id", "Id", "ID", "recordId" })
            {
                if (el.TryGetProperty(name, out var idEl))
                {
                    if (idEl.ValueKind == JsonValueKind.Number)
                    {
                        recordId = idEl.GetRawText();
                        return true;
                    }

                    if (idEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(idEl.GetString()))
                    {
                        recordId = idEl.GetString()!.Trim();
                        return true;
                    }
                }
            }

            foreach (var nest in new[] { "data", "items", "result", "results", "rows", "output", "records" })
            {
                if (el.TryGetProperty(nest, out var nested) && TryFindId(nested, out recordId))
                    return true;
            }

            foreach (var prop in el.EnumerateObject())
            {
                if (TryFindId(prop.Value, out recordId))
                    return true;
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (TryFindId(item, out recordId))
                    return true;
            }
        }

        return false;
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
