using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using V6Playground.Services;

namespace V6Playground.Controllers;

[ApiController]
[Route("api/v6")]
public sealed class V6WorkflowController : ControllerBase
{
    private readonly PlaygroundKeyService _keyService;
    private readonly V6ApiClient _v6Api;
    private readonly ILogger<V6WorkflowController> _logger;

    public V6WorkflowController(
        PlaygroundKeyService keyService,
        V6ApiClient v6Api,
        ILogger<V6WorkflowController> logger)
    {
        _keyService = keyService;
        _v6Api = v6Api;
        _logger = logger;
    }

    /// <summary>Proxy: GET /api/workflows</summary>
    [HttpGet("workflows")]
    public Task<IActionResult> ListWorkflows(CancellationToken cancellationToken) =>
        ProxyAsync(HttpMethod.Get, "/api/workflows", null, cancellationToken);

    /// <summary>
    /// Initiate Request: client sends <c>workflowName</c>; playground resolves ID then proxies
    /// POST /api/workflows/{id}/start (multipart/form-data).
    /// </summary>
    [HttpPost("workflows/start")]
    public async Task<IActionResult> StartWorkflowByName(
        [FromForm] string? workflowName,
        IFormFile? file,
        [FromForm] string? context,
        [FromForm] string? envType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workflowName))
            return BadRequest(new { error = "workflowName is required." });

        var validation = await GetValidatedKeyAsync(cancellationToken);
        if (validation.Record == null)
            return Unauthorized(new { message = validation.Message ?? "Invalid API Key." });

        var accessToken = await EnsureAccessTokenAsync(validation.Record, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            return Unauthorized(new { message = "Unable to generate access token for this API key." });

        var resolved = await ResolveWorkflowIdByNameAsync(workflowName.Trim(), accessToken, cancellationToken);
        if (resolved.Error != null)
            return resolved.Error;

        return await StartWorkflowInternalAsync(
            resolved.WorkflowId!.Value,
            file,
            context,
            envType,
            accessToken,
            cancellationToken);
    }

    /// <summary>Proxy: POST /api/workflows/{workflowId}/start (multipart/form-data). Prefer /workflows/start with workflowName.</summary>
    [HttpPost("workflows/{workflowId:guid}/start")]
    public async Task<IActionResult> StartWorkflow(
        Guid workflowId,
        IFormFile? file,
        [FromForm] string? context,
        [FromForm] string? envType,
        CancellationToken cancellationToken)
    {
        var validation = await GetValidatedKeyAsync(cancellationToken);
        if (validation.Record == null)
            return Unauthorized(new { message = validation.Message ?? "Invalid API Key." });

        var accessToken = await EnsureAccessTokenAsync(validation.Record, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            return Unauthorized(new { message = "Unable to generate access token for this API key." });

        return await StartWorkflowInternalAsync(workflowId, file, context, envType, accessToken, cancellationToken);
    }

    private async Task<IActionResult> StartWorkflowInternalAsync(
        Guid workflowId,
        IFormFile? file,
        string? context,
        string? envType,
        string accessToken,
        CancellationToken cancellationToken)
    {
        byte[]? fileBytes = null;
        if (file is { Length: > 0 })
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, cancellationToken);
            fileBytes = ms.ToArray();
        }

        var result = await _v6Api.SendMultipartAsync(
            $"/api/workflows/{workflowId}/start",
            accessToken,
            context,
            envType,
            file?.FileName,
            file?.ContentType,
            fileBytes,
            cancellationToken: cancellationToken);

        return new ContentResult
        {
            StatusCode = result.StatusCode,
            Content = result.Body,
            ContentType = result.ContentType
        };
    }

    private async Task<(Guid? WorkflowId, IActionResult? Error)> ResolveWorkflowIdByNameAsync(
        string workflowName,
        string accessToken,
        CancellationToken cancellationToken)
    {
        HttpProxyResult list;
        try
        {
            list = await _v6Api.SendAsync(HttpMethod.Get, "/api/workflows", accessToken, null, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list workflows while resolving name {Name}", workflowName);
            return (null, StatusCode(502, new { error = "Failed to reach V6 API while resolving workflow name.", detail = ex.Message }));
        }

        if (list.StatusCode < 200 || list.StatusCode >= 300)
        {
            return (null, new ContentResult
            {
                StatusCode = list.StatusCode,
                Content = list.Body,
                ContentType = list.ContentType
            });
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(list.Body) ? "{}" : list.Body);
            if (!TryGetWorkflowItems(doc.RootElement, out var items))
                return (null, StatusCode(502, new { error = "Unexpected workflow list response from V6 API." }));

            Guid? match = null;
            foreach (var item in items.EnumerateArray())
            {
                var name = GetStringProp(item, "name") ?? GetStringProp(item, "Name");
                if (!string.Equals(name, workflowName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryGetGuidProp(item, "id", out var id) || TryGetGuidProp(item, "Id", out id))
                {
                    match = id;
                    break;
                }
            }

            if (!match.HasValue)
                return (null, NotFound(new { error = $"Workflow '{workflowName}' was not found." }));

            return (match, null);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid workflow list JSON while resolving {Name}", workflowName);
            return (null, StatusCode(502, new { error = "Invalid workflow list response from V6 API." }));
        }
    }

    private static bool TryGetWorkflowItems(JsonElement root, out JsonElement items)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            items = root;
            return true;
        }

        if (root.TryGetProperty("items", out items) && items.ValueKind == JsonValueKind.Array)
            return true;
        if (root.TryGetProperty("Items", out items) && items.ValueKind == JsonValueKind.Array)
            return true;

        items = default;
        return false;
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

    /// <summary>Proxy: GET /api/workflows/inbox</summary>
    [HttpGet("workflows/inbox")]
    public async Task<IActionResult> GetInbox(
        [FromQuery] Guid workflowId,
        [FromQuery] Guid? instanceId = null,
        [FromQuery] string? transactionId = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool skipTotal = false,
        CancellationToken cancellationToken = default)
    {
        if (workflowId == Guid.Empty)
            return BadRequest(new { error = "workflowId is required." });

        var query = new List<string> { $"workflowId={workflowId}" };
        if (instanceId.HasValue) query.Add($"instanceId={instanceId}");
        if (!string.IsNullOrWhiteSpace(transactionId)) query.Add($"transactionId={Uri.EscapeDataString(transactionId)}");
        query.Add($"pageNumber={pageNumber}");
        query.Add($"pageSize={pageSize}");
        query.Add($"skipTotal={skipTotal.ToString().ToLowerInvariant()}");

        return await ProxyAsync(HttpMethod.Get, $"/api/workflows/inbox?{string.Join("&", query)}", null, cancellationToken);
    }

    /// <summary>
    /// Advance Workflow: client may send <c>workflowName</c> in the JSON body; playground resolves
    /// it to <c>workflowId</c> then proxies POST /api/workflows/instances/{instanceId}/move-next.
    /// </summary>
    [HttpPost("workflows/instances/{instanceId:guid}/move-next")]
    public async Task<IActionResult> MoveNext(
        Guid instanceId,
        [FromBody] JsonElement body,
        CancellationToken cancellationToken)
    {
        var validation = await GetValidatedKeyAsync(cancellationToken);
        if (validation.Record == null)
            return Unauthorized(new { message = validation.Message ?? "Invalid API Key." });

        var accessToken = await EnsureAccessTokenAsync(validation.Record, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            return Unauthorized(new { message = "Unable to generate access token for this API key." });

        string jsonBody;
        try
        {
            using var doc = JsonDocument.Parse(body.ValueKind == JsonValueKind.Undefined ? "{}" : body.GetRawText());
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                string? workflowName = null;
                var hasWorkflowId = false;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals("workflowName") || prop.NameEquals("WorkflowName"))
                    {
                        workflowName = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
                        continue;
                    }

                    if (prop.NameEquals("workflowId") || prop.NameEquals("WorkflowId"))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Null ||
                            (prop.Value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(prop.Value.GetString())))
                            continue;

                        hasWorkflowId = true;
                        prop.WriteTo(writer);
                        continue;
                    }

                    prop.WriteTo(writer);
                }

                if (!hasWorkflowId)
                {
                    if (string.IsNullOrWhiteSpace(workflowName))
                        return BadRequest(new { error = "workflowName is required (or pass workflowId)." });

                    var resolved = await ResolveWorkflowIdByNameAsync(workflowName.Trim(), accessToken, cancellationToken);
                    if (resolved.Error != null)
                        return resolved.Error;

                    writer.WriteString("workflowId", resolved.WorkflowId!.Value);
                }

                writer.WriteEndObject();
            }

            jsonBody = Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = "Invalid JSON body.", detail = ex.Message });
        }

        try
        {
            var result = await _v6Api.SendAsync(
                HttpMethod.Post,
                $"/api/workflows/instances/{instanceId}/move-next",
                accessToken,
                jsonBody,
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
            _logger.LogError(ex, "V6 proxy failed for move-next {InstanceId}", instanceId);
            return StatusCode(502, new { error = "Failed to reach V6 API.", detail = ex.Message });
        }
    }

    private async Task<IActionResult> ProxyAsync(
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
