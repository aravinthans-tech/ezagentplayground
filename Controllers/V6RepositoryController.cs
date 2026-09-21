using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using V6Playground.Services;

namespace V6Playground.Controllers;

/// <summary>
/// Repository wrappers for external playground customers (X-API-Key only).
/// </summary>
[ApiController]
[Route("api/v6")]
public sealed class V6RepositoryController : ControllerBase
{
    private readonly PlaygroundKeyService _keyService;
    private readonly V6ApiClient _v6Api;
    private readonly ILogger<V6RepositoryController> _logger;

    public V6RepositoryController(
        PlaygroundKeyService keyService,
        V6ApiClient v6Api,
        ILogger<V6RepositoryController> logger)
    {
        _keyService = keyService;
        _v6Api = v6Api;
        _logger = logger;
    }

    /// <summary>List repositories for the API key tenant (for Upload File dropdown).</summary>
    [HttpGet("repositories")]
    public async Task<IActionResult> ListRepositories(CancellationToken cancellationToken)
    {
        var validation = await GetValidatedKeyAsync(cancellationToken);
        if (validation.Record == null)
            return Unauthorized(new { message = validation.Message ?? "Invalid API Key." });

        var accessToken = await EnsureAccessTokenAsync(validation.Record, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            return Unauthorized(new { message = "Unable to generate access token for this API key." });

        try
        {
            var result = await _v6Api.SendAsync(
                HttpMethod.Get,
                "/api/repositories",
                accessToken,
                tenantId: validation.Record.TenantId,
                cancellationToken: cancellationToken);

            return new ContentResult
            {
                StatusCode = result.StatusCode,
                Content = result.Body,
                ContentType = result.ContentType
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list repositories");
            return StatusCode(502, new { error = "Failed to reach V6 API.", detail = ex.Message });
        }
    }

    /// <summary>Get one repository (includes field definitions for OCR).</summary>
    [HttpGet("repositories/{repositoryId:guid}")]
    public async Task<IActionResult> GetRepository(Guid repositoryId, CancellationToken cancellationToken)
    {
        var validation = await GetValidatedKeyAsync(cancellationToken);
        if (validation.Record == null)
            return Unauthorized(new { message = validation.Message ?? "Invalid API Key." });

        var accessToken = await EnsureAccessTokenAsync(validation.Record, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            return Unauthorized(new { message = "Unable to generate access token for this API key." });

        try
        {
            var result = await _v6Api.SendAsync(
                HttpMethod.Get,
                $"/api/repositories/{repositoryId}",
                accessToken,
                tenantId: validation.Record.TenantId,
                cancellationToken: cancellationToken);

            return new ContentResult
            {
                StatusCode = result.StatusCode,
                Content = result.Body,
                ContentType = result.ContentType
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get repository {RepositoryId}", repositoryId);
            return StatusCode(502, new { error = "Failed to reach V6 API.", detail = ex.Message });
        }
    }

    /// <summary>Filter field names for a repository.</summary>
    [HttpGet("repositories/{repositoryId:guid}/items/filter-fields")]
    public Task<IActionResult> GetFilterFields(Guid repositoryId, CancellationToken cancellationToken) =>
        ProxyV6Async(HttpMethod.Get, $"/api/repositories/{repositoryId}/items/filter-fields", null, cancellationToken);

    /// <summary>Available values for one filter field (e.g. Supplier list).</summary>
    [HttpGet("repositories/{repositoryId:guid}/items/facets/{fieldName}")]
    public Task<IActionResult> GetFacets(
        Guid repositoryId,
        string fieldName,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default) =>
        ProxyV6Async(
            HttpMethod.Get,
            $"/api/repositories/{repositoryId}/items/facets/{Uri.EscapeDataString(fieldName)}?limit={limit}",
            null,
            cancellationToken);

    /// <summary>List files matching selected filter values.</summary>
    [HttpPost("repositories/{repositoryId:guid}/items/query")]
    public async Task<IActionResult> QueryItems(
        Guid repositoryId,
        [FromBody] JsonElement body,
        CancellationToken cancellationToken)
    {
        var json = body.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? "{}"
            : body.GetRawText();
        return await ProxyV6Async(
            HttpMethod.Post,
            $"/api/repositories/{repositoryId}/items/query",
            json,
            cancellationToken);
    }

    /// <summary>List files in a repository (for Download File dropdown).</summary>
    [HttpGet("repositories/{repositoryId:guid}/items")]
    public async Task<IActionResult> ListItems(
        Guid repositoryId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var validation = await GetValidatedKeyAsync(cancellationToken);
        if (validation.Record == null)
            return Unauthorized(new { message = validation.Message ?? "Invalid API Key." });

        var accessToken = await EnsureAccessTokenAsync(validation.Record, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            return Unauthorized(new { message = "Unable to generate access token for this API key." });

        try
        {
            var result = await _v6Api.SendAsync(
                HttpMethod.Get,
                $"/api/repositories/{repositoryId}/items?page={page}&pageSize={pageSize}",
                accessToken,
                tenantId: validation.Record.TenantId,
                cancellationToken: cancellationToken);

            return new ContentResult
            {
                StatusCode = result.StatusCode,
                Content = result.Body,
                ContentType = result.ContentType
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list repository items {RepositoryId}", repositoryId);
            return StatusCode(502, new { error = "Failed to reach V6 API.", detail = ex.Message });
        }
    }

    /// <summary>Download a repository file. Wrapper for GET /api/repositories/{id}/items/{itemId}/file.</summary>
    [HttpGet("repositories/{repositoryId:guid}/items/{itemId:guid}/file")]
    public async Task<IActionResult> DownloadFile(
        Guid repositoryId,
        Guid itemId,
        [FromQuery] string disposition = "attachment",
        CancellationToken cancellationToken = default)
    {
        var validation = await GetValidatedKeyAsync(cancellationToken);
        if (validation.Record == null)
            return Unauthorized(new { message = validation.Message ?? "Invalid API Key." });

        var accessToken = await EnsureAccessTokenAsync(validation.Record, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            return Unauthorized(new { message = "Unable to generate access token for this API key." });

        var disp = string.Equals(disposition, "inline", StringComparison.OrdinalIgnoreCase) ? "inline" : "attachment";

        try
        {
            var result = await _v6Api.SendBytesAsync(
                HttpMethod.Get,
                $"/api/repositories/{repositoryId}/items/{itemId}/file?disposition={disp}",
                accessToken,
                tenantId: validation.Record.TenantId,
                cancellationToken: cancellationToken);

            if (result.StatusCode < 200 || result.StatusCode >= 300)
            {
                var text = Encoding.UTF8.GetString(result.Body ?? Array.Empty<byte>());
                return new ContentResult
                {
                    StatusCode = result.StatusCode,
                    Content = string.IsNullOrWhiteSpace(text) ? "{\"error\":\"Download failed.\"}" : text,
                    ContentType = result.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                        ? result.ContentType
                        : "application/json"
                };
            }

            var fileName = string.IsNullOrWhiteSpace(result.FileName)
                ? $"file-{itemId:N}"
                : result.FileName.Trim('"');
            return File(result.Body, result.ContentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download item {ItemId} from {RepositoryId}", itemId, repositoryId);
            return StatusCode(502, new { error = "Failed to reach V6 API.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Single Upload File: OCR (<c>uploadForOcr</c>) then archive upload with OCR metadata.
    /// Client sends <c>repositoryName</c> (or <c>repositoryId</c>) + <c>file</c>.
    /// </summary>
    [HttpPost("repositories/upload-file")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 104_857_600)]
    public async Task<IActionResult> UploadFile(
        IFormFile? file,
        [FromForm] string? repositoryName,
        [FromForm] Guid? repositoryId,
        [FromForm] string? pageNo,
        [FromForm] string? ocrType,
        [FromForm] string? validateType,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "file is required." });

        var validation = await GetValidatedKeyAsync(cancellationToken);
        if (validation.Record == null)
            return Unauthorized(new { message = validation.Message ?? "Invalid API Key." });

        var accessToken = await EnsureAccessTokenAsync(validation.Record, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            return Unauthorized(new { message = "Unable to generate access token for this API key." });

        var tenantId = validation.Record.TenantId;

        Guid repoId;
        string? resolvedName = repositoryName?.Trim();
        if (repositoryId.HasValue && repositoryId.Value != Guid.Empty)
        {
            repoId = repositoryId.Value;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(repositoryName))
                return BadRequest(new { error = "repositoryName is required (or pass repositoryId)." });

            var resolved = await ResolveRepositoryIdByNameAsync(repositoryName.Trim(), accessToken, tenantId, cancellationToken);
            if (resolved.Error != null)
                return resolved.Error;

            repoId = resolved.RepositoryId!.Value;
            resolvedName = resolved.RepositoryName ?? repositoryName.Trim();
        }

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        var fileBytes = ms.ToArray();
        var fileName = file.FileName;
        var contentType = file.ContentType;

        // Always build OCR fields from repository definitions (ignore manual input).
        string? fieldsJson = null;
        try
        {
            var detail = await _v6Api.SendAsync(
                HttpMethod.Get,
                $"/api/repositories/{repoId}",
                accessToken,
                tenantId: tenantId,
                cancellationToken: cancellationToken);

            if (detail.StatusCode >= 200 && detail.StatusCode < 300)
                fieldsJson = BuildOcrFieldsJsonFromRepository(detail.Body);
            else
                _logger.LogWarning("Could not load repository {RepositoryId} fields ({Status})", repoId, detail.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed loading repository fields for {RepositoryId}; OCR will use server defaults", repoId);
        }

        // Step 1 — OCR
        var ocrFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["repositoryId"] = repoId.ToString()
        };
        if (!string.IsNullOrWhiteSpace(pageNo))
            ocrFields["pageNo"] = pageNo.Trim();
        if (!string.IsNullOrWhiteSpace(ocrType))
            ocrFields["ocrType"] = ocrType.Trim();
        if (!string.IsNullOrWhiteSpace(validateType))
            ocrFields["validateType"] = validateType.Trim();
        if (!string.IsNullOrWhiteSpace(fieldsJson))
            ocrFields["fields"] = fieldsJson;

        HttpProxyResult ocrResult;
        try
        {
            ocrResult = await _v6Api.SendMultipartFormAsync(
                "/api/uploadAndIndex/uploadForOcr",
                accessToken,
                ocrFields,
                fileName,
                contentType,
                fileBytes,
                tenantId,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR upload failed for repository {RepositoryId}", repoId);
            return StatusCode(502, new { error = "Failed to reach V6 OCR API.", detail = ex.Message });
        }

        if (ocrResult.StatusCode < 200 || ocrResult.StatusCode >= 300)
        {
            return new ContentResult
            {
                StatusCode = ocrResult.StatusCode,
                Content = ocrResult.Body,
                ContentType = ocrResult.ContentType
            };
        }

        object? ocrPayload;
        Dictionary<string, string> metadata;
        try
        {
            using var ocrDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(ocrResult.Body) ? "{}" : ocrResult.Body);
            ocrPayload = JsonSerializer.Deserialize<object>(ocrResult.Body);
            metadata = BuildMetadataFromOcr(ocrDoc.RootElement);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid OCR JSON for repository {RepositoryId}", repoId);
            return StatusCode(502, new { error = "Invalid OCR response from V6 API.", detail = ex.Message, raw = ocrResult.Body });
        }

        var metadataJson = JsonSerializer.Serialize(metadata);

        // Step 2 — archive upload with OCR metadata
        var archiveFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["metadata"] = metadataJson
        };

        HttpProxyResult archiveResult;
        try
        {
            archiveResult = await _v6Api.SendMultipartFormAsync(
                $"/api/repositories/{repoId}/items/upload-archive",
                accessToken,
                archiveFields,
                fileName,
                contentType,
                fileBytes,
                tenantId,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive upload failed for repository {RepositoryId}", repoId);
            return StatusCode(502, new
            {
                error = "OCR succeeded but archive upload failed to reach V6 API.",
                detail = ex.Message,
                repositoryId = repoId,
                repositoryName = resolvedName,
                ocr = ocrPayload,
                metadata
            });
        }

        object? archivePayload = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(archiveResult.Body))
                archivePayload = JsonSerializer.Deserialize<object>(archiveResult.Body);
        }
        catch
        {
            archivePayload = archiveResult.Body;
        }

        if (archiveResult.StatusCode < 200 || archiveResult.StatusCode >= 300)
        {
            return StatusCode(archiveResult.StatusCode, new
            {
                error = "OCR succeeded but archive upload failed.",
                repositoryId = repoId,
                repositoryName = resolvedName,
                ocr = ocrPayload,
                metadata,
                archive = archivePayload
            });
        }

        return Ok(new
        {
            repositoryId = repoId,
            repositoryName = resolvedName,
            steps = new[] { "uploadForOcr", "upload-archive" },
            fields = fieldsJson,
            ocr = ocrPayload,
            metadata,
            archive = archivePayload
        });
    }

    /// <summary>
    /// Builds OCR fields JSON from repository field definitions, e.g.
    /// ["Supplier, SHORT_TEXT","InvoiceDate, DATE","InvoiceExtractedLineItem, DYNAMIC_TABLE"].
    /// </summary>
    private static string? BuildOcrFieldsJsonFromRepository(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("fields", out var fieldsEl) &&
            !doc.RootElement.TryGetProperty("Fields", out fieldsEl))
            return null;

        if (fieldsEl.ValueKind != JsonValueKind.Array || fieldsEl.GetArrayLength() == 0)
            return null;

        var rows = new List<(int Level, int Order, string Line)>();
        foreach (var f in fieldsEl.EnumerateArray())
        {
            var name = GetStringProp(f, "name") ?? GetStringProp(f, "Name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var dataType = GetStringProp(f, "dataType") ?? GetStringProp(f, "DataType") ?? "SHORT_TEXT";
            var level = 0;
            if (f.TryGetProperty("level", out var levelEl) || f.TryGetProperty("Level", out levelEl))
            {
                if (levelEl.ValueKind == JsonValueKind.Number)
                    level = levelEl.GetInt32();
            }

            var order = int.MaxValue;
            if ((f.TryGetProperty("orderId", out var orderEl) || f.TryGetProperty("OrderId", out orderEl))
                && orderEl.ValueKind == JsonValueKind.Number)
            {
                order = orderEl.GetInt32();
            }

            rows.Add((level, order, $"{name.Trim()}, {dataType.Trim()}"));
        }

        if (rows.Count == 0)
            return null;

        var lines = rows
            .OrderBy(r => r.Level)
            .ThenBy(r => r.Order)
            .Select(r => r.Line)
            .ToList();

        return JsonSerializer.Serialize(lines);
    }

    private static Dictionary<string, string> BuildMetadataFromOcr(JsonElement root)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!TryGetOcrFieldList(root, out var list))
            return metadata;

        foreach (var item in list.EnumerateArray())
        {
            var name = GetStringProp(item, "name") ?? GetStringProp(item, "Name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var value = GetFlexibleString(item, "value") ?? GetFlexibleString(item, "Value") ?? "";
            metadata[name.Trim()] = value;
        }

        return metadata;
    }

    private static string? GetFlexibleString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number => p.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            _ => p.GetRawText()
        };
    }

    private static bool TryGetOcrFieldList(JsonElement root, out JsonElement list)
    {
        if (root.TryGetProperty("ocrFieldList", out list) && list.ValueKind == JsonValueKind.Array)
            return true;
        if (root.TryGetProperty("OcrFieldList", out list) && list.ValueKind == JsonValueKind.Array)
            return true;
        list = default;
        return false;
    }

    private async Task<(Guid? RepositoryId, string? RepositoryName, IActionResult? Error)> ResolveRepositoryIdByNameAsync(
        string repositoryName,
        string accessToken,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        HttpProxyResult list;
        try
        {
            list = await _v6Api.SendAsync(
                HttpMethod.Get,
                "/api/repositories",
                accessToken,
                tenantId: tenantId,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list repositories while resolving name {Name}", repositoryName);
            return (null, null, StatusCode(502, new { error = "Failed to reach V6 API while resolving repository name.", detail = ex.Message }));
        }

        if (list.StatusCode < 200 || list.StatusCode >= 300)
        {
            return (null, null, new ContentResult
            {
                StatusCode = list.StatusCode,
                Content = list.Body,
                ContentType = list.ContentType
            });
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(list.Body) ? "[]" : list.Body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return (null, null, StatusCode(502, new { error = "Unexpected repository list response from V6 API." }));

            Guid? match = null;
            string? matchName = null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var name = GetStringProp(item, "name") ?? GetStringProp(item, "Name");
                if (!string.Equals(name, repositoryName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryGetGuidProp(item, "id", out var id) || TryGetGuidProp(item, "Id", out id))
                {
                    match = id;
                    matchName = name;
                    break;
                }
            }

            if (!match.HasValue)
                return (null, null, NotFound(new { error = $"Repository '{repositoryName}' was not found." }));

            return (match, matchName, null);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid repository list JSON while resolving {Name}", repositoryName);
            return (null, null, StatusCode(502, new { error = "Invalid repository list response from V6 API." }));
        }
    }

    private static string? GetStringProp(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool TryGetGuidProp(JsonElement el, string name, out Guid id)
    {
        id = default;
        if (!el.TryGetProperty(name, out var p))
            return false;
        if (p.ValueKind == JsonValueKind.String && Guid.TryParse(p.GetString(), out id))
            return true;
        return p.TryGetGuid(out id);
    }

    private async Task<IActionResult> ProxyV6Async(
        HttpMethod method,
        string relativePath,
        string? jsonBody,
        CancellationToken cancellationToken)
    {
        var validation = await GetValidatedKeyAsync(cancellationToken);
        if (validation.Record == null)
            return Unauthorized(new { message = validation.Message ?? "Invalid API Key." });

        var accessToken = await EnsureAccessTokenAsync(validation.Record, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            return Unauthorized(new { message = "Unable to generate access token for this API key." });

        try
        {
            var result = await _v6Api.SendAsync(
                method,
                relativePath,
                accessToken,
                jsonBody,
                tenantId: validation.Record.TenantId,
                cancellationToken: cancellationToken);

            return new ContentResult
            {
                StatusCode = result.StatusCode,
                Content = result.Body,
                ContentType = result.ContentType
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "V6 proxy failed for {Path}", relativePath);
            return StatusCode(502, new { error = "Failed to reach V6 API.", detail = ex.Message });
        }
    }

    private async Task<PlaygroundKeyValidation> GetValidatedKeyAsync(CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("X-API-Key", out var apiKeyHeader))
            return new PlaygroundKeyValidation(false, "X-API-Key header is required.", null);

        return await _keyService.ValidateAsync(apiKeyHeader.ToString(), cancellationToken);
    }

    private async Task<string?> EnsureAccessTokenAsync(PlaygroundKeyRecord record, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(record.AccessToken) &&
            (!record.AccessTokenExpiresAtUtc.HasValue || record.AccessTokenExpiresAtUtc.Value > DateTime.UtcNow.AddMinutes(1)))
        {
            return record.AccessToken;
        }

        var credentials = _keyService.GetCredentials(record);
        V6LoginResult login;
        if (!string.IsNullOrWhiteSpace(credentials.SocialProvider))
        {
            login = await _v6Api.SocialLoginAsync(record.Email, credentials.SocialProvider, record.TenantId, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(credentials.Password))
        {
            login = await _v6Api.LoginAsync(record.Email, credentials.Password, record.TenantId, cancellationToken);
        }
        else
        {
            return null;
        }

        var tokenExpiresAt = DateTime.UtcNow.AddSeconds(login.ExpiresIn);
        await _keyService.UpdateAccessTokenAsync(record.ApiKey, login.AccessToken, tokenExpiresAt, cancellationToken);
        return login.AccessToken;
    }
}
