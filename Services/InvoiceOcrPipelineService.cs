using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using QRCodeAPI.Models;

namespace QRCodeAPI.Services;

/// <summary>
/// Invoice OCR pipeline for tenant 2:
/// 1) uploadforStaticMetadata → OCR values
/// 2) uploadAndIndex/upload → fileId + repo fields
/// 3) transaction → workflow form submit (form 141)
/// 4) build Access2Pay requestPayload only (no connectorId / connector insert)
/// </summary>
public class InvoiceOcrPipelineService
{
    private const string DefaultTenantId = "2";
    private const int DefaultRepositoryId = 214;
    private const int DefaultFormId = 141;
    private const int DefaultWorkflowId = 104;
    private const string DefaultPortalId = "69";
    private const string DefaultReview = "Submit";

    /// <summary>
    /// OCR fields for the Access2Pay Vendor / Invoice JSON (final response).
    /// Combined with <see cref="RepositoryFields"/> into <see cref="OcrFormFields"/> for uploadforStaticMetadata.
    /// Indexed upload still uses only <see cref="RepositoryFields"/>.
    /// </summary>
    private static readonly string[] StaticFormFields =
    {
        // Vendor
        "Supplier Name, SHORT_TEXT",
        "Address, LONG_TEXT",
        "Vendor Id, SHORT_TEXT",
        "City, SHORT_TEXT",
        "State, SHORT_TEXT",
        "Zip, SHORT_TEXT",
        "Country, SHORT_TEXT",
        "Email, SHORT_TEXT",
        "Contact Name, SHORT_TEXT",
        "GLID, SHORT_TEXT",

        // Invoice header
        "Invoice Number, SHORT_TEXT",
        "Invoice Date, DATE",
        "PO Number, SHORT_TEXT",
        "PO Date, DATE",
        "Document Type, SHORT_TEXT",
        "FID Number, SHORT_TEXT",
        "Delivery Doc Number, SHORT_TEXT",
        "Delivery Doc Date, DATE",
        "Currency, SHORT_TEXT",

        // Buyer / ship-to
        "Bill To, SHORT_TEXT",
        "Bill To Address, LONG_TEXT",
        "Ship To Name, SHORT_TEXT",
        "Ship To Address, LONG_TEXT",

        // Amounts
        "Subtotal, SHORT_TEXT",
        "Tax (13%), SHORT_TEXT",
        "Discount, SHORT_TEXT",
        "Deposit, SHORT_TEXT",
        "Charge, SHORT_TEXT",
        "Round Off, SHORT_TEXT",
        "Total Due, SHORT_TEXT",

        // Terms / notes / remittance
        "Payment Terms, SHORT_TEXT",
        "Notes, LONG_TEXT",
        "Remit To, SHORT_TEXT",
        "Bank Account, SHORT_TEXT",
        "Bank Account Number, SHORT_TEXT",

        // Lines
        "Line Items, TABLE"
    };

    /// <summary>Repository metadata field ids for repo 214 — used only for uploadAndIndex/upload.</summary>
    private static readonly (int Id, string Name, string Type)[] RepositoryFields =
    {
        (317, "Supplier Name", "SHORT_TEXT"),
        (318, "PO Number", "SHORT_TEXT"),
        (319, "Invoice Date", "DATE"),
        (320, "Billing To", "SHORT_TEXT"),
        (321, "Description", "SHORT_TEXT"),
        (322, "Type", "SHORT_TEXT"),
        (323, "Quantity", "SHORT_TEXT"),
        (324, "Rate", "SHORT_TEXT"),
        (325, "Amount", "SHORT_TEXT"),
        (326, "Subtotal", "SHORT_TEXT"),
        (327, "Tax(13%)", "SHORT_TEXT"),
        (328, "Total Due", "SHORT_TEXT"),
        (329, "Payment Terms", "SHORT_TEXT"),
        (330, "Remit To", "SHORT_TEXT")
    };

    /// <summary>
    /// formFields for uploadforStaticMetadata: all Vendor/Invoice OCR fields ∪ repository names (deduped).
    /// </summary>
    private static readonly string[] OcrFormFields = BuildOcrFormFields();

    private static string[] BuildOcrFormFields()
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string type)
        {
            name = name.Trim();
            type = type.Trim();
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                return;
            result.Add($"{name}, {type}");
        }

        foreach (var entry in StaticFormFields)
        {
            var parts = entry.Split(',', 2);
            Add(parts[0], parts.Length > 1 ? parts[1] : "SHORT_TEXT");
        }

        // Include repo names so upload can bind OCR → repository metadata directly.
        foreach (var (_, name, type) in RepositoryFields)
            Add(name, type);

        return result.ToArray();
    }

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApiKeyService _apiKeyService;
    private readonly FormDetailsService _formDetailsService;
    private readonly ILogger<InvoiceOcrPipelineService> _logger;
    private readonly string _ezofisBaseUrl;
    private readonly string _tenantId;
    private readonly int _repositoryId;
    private readonly int _formId;
    private readonly int _workflowId;
    private readonly string _portalId;
    private readonly string _transactionPath;
    private readonly int _fileExportArchivedWaitSeconds;
    private readonly int _fileExportArchivedPollMs;

    public InvoiceOcrPipelineService(
        IHttpClientFactory httpClientFactory,
        ApiKeyService apiKeyService,
        FormDetailsService formDetailsService,
        ILogger<InvoiceOcrPipelineService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _apiKeyService = apiKeyService;
        _formDetailsService = formDetailsService;
        _logger = logger;
        _ezofisBaseUrl = (configuration["ExternalApis:Ezofis:BaseUrl"] ?? "https://eztapi.ezofis.com").TrimEnd('/');
        _tenantId = configuration["ExternalApis:InvoiceOcr:TenantId"]
            ?? configuration["ExternalApis:Access2Pay:TenantId"]
            ?? DefaultTenantId;
        _repositoryId = int.TryParse(configuration["ExternalApis:InvoiceOcr:RepositoryId"], out var repo)
            ? repo
            : DefaultRepositoryId;
        _formId = int.TryParse(configuration["ExternalApis:InvoiceOcr:FormId"], out var form)
            ? form
            : DefaultFormId;
        _workflowId = int.TryParse(configuration["ExternalApis:InvoiceOcr:WorkflowId"], out var wf)
            ? wf
            : DefaultWorkflowId;
        _portalId = configuration["ExternalApis:InvoiceOcr:PortalId"] ?? DefaultPortalId;
        _transactionPath = configuration["ExternalApis:InvoiceOcr:TransactionPath"] ?? "/api/workflow/transaction";
        _fileExportArchivedWaitSeconds = int.TryParse(
            configuration["ExternalApis:InvoiceOcr:FileExportArchivedWaitSeconds"], out var waitSec)
            ? Math.Clamp(waitSec, 1, 300)
            : 45;
        _fileExportArchivedPollMs = int.TryParse(
            configuration["ExternalApis:InvoiceOcr:FileExportArchivedPollMs"], out var pollMs)
            ? Math.Clamp(pollMs, 200, 10000)
            : 1500;
    }

    public async Task<ResultForHttpsCode> ProcessInvoiceAsync(
        IFormFile file,
        string? playgroundApiKey = null,
        string? storageCallbackUrl = null,
        CancellationToken cancellationToken = default)
    {
        var log = new ProcessIoLogState
        {
            FileName = file?.FileName,
            FileSize = file?.Length ?? 0,
            StorageCallbackUrl = storageCallbackUrl
        };

        ResultForHttpsCode result;
        try
        {
            result = await ProcessInvoiceCoreAsync(file, playgroundApiKey, storageCallbackUrl, log, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InitiateProcess failed unexpectedly");
            result = Error(ex.Message);
        }

        // Tenant DB only (not main) — fire-and-forget so API latency is unaffected.
        QueueTenantProcessIoLog(log, result);
        return result;
    }

    private async Task<ResultForHttpsCode> ProcessInvoiceCoreAsync(
        IFormFile? file,
        string? playgroundApiKey,
        string? storageCallbackUrl,
        ProcessIoLogState log,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return Error("File is required");

        string token;
        string createdBy;
        string? userId = null;

        if (!string.IsNullOrWhiteSpace(playgroundApiKey))
        {
            var tokenResolved = await _apiKeyService.GetEzofisTokenByApiKeyAsync(playgroundApiKey, _tenantId);
            if (string.IsNullOrWhiteSpace(tokenResolved.Token))
                return Error(tokenResolved.Error ?? $"Unable to resolve Ezofis token for API key (tenantId {_tenantId})");

            token = tokenResolved.Token!;
            createdBy = tokenResolved.Email ?? $"tenant{_tenantId}@ezofis.com";
            userId = tokenResolved.UserId;
            _logger.LogInformation(
                "InitiateProcess token resolved via API key email={Email} userId={UserId} tenantId={TenantId}",
                tokenResolved.Email, tokenResolved.UserId, tokenResolved.TenantId);
        }
        else
        {
            var fallback = await _apiKeyService.GetTokenByTenantIdAsync(_tenantId);
            if (string.IsNullOrWhiteSpace(fallback))
                return Error($"No authenticate token found for tenantId {_tenantId}");

            token = fallback;
            createdBy = await ResolveTenantEmailAsync(_tenantId) ?? $"tenant{_tenantId}@ezofis.com";
        }

        log.SubmittedFrom = createdBy;

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();
        var originalFileName = string.IsNullOrWhiteSpace(file.FileName) ? "invoice.pdf" : file.FileName;
        log.FileName = originalFileName;
        log.FileSize = bytes.Length;

        // Step 1 — OCR via uploadforStaticMetadata (response may be AES-encrypted for portal tokens)
        var ocrRaw = await UploadForStaticMetadataAsync(bytes, originalFileName, token, cancellationToken);
        if (ocrRaw.id == 0)
            return ocrRaw;

        var ocrPlain = await DecryptResponseIfNeededAsync(token, ocrRaw.output);
        if (ocrPlain.error != null)
            return Error(ocrPlain.error);

        ocrRaw.output = ocrPlain.plain;
        var ocrDoc = UnwrapJson(ocrPlain.plain ?? "");
        if (ocrDoc == null)
            return Error("Unable to parse OCR response from uploadforStaticMetadata");

        log.InputJson = BuildProcessInputJson(originalFileName, bytes.Length, createdBy, storageCallbackUrl, ocrPlain.plain);

        var ocrByName = BuildOcrLookup(ocrDoc);
        var invoiceNumber = GetOcrString(ocrByName, "Invoice Number");
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            invoiceNumber = Path.GetFileNameWithoutExtension(originalFileName);

        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".pdf";
        var uploadFileName = SanitizeFileName(invoiceNumber) + ext;

        // Step 2 — upload with repo fields filled from OCR (response may also be encrypted)
        var fieldsJson = BuildRepositoryFieldsJson(ocrByName);
        var uploadRaw = await UploadIndexedAsync(bytes, uploadFileName, fieldsJson, token, cancellationToken);
        if (uploadRaw.id == 0)
            return uploadRaw;

        var uploadPlain = await DecryptResponseIfNeededAsync(token, uploadRaw.output);
        if (uploadPlain.error != null)
            return Error(uploadPlain.error);

        uploadRaw.output = uploadPlain.plain;
        var uploadDoc = UnwrapJson(uploadPlain.plain ?? "");
        if (uploadDoc == null || !TryGetFileId(uploadDoc.RootElement, out var fileId))
            return Error($"Unable to parse fileId from upload response: {uploadRaw.output}");

        // Step 3 — transaction with form 141 mapped from wformcontrols + OCR
        var controls = await LoadFormControlsAsync(_formId);
        var transactionBody = BuildTransactionPayload(
            ocrByName,
            controls,
            fileId,
            uploadFileName,
            bytes.Length,
            createdBy);

        var txRaw = await SubmitTransactionAsync(transactionBody, token, cancellationToken);
        if (txRaw.id == 0)
            return txRaw;

        var txPlain = await DecryptResponseIfNeededAsync(token, txRaw.output);
        if (txPlain.error != null)
            return Error(txPlain.error);
        txRaw.output = txPlain.plain;

        // Step 4 — CreateJsonPayload-style: load from tenant DB queries → requestPayload only
        var txIds = ExtractTransactionIds(txRaw.output);
        var processId = txIds.ProcessId;
        var transactionId = txIds.TransactionId > 0 ? txIds.TransactionId : processId;
        var formId = _formId;
        var entryId = txIds.FormEntryId;

        if (processId <= 0)
            processId = await ResolveLatestProcessIdAsync(_workflowId);

        if (processId <= 0)
            return Error("Unable to resolve processId from transaction response for CreateJsonPayload");

        if (transactionId <= 0)
            transactionId = processId;

        var (requestPayload, payloadError, _) = await CreateRequestPayloadFromDbAsync(
            wId: _workflowId,
            pId: processId,
            formId: formId,
            entryId: entryId,
            userId: userId,
            fallbackFileName: uploadFileName,
            fallbackFileId: fileId,
            fallbackSubmittedFrom: createdBy,
            cancellationToken);

        if (payloadError != null)
            return Error(payloadError);

        // Prefer DB form columns; fill empty/missing Vendor/Invoice values from OCR.
        FillMissingVendorInvoiceFromOcr(requestPayload!, ocrByName);

        var payloadJson = requestPayload!.ToJsonString();
        log.ReferenceNo = requestPayload["referenceNo"]?.ToString();

        // Optional: POST request payload to client's storageCallbackUrl and return their response
        // together with the usual requestPayload.
        if (!string.IsNullOrWhiteSpace(storageCallbackUrl))
        {
            var callback = await PostPayloadToCallbackAsync(
                storageCallbackUrl.Trim(), payloadJson, cancellationToken);

            // storageCallback first, then the usual requestPayload fields below
            var combined = new JsonObject
            {
                ["storageCallback"] = callback.ResponseNode
            };
            foreach (var prop in requestPayload)
                combined[prop.Key] = prop.Value?.DeepClone();

            return new ResultForHttpsCode
            {
                id = 1,
                output = combined.ToJsonString(),
                EncryptOutput = callback.Success
                    ? null
                    : (callback.ErrorMessage ?? "Storage callback failed")
            };
        }

        return new ResultForHttpsCode
        {
            id = 1,
            output = payloadJson,
            EncryptOutput = null
        };
    }

    private sealed class ProcessIoLogState
    {
        public string? FileName { get; set; }
        public long FileSize { get; set; }
        public string? SubmittedFrom { get; set; }
        public string? StorageCallbackUrl { get; set; }
        public string? InputJson { get; set; }
        public string? ReferenceNo { get; set; }
    }

    private static string BuildProcessInputJson(
        string fileName,
        long fileSize,
        string submittedFrom,
        string? storageCallbackUrl,
        string? ocrJson)
    {
        JsonNode? ocrNode = null;
        if (!string.IsNullOrWhiteSpace(ocrJson))
        {
            try { ocrNode = JsonNode.Parse(ocrJson); }
            catch { ocrNode = ocrJson; }
        }

        var input = new JsonObject
        {
            ["fileName"] = fileName,
            ["fileSize"] = fileSize,
            ["submittedFrom"] = submittedFrom,
            ["storageCallbackUrl"] = string.IsNullOrWhiteSpace(storageCallbackUrl) ? null : storageCallbackUrl,
            ["ocr"] = ocrNode
        };
        return input.ToJsonString();
    }

    private void QueueTenantProcessIoLog(ProcessIoLogState log, ResultForHttpsCode result)
    {
        // Capture values — do not capture mutable request objects beyond this point.
        var fileName = log.FileName;
        var fileSize = log.FileSize;
        var submittedFrom = log.SubmittedFrom;
        var referenceNo = log.ReferenceNo;
        var inputJson = log.InputJson;
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            inputJson = BuildProcessInputJson(
                fileName ?? "",
                fileSize,
                submittedFrom ?? "",
                log.StorageCallbackUrl,
                ocrJson: null);
        }

        var outputJson = result.output;
        var isSuccess = result.id == 1;
        var errorMessage = result.EncryptOutput;
        if (string.IsNullOrWhiteSpace(referenceNo) && !string.IsNullOrWhiteSpace(outputJson))
        {
            try
            {
                var node = JsonNode.Parse(outputJson);
                referenceNo = node?["referenceNo"]?.ToString()
                    ?? node?["storageCallback"]?["referenceNo"]?.ToString()
                    ?? node?["storageCallback"]?["payload"]?["referenceNo"]?.ToString();
            }
            catch
            {
                // ignore parse errors for logging
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await SaveTenantProcessIoLogAsync(
                    referenceNo,
                    fileName,
                    submittedFrom,
                    inputJson,
                    outputJson,
                    isSuccess,
                    errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background Access2PayProcessIoLog save failed");
            }
        });
    }

    private async Task SaveTenantProcessIoLogAsync(
        string? referenceNo,
        string? fileName,
        string? submittedFrom,
        string? inputJson,
        string? outputJson,
        bool isSuccess,
        string? errorMessage)
    {
        var tenantCs = await GetTenantConnectionStringAsync(_tenantId);
        if (string.IsNullOrWhiteSpace(tenantCs))
        {
            _logger.LogWarning(
                "Skip Access2PayProcessIoLog: no tenant connectionString for tenantId={TenantId}",
                _tenantId);
            return;
        }

        await using var conn = new SqlConnection(tenantCs);
        await conn.OpenAsync();
        await EnsureAccess2PayProcessIoLogTableAsync(conn);

        await using var cmd = new SqlCommand(
            @"INSERT INTO dbo.Access2PayProcessIoLog
                (ReferenceNo, FileName, SubmittedFrom, InputJson, OutputJson, IsSuccess, ErrorMessage, CreatedAt)
              VALUES
                (@ref, @file, @from, @in, @out, @ok, @err, SYSUTCDATETIME())", conn);
        cmd.Parameters.AddWithValue("@ref", (object?)NullIfWhite(referenceNo) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@file", (object?)NullIfWhite(fileName) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@from", (object?)NullIfWhite(submittedFrom) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@in", (object?)inputJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@out", (object?)outputJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ok", isSuccess);
        cmd.Parameters.AddWithValue("@err", (object?)NullIfWhite(errorMessage) ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string? NullIfWhite(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task EnsureAccess2PayProcessIoLogTableAsync(SqlConnection conn)
    {
        await using var cmd = new SqlCommand(
            @"IF OBJECT_ID(N'dbo.Access2PayProcessIoLog', N'U') IS NULL
              BEGIN
                CREATE TABLE dbo.Access2PayProcessIoLog (
                    Id            BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    ReferenceNo   NVARCHAR(100) NULL,
                    FileName      NVARCHAR(512) NULL,
                    SubmittedFrom NVARCHAR(256) NULL,
                    InputJson     NVARCHAR(MAX) NULL,
                    OutputJson    NVARCHAR(MAX) NULL,
                    IsSuccess     BIT NOT NULL CONSTRAINT DF_Access2PayProcessIoLog_IsSuccess DEFAULT (1),
                    ErrorMessage  NVARCHAR(MAX) NULL,
                    CreatedAt     DATETIME2 NOT NULL CONSTRAINT DF_Access2PayProcessIoLog_CreatedAt DEFAULT (SYSUTCDATETIME())
                );
                CREATE INDEX IX_Access2PayProcessIoLog_CreatedAt
                    ON dbo.Access2PayProcessIoLog (CreatedAt DESC);
                CREATE INDEX IX_Access2PayProcessIoLog_ReferenceNo
                    ON dbo.Access2PayProcessIoLog (ReferenceNo);
              END", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// POSTs requestPayload JSON to the client-provided URL and returns their response body
    /// (parsed JSON when possible). On HTTP/network failure, returns received=false + fail message.
    /// </summary>
    private async Task<(bool Success, JsonNode ResponseNode, string? ErrorMessage)> PostPayloadToCallbackAsync(
        string callbackUrl,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (false,
                new JsonObject
                {
                    ["received"] = false,
                    ["message"] = "Invoice data store failed"
                },
                "StorageCallbackUrl must be a valid http or https URL");
        }

        try
        {
            var client = _httpClientFactory.CreateClient("InvoiceOcr");
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(uri, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            JsonNode responseNode;
            try
            {
                responseNode = string.IsNullOrWhiteSpace(body)
                    ? new JsonObject()
                    : (JsonNode.Parse(body) ?? new JsonObject());
            }
            catch
            {
                responseNode = new JsonObject { ["body"] = body };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "StorageCallbackUrl returned {Status}: {Body}",
                    (int)response.StatusCode,
                    body);

                // Prefer client body; ensure fail shape if client didn't set received/message
                if (responseNode is JsonObject failObj)
                {
                    failObj["received"] = failObj.ContainsKey("received")
                        ? failObj["received"]
                        : false;
                    if (!failObj.ContainsKey("message") || string.IsNullOrWhiteSpace(failObj["message"]?.ToString()))
                        failObj["message"] = "Invoice data store failed";
                    if (!failObj.ContainsKey("statusCode"))
                        failObj["statusCode"] = (int)response.StatusCode;
                }

                return (false, responseNode,
                    $"StorageCallbackUrl error {(int)response.StatusCode}: {body}");
            }

            _logger.LogInformation("Request payload posted to StorageCallbackUrl {Url}", callbackUrl);

            // Normalize success message if client returned a simple ack without message
            if (responseNode is JsonObject okObj)
            {
                if (!okObj.ContainsKey("received"))
                    okObj["received"] = true;
                if (!okObj.ContainsKey("message") || string.IsNullOrWhiteSpace(okObj["message"]?.ToString()))
                    okObj["message"] = "Invoice data stored successfully";
            }

            return (true, responseNode, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StorageCallbackUrl call failed for {Url}", callbackUrl);
            return (false,
                new JsonObject
                {
                    ["received"] = false,
                    ["message"] = "Invoice data store failed",
                    ["error"] = ex.Message
                },
                "StorageCallbackUrl failed: " + ex.Message);
        }
    }

    /// <summary>
    /// EncMiddleware encrypts responses for non-GENERICAPI tokens.
    /// If body is already JSON, return as-is; otherwise decryptAES.
    /// </summary>
    private async Task<(string? plain, string? error)> DecryptResponseIfNeededAsync(string token, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (raw, null);

        var text = UnwrapQuotedString(raw.Trim());
        if (string.IsNullOrWhiteSpace(text))
            return (text, null);

        // Already plaintext JSON / known middleware error prefixes
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('[')
            || trimmed.StartsWith("1..") || trimmed.StartsWith("2..")
            || trimmed.StartsWith("3..") || trimmed.StartsWith("4.."))
        {
            return (text, null);
        }

        var (decOk, plainOrErr) = await _formDetailsService.DecryptAesAsync(token, text);
        if (!decOk || string.IsNullOrWhiteSpace(plainOrErr))
            return (null, plainOrErr ?? "decryptAES failed for upstream response");

        return (UnwrapQuotedString(plainOrErr.Trim()), null);
    }

    private sealed class PayloadResolvedIds
    {
        public int FormId { get; set; }
        public int EntryId { get; set; }
        public int RepositoryId { get; set; }
        public long ItemId { get; set; }
        public string? FileName { get; set; }
    }

    /// <summary>
    /// Poll fileExport until Status='Archived' for this workflow/process.
    /// Filename/itemId must only be taken from the Archived row (non-Archived can be wrong).
    /// </summary>
    private async Task<(string RepositoryId, string ItemId)?> WaitForArchivedFileExportAsync(
        SqlConnection conn,
        int wId,
        int pId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(_fileExportArchivedWaitSeconds);
        var attempt = 0;

        while (true)
        {
            attempt++;
            await using (var feCmd = new SqlCommand(
                             @"SELECT TOP 1 repositoryId, itemId, [Status]
                               FROM fileExport
                               WHERE workflowid=@wId AND processid=@pId AND [Status]='Archived'
                               ORDER BY id DESC", conn))
            {
                feCmd.Parameters.AddWithValue("@wId", wId);
                feCmd.Parameters.AddWithValue("@pId", pId);
                await using var feReader = await feCmd.ExecuteReaderAsync(cancellationToken);
                if (await feReader.ReadAsync(cancellationToken))
                {
                    var repo = feReader["repositoryId"]?.ToString()?.Trim() ?? "";
                    var item = feReader["itemId"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(repo) && !string.IsNullOrWhiteSpace(item))
                    {
                        _logger.LogInformation(
                            "fileExport Archived found on attempt {Attempt} for workflowId={WorkflowId} processId={ProcessId} repositoryId={RepositoryId} itemId={ItemId}",
                            attempt, wId, pId, repo, item);
                        return (repo, item);
                    }
                }
            }

            // Optional: log current non-Archived status so we can see progress
            string? currentStatus = null;
            await using (var statusCmd = new SqlCommand(
                             @"SELECT TOP 1 [Status]
                               FROM fileExport
                               WHERE workflowid=@wId AND processid=@pId
                               ORDER BY id DESC", conn))
            {
                statusCmd.Parameters.AddWithValue("@wId", wId);
                statusCmd.Parameters.AddWithValue("@pId", pId);
                var statusVal = await statusCmd.ExecuteScalarAsync(cancellationToken);
                currentStatus = statusVal?.ToString();
            }

            if (DateTime.UtcNow >= deadline)
            {
                _logger.LogWarning(
                    "Timed out waiting for fileExport Archived (wait={Wait}s, lastStatus={Status}) workflowId={WorkflowId} processId={ProcessId}",
                    _fileExportArchivedWaitSeconds, currentStatus ?? "(none)", wId, pId);
                return null;
            }

            _logger.LogInformation(
                "Waiting for fileExport Archived (attempt={Attempt}, status={Status}, pollMs={PollMs}) workflowId={WorkflowId} processId={ProcessId}",
                attempt, currentStatus ?? "(none)", _fileExportArchivedPollMs, wId, pId);

            await Task.Delay(_fileExportArchivedPollMs, cancellationToken);
        }
    }

    /// <summary>
    /// Same data path as ezofis CreateJsonPayload: processform_*, fileExport, ezca_*_items, ezfb_*_items.
    /// Returns requestPayload only (no connectorId / connector insert).
    /// </summary>
    private async Task<(JsonObject? payload, string? error, PayloadResolvedIds resolved)> CreateRequestPayloadFromDbAsync(
        int wId,
        int pId,
        int formId,
        int entryId,
        string? userId,
        string fallbackFileName,
        long fallbackFileId,
        string fallbackSubmittedFrom,
        CancellationToken cancellationToken)
    {
        var resolved = new PayloadResolvedIds
        {
            FormId = formId,
            EntryId = entryId,
            RepositoryId = _repositoryId,
            ItemId = fallbackFileId,
            FileName = fallbackFileName
        };

        if (wId <= 0 || pId <= 0)
            return (null, "Wrong Input: workflowId and processId are required", resolved);

        var tenantCs = await GetTenantConnectionStringAsync(_tenantId);
        if (string.IsNullOrWhiteSpace(tenantCs))
            return (null, $"No tenant connectionString for tenantId {_tenantId}", resolved);

        await using var conn = new SqlConnection(tenantCs);
        await conn.OpenAsync(cancellationToken);

        // Resolve formId + entryId from processform_{wId} when missing
        if (formId == 0 || entryId == 0)
        {
            var processFormSql =
                $"SELECT TOP 1 wFormId, formEntryId FROM processform_{wId} WHERE processId=@pId AND isdeleted=0";
            await using (var cmd = new SqlCommand(processFormSql, conn))
            {
                cmd.Parameters.AddWithValue("@pId", pId);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    if (formId == 0 && !reader.IsDBNull(0))
                        formId = Convert.ToInt32(reader.GetValue(0));
                    if (entryId == 0 && !reader.IsDBNull(1))
                        entryId = Convert.ToInt32(reader.GetValue(1));
                }
            }
        }

        if (formId <= 0 || entryId <= 0)
            return (null, $"No Record Found in processform_{wId} for processId={pId}", resolved);

        resolved.FormId = formId;
        resolved.EntryId = entryId;

        // submittedFrom via udf_email(userId) when available
        var submittedFrom = fallbackSubmittedFrom ?? "";
        if (!string.IsNullOrWhiteSpace(userId))
        {
            try
            {
                await using var emailCmd = new SqlCommand("SELECT dbo.udf_email(@uid)", conn);
                emailCmd.Parameters.AddWithValue("@uid", userId);
                var emailVal = await emailCmd.ExecuteScalarAsync(cancellationToken);
                var email = emailVal?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(email))
                    submittedFrom = email;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "udf_email failed for userId={UserId}; using fallback email", userId);
            }
        }

        // Prefer fileExport Status='Archived' for repositoryId/itemId/fileName.
        // If archive is slow (larger OCR / backend lag), fall back to upload fileId so we still return Vendor/Invoice.
        string fileName = fallbackFileName;
        string repositoryId = _repositoryId.ToString(CultureInfo.InvariantCulture);
        string itemId = fallbackFileId.ToString(CultureInfo.InvariantCulture);

        var archived = await WaitForArchivedFileExportAsync(conn, wId, pId, cancellationToken);
        if (archived == null)
        {
            _logger.LogWarning(
                "fileExport Status='Archived' not found for workflowId={WorkflowId} processId={ProcessId} after {Wait}s; using upload fallback fileId={FileId} fileName={FileName}",
                wId, pId, _fileExportArchivedWaitSeconds, fallbackFileId, fallbackFileName);
        }
        else
        {
            repositoryId = archived.Value.RepositoryId;
            itemId = archived.Value.ItemId;
        }

        if (int.TryParse(repositoryId, out var repoParsed))
            resolved.RepositoryId = repoParsed;
        if (long.TryParse(itemId, out var itemParsed))
            resolved.ItemId = itemParsed;

        if (!string.IsNullOrWhiteSpace(repositoryId) && !string.IsNullOrWhiteSpace(itemId)
            && int.TryParse(repositoryId, out _) && long.TryParse(itemId, out _))
        {
            var itemTbl = "ezca_" + repositoryId + "_items";
            await using var fileCmd = new SqlCommand(
                $"SELECT TOP 1 ifileName, ifilepath, createdAt FROM {itemTbl} WHERE itemId=@itemId", conn);
            fileCmd.Parameters.AddWithValue("@itemId", itemId);
            try
            {
                await using var fileReader = await fileCmd.ExecuteReaderAsync(cancellationToken);
                if (await fileReader.ReadAsync(cancellationToken))
                {
                    var name = fileReader["ifileName"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                        fileName = name;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to read {Table} for itemId={ItemId}", itemTbl, itemId);
            }
        }

        resolved.FileName = fileName;

        // Form entry: ezfb_{formId}_items
        var frmEntryTbl = "ezfb_" + formId + "_items";
        Dictionary<string, string?> rowVals;
        await using (var frmCmd = new SqlCommand(
                         $"SELECT TOP 1 * FROM {frmEntryTbl} WHERE itemid=@entryId", conn))
        {
            frmCmd.Parameters.AddWithValue("@entryId", entryId);
            await using var frmReader = await frmCmd.ExecuteReaderAsync(cancellationToken);
            if (!await frmReader.ReadAsync(cancellationToken))
                return (null, $"No Record Found in {frmEntryTbl} for itemid={entryId}", resolved);

            rowVals = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var i = 0; i < frmReader.FieldCount; i++)
            {
                var col = frmReader.GetName(i);
                rowVals[col] = frmReader.IsDBNull(i) ? null : Convert.ToString(frmReader.GetValue(i), CultureInfo.InvariantCulture);
            }
        }

        static string Col(Dictionary<string, string?> map, string key) =>
            map.TryGetValue(key, out var v) ? (v ?? "") : "";

        var invoiceNo = Col(rowVals, "4aLUV4JxBiKL8wQgFhebs");
        var invoiceDate = Col(rowVals, "fj3TUS79vGvd_ifEdOsE4");
        var poNumber = Col(rowVals, "HxtFeDMScd1LPjrYf6v8T");
        var billToName = Col(rowVals, "gsA2hzqoY9fMCwm42hKrh");
        var billtoAddress = Col(rowVals, "-k3D7jU77XRWgtfcil1dG");
        var taxAmt = Col(rowVals, "MC5iT7wcEMrKk4cmqg-7h");
        var paymentTerms = Col(rowVals, "UVbvHIwz-amlBAGs7VV1n");
        var bankName = Col(rowVals, "9yKjxAOWa39mM21dAtvxM");
        var companyName = Col(rowVals, "TaeCgq7OzxoyAA0o2v8ix");
        var address1 = Col(rowVals, "-k3D7jU77XRWgtfcil1dG");
        var netTotal = Col(rowVals, "ZGN3pv6w7DJsQYWPhWNFW");
        var grossAmt = Col(rowVals, "z4hqS0dfNuqd8jIneIW3H");
        var lineItemsRaw = Col(rowVals, "FmdAgT2UC6LC1xsUP6_BH");
        var submittedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        var controls = await LoadFormControlsAsync(formId);
        var controlMap = controls
            .Where(c => !IsDeleted(c) && !string.IsNullOrWhiteSpace(c.JsonId))
            .GroupBy(c => c.JsonId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var lineItems = new JsonArray();
        if (!string.IsNullOrWhiteSpace(lineItemsRaw) && lineItemsRaw != "[]")
        {
            try
            {
                using var rowsDoc = JsonDocument.Parse(lineItemsRaw);
                if (rowsDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var rowNo = 1;
                    foreach (var row in rowsDoc.RootElement.EnumerateArray())
                    {
                        if (row.ValueKind != JsonValueKind.Object) continue;

                        var metadata = new JsonArray();
                        var metaLineNo = 1;
                        foreach (var prop in row.EnumerateObject())
                        {
                            if (!controlMap.TryGetValue(prop.Name, out var ctrl)) continue;
                            metadata.Add(new JsonObject
                            {
                                ["lineNumber"] = metaLineNo++,
                                ["header"] = ctrl.Name,
                                ["dataType"] = ctrl.Type,
                                ["value"] = prop.Value.ValueKind == JsonValueKind.String
                                    ? prop.Value.GetString()
                                    : prop.Value.GetRawText()
                            });
                        }

                        lineItems.Add(new JsonObject
                        {
                            ["lineNumber"] = rowNo++,
                            ["itemNumber"] = null,
                            ["description"] = GetJsonPropAsString(row, "zlWNOsO9uKLmi4OFPwwPL"),
                            ["quantity"] = GetJsonPropAsString(row, "e57PUxiqXSefFbDp5wt3I"),
                            ["unitOfMeasure"] = "",
                            ["rate"] = GetJsonPropAsString(row, "ila1u-gw9OiASx5-QeOTa"),
                            ["lineAmount"] = GetJsonPropAsString(row, "cJ9aKTkwvdSTv_XK5Z9dM"),
                            ["metadata"] = metadata
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to parse line items JSON from form entry");
            }
        }

        // referenceNo from process_{wId}
        var referenceNo = "";
        try
        {
            await using var refCmd = new SqlCommand(
                $"SELECT TOP 1 requestNo FROM process_{wId} WHERE id=@pId", conn);
            refCmd.Parameters.AddWithValue("@pId", pId);
            var refVal = await refCmd.ExecuteScalarAsync(cancellationToken);
            referenceNo = refVal?.ToString()?.Trim() ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read requestNo from process_{WorkflowId}", wId);
        }

        if (string.IsNullOrWhiteSpace(referenceNo))
            referenceNo = $"REQ-{pId}";

        // Same as CreateJsonPayload:
        // apiurl + "/api/file/view/" + tenantId + "/" + userId + "/" + repositoryId + "/" + itemId + "/2/1/" + fileName
        var userSegment = string.IsNullOrWhiteSpace(userId) ? "0" : userId.Trim();
        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "file.pdf" : fileName.Trim().TrimStart('/');
        var location =
            $"{_ezofisBaseUrl}/api/file/view/{_tenantId}/2/{repositoryId}/{itemId}/2/1/{safeFileName}";

        var payload = new JsonObject
        {
            ["referenceNo"] = referenceNo,
            ["submission"] = new JsonObject
            {
                ["submittedFrom"] = submittedFrom,
                ["emailSubject"] = companyName,
                ["receivedFileName"] = fileName,
                ["submittedAtUtc"] = submittedAtUtc
            },
            ["sourceDocument"] = new JsonObject
            {
                ["fileName"] = fileName,
                ["mimeType"] = "application/pdf",
                ["storageProvider"] = "blob",
                ["location"] = location,
                ["externalId"] = ""
            },
            ["Vendor"] = new JsonObject
            {
                ["vendorId"] = "",
                ["company"] = companyName,
                ["address1"] = address1,
                ["city"] = "",
                ["state"] = "",
                ["zip"] = "",
                ["country"] = "",
                ["email"] = "",
                ["contactName"] = "",
                ["glid"] = ""
            },
            ["Invoice"] = new JsonObject
            {
                ["documentType"] = "Invoice",
                ["fidNumber"] = null,
                ["invoiceNumber"] = invoiceNo,
                ["invoiceDate"] = invoiceDate,
                ["poNumber"] = poNumber,
                ["poDate"] = "",
                ["deliveryDocNumber"] = null,
                ["deliveryDocDate"] = null,
                ["currency"] = "CAD",
                ["buyer"] = new JsonObject
                {
                    ["billToName"] = billToName,
                    ["billToAddress"] = billtoAddress,
                    ["shipToName"] = null,
                    ["shipToAddress"] = null
                },
                ["amounts"] = new JsonObject
                {
                    ["grossAmount"] = string.IsNullOrWhiteSpace(grossAmt) ? null : grossAmt,
                    ["taxAmount"] = string.IsNullOrWhiteSpace(taxAmt) ? null : taxAmt,
                    ["discount"] = null,
                    ["deposit"] = null,
                    ["charge"] = null,
                    ["roundOff"] = null,
                    ["netTotal"] = string.IsNullOrWhiteSpace(netTotal) ? null : netTotal
                },
                ["paymentTerms"] = paymentTerms,
                ["notes"] = "",
                ["remittance"] = new JsonObject
                {
                    ["bankName"] = bankName,
                    ["bankAccount"] = "",
                    ["bankAccountNumber"] = ""
                },
                ["lineItems"] = lineItems
            }
        };

        return (payload, null, resolved);
    }

    /// <summary>
    /// Prefer DB form values already on the payload. Where a field is null/empty, fill from OCR.
    /// </summary>
    private static void FillMissingVendorInvoiceFromOcr(JsonObject payload, Dictionary<string, JsonElement> ocrByName)
    {
        static string Ocr(Dictionary<string, JsonElement> map, params string[] names)
        {
            foreach (var n in names)
            {
                var v = GetOcrString(map, n);
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }
            return "";
        }

        static bool IsEmpty(JsonNode? node)
        {
            if (node == null || node is JsonObject { Count: 0 })
                return true;
            if (node is JsonValue)
            {
                var s = node.ToString();
                return string.IsNullOrWhiteSpace(s) || s == "null";
            }
            return false;
        }

        static void SetIfEmpty(JsonObject obj, string key, string? ocrValue, bool asNullWhenEmpty = false)
        {
            obj.TryGetPropertyValue(key, out var existing);
            if (!IsEmpty(existing))
                return;

            if (string.IsNullOrWhiteSpace(ocrValue))
            {
                if (asNullWhenEmpty)
                    obj[key] = null;
                return;
            }

            obj[key] = ocrValue.Trim();
        }

        static JsonObject EnsureObject(JsonObject parent, string key)
        {
            if (parent.TryGetPropertyValue(key, out var node) && node is JsonObject existing)
                return existing;
            var created = new JsonObject();
            parent[key] = created;
            return created;
        }

        var vendor = EnsureObject(payload, "Vendor");
        SetIfEmpty(vendor, "vendorId", Ocr(ocrByName, "Vendor Id", "VendorId"));
        SetIfEmpty(vendor, "company", Ocr(ocrByName, "Supplier Name", "Company"));
        SetIfEmpty(vendor, "address1", Ocr(ocrByName, "Address", "Address1"));
        SetIfEmpty(vendor, "city", Ocr(ocrByName, "City"));
        SetIfEmpty(vendor, "state", Ocr(ocrByName, "State"));
        SetIfEmpty(vendor, "zip", Ocr(ocrByName, "Zip", "Postal Code"));
        SetIfEmpty(vendor, "country", Ocr(ocrByName, "Country"));
        SetIfEmpty(vendor, "email", Ocr(ocrByName, "Email"));
        SetIfEmpty(vendor, "contactName", Ocr(ocrByName, "Contact Name"));
        SetIfEmpty(vendor, "glid", Ocr(ocrByName, "GLID", "Glid"));

        var invoice = EnsureObject(payload, "Invoice");
        SetIfEmpty(invoice, "documentType", Ocr(ocrByName, "Document Type"));
        if (!invoice.TryGetPropertyValue("documentType", out var dt) || IsEmpty(dt))
            invoice["documentType"] = "Invoice";

        SetIfEmpty(invoice, "fidNumber", Ocr(ocrByName, "FID Number"), asNullWhenEmpty: true);
        SetIfEmpty(invoice, "invoiceNumber", Ocr(ocrByName, "Invoice Number"));
        SetIfEmpty(invoice, "invoiceDate", Ocr(ocrByName, "Invoice Date"));
        SetIfEmpty(invoice, "poNumber", Ocr(ocrByName, "PO Number"));
        SetIfEmpty(invoice, "poDate", Ocr(ocrByName, "PO Date"));
        SetIfEmpty(invoice, "deliveryDocNumber", Ocr(ocrByName, "Delivery Doc Number"), asNullWhenEmpty: true);
        SetIfEmpty(invoice, "deliveryDocDate", Ocr(ocrByName, "Delivery Doc Date"), asNullWhenEmpty: true);
        SetIfEmpty(invoice, "currency", Ocr(ocrByName, "Currency"));
        if (!invoice.TryGetPropertyValue("currency", out var cur) || IsEmpty(cur))
            invoice["currency"] = "CAD";

        var buyer = EnsureObject(invoice, "buyer");
        SetIfEmpty(buyer, "billToName", Ocr(ocrByName, "Bill To", "Billing To"));
        SetIfEmpty(buyer, "billToAddress", Ocr(ocrByName, "Bill To Address", "Address"));
        SetIfEmpty(buyer, "shipToName", Ocr(ocrByName, "Ship To Name"), asNullWhenEmpty: true);
        SetIfEmpty(buyer, "shipToAddress", Ocr(ocrByName, "Ship To Address"), asNullWhenEmpty: true);

        var amounts = EnsureObject(invoice, "amounts");
        SetIfEmpty(amounts, "grossAmount", Ocr(ocrByName, "Subtotal", "Gross Amount"), asNullWhenEmpty: true);
        SetIfEmpty(amounts, "taxAmount", Ocr(ocrByName, "Tax (13%)", "Tax(13%)", "Tax Amount"), asNullWhenEmpty: true);
        SetIfEmpty(amounts, "discount", Ocr(ocrByName, "Discount"), asNullWhenEmpty: true);
        SetIfEmpty(amounts, "deposit", Ocr(ocrByName, "Deposit"), asNullWhenEmpty: true);
        SetIfEmpty(amounts, "charge", Ocr(ocrByName, "Charge"), asNullWhenEmpty: true);
        SetIfEmpty(amounts, "roundOff", Ocr(ocrByName, "Round Off", "RoundOff"), asNullWhenEmpty: true);
        SetIfEmpty(amounts, "netTotal", Ocr(ocrByName, "Total Due", "Net Total"), asNullWhenEmpty: true);

        SetIfEmpty(invoice, "paymentTerms", Ocr(ocrByName, "Payment Terms"));
        SetIfEmpty(invoice, "notes", Ocr(ocrByName, "Notes"));

        var remittance = EnsureObject(invoice, "remittance");
        SetIfEmpty(remittance, "bankName", Ocr(ocrByName, "Remit To", "Bank Name"));
        SetIfEmpty(remittance, "bankAccount", Ocr(ocrByName, "Bank Account"));
        SetIfEmpty(remittance, "bankAccountNumber", Ocr(ocrByName, "Bank Account Number"));

        // Line items: keep DB rows when present; otherwise use OCR table.
        var hasDbLines = invoice.TryGetPropertyValue("lineItems", out var linesNode)
            && linesNode is JsonArray { Count: > 0 } dbLines
            && dbLines.Any(x => x is JsonObject);
        if (!hasDbLines)
            invoice["lineItems"] = BuildAccess2PayLineItemsFromOcr(ocrByName);

        if (payload.TryGetPropertyValue("submission", out var subNode) && subNode is JsonObject submission)
        {
            var company = vendor.TryGetPropertyValue("company", out var c) ? c?.ToString() : null;
            if (!string.IsNullOrWhiteSpace(company) && company != "null")
                SetIfEmpty(submission, "emailSubject", company);
        }
    }

    private static JsonArray BuildAccess2PayLineItemsFromOcr(Dictionary<string, JsonElement> ocrByName)
    {
        var lineItems = new JsonArray();
        if (!TryGetOcrValue(ocrByName, "Line Items", out var items) || items.ValueKind != JsonValueKind.Array)
            return lineItems;

        var rowNo = 1;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var description = GetPropString(item, "Description");
            var type = GetPropString(item, "Type");
            var quantity = GetPropString(item, "Quantity");
            var rate = StripCurrency(GetPropString(item, "Unit Price", "Rate"));
            var amount = GetPropString(item, "Amount");
            var itemNumber = GetPropString(item, "Item Number", "ItemNumber");
            var uom = GetPropString(item, "Unit Of Measure", "UOM", "unitOfMeasure");

            var metaLineNo = 1;
            var metadata = new JsonArray();
            void AddMeta(string header, string value)
            {
                metadata.Add(new JsonObject
                {
                    ["lineNumber"] = metaLineNo++,
                    ["header"] = header,
                    ["dataType"] = "SHORT_TEXT",
                    ["value"] = value ?? ""
                });
            }

            AddMeta("Description", description);
            AddMeta("Type", type);
            AddMeta("Quantity", quantity);
            AddMeta("Rate", rate);
            AddMeta("Amount", amount);

            lineItems.Add(new JsonObject
            {
                ["lineNumber"] = rowNo++,
                ["itemNumber"] = string.IsNullOrWhiteSpace(itemNumber) ? null : itemNumber,
                ["description"] = description,
                ["quantity"] = quantity,
                ["unitOfMeasure"] = uom ?? "",
                ["rate"] = rate,
                ["lineAmount"] = amount,
                ["metadata"] = metadata
            });
        }

        return lineItems;
    }

    private async Task<string?> GetTenantConnectionStringAsync(string tenantId)
    {
        var mainCs = _apiKeyService.GetConnectionString();
        if (string.IsNullOrWhiteSpace(mainCs)) return null;

        try
        {
            await using var main = new SqlConnection(mainCs);
            await main.OpenAsync();
            await using var cmd = new SqlCommand(
                @"SELECT TOP 1 connectionString
                  FROM tenant
                  WHERE id=@tid AND (isDeleted=0 OR isDeleted IS NULL)", main);
            cmd.Parameters.AddWithValue("@tid", tenantId);
            var val = await cmd.ExecuteScalarAsync();
            return val?.ToString()?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to load connectionString for tenant {TenantId}", tenantId);
            return null;
        }
    }

    private static string GetJsonPropAsString(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var el)) return "";
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? "",
            JsonValueKind.Null => "",
            _ => el.GetRawText()
        };
    }

    private async Task<int> ResolveLatestProcessIdAsync(int workflowId)
    {
        var tenantCs = await GetTenantConnectionStringAsync(_tenantId);
        if (string.IsNullOrWhiteSpace(tenantCs) || workflowId <= 0) return 0;

        try
        {
            await using var conn = new SqlConnection(tenantCs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                $"SELECT TOP 1 id FROM process_{workflowId} ORDER BY id DESC", conn);
            var val = await cmd.ExecuteScalarAsync();
            return val != null && val != DBNull.Value ? Convert.ToInt32(val) : 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to resolve latest process id for workflow {WorkflowId}", workflowId);
            return 0;
        }
    }

    private static (int ProcessId, int TransactionId, int FormEntryId, string? RequestNo) ExtractTransactionIds(string? txOutput)
    {
        var doc = UnwrapJson(txOutput ?? "");
        if (doc == null)
            return (0, 0, 0, null);

        var processId = FindIntDeep(doc.RootElement, "processId", "processid", "pId");
        var transactionId = FindIntDeep(doc.RootElement, "transactionId", "transactionid", "tId");
        if (transactionId == 0)
            transactionId = processId;
        var formEntryId = FindIntDeep(doc.RootElement, "formEntryId", "formentryId", "formEntryid", "entryId", "entryid");
        var requestNo = FindStringDeep(doc.RootElement, "requestNo", "referenceNo", "requestNumber");

        if (processId == 0)
            processId = ReadIntProp(doc.RootElement, "id");

        return (processId, transactionId, formEntryId, requestNo);
    }

    private static int FindIntDeep(JsonElement root, params string[] names)
    {
        var direct = ReadIntProp(root, names);
        if (direct != 0) return direct;

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                var found = FindIntDeep(prop.Value, names);
                if (found != 0) return found;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var found = FindIntDeep(item, names);
                if (found != 0) return found;
            }
        }

        return 0;
    }

    private static string? FindStringDeep(JsonElement root, params string[] names)
    {
        var direct = ReadStringProp(root, names);
        if (!string.IsNullOrWhiteSpace(direct)) return direct;

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                var found = FindStringDeep(prop.Value, names);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var found = FindStringDeep(item, names);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
        }

        return null;
    }

    private static int ReadIntProp(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyIgnoreCase(root, name, out var el)) continue;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s)) return s;
        }

        return 0;
    }

    private static string? ReadStringProp(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyIgnoreCase(root, name, out var el)) continue;
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            else if (el.ValueKind == JsonValueKind.Number)
            {
                return el.GetRawText();
            }
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (root.TryGetProperty(name, out value))
            return true;

        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private async Task<ResultForHttpsCode> UploadForStaticMetadataAsync(
        byte[] bytes,
        string fileName,
        string token,
        CancellationToken cancellationToken)
    {
        var url = $"{_ezofisBaseUrl}/api/uploadAndIndex/uploadforStaticMetadata";
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(_repositoryId.ToString(CultureInfo.InvariantCulture)), "repositoryId");
        content.Add(new StringContent(JsonSerializer.Serialize(OcrFormFields)), "formFields");
        content.Add(CreateFileContent(bytes, fileName), "file", fileName);
        content.Add(new StringContent("false"), "isReturnJson");
        content.Add(new StringContent("1"), "validateType");

        return await SendMultipartAsync(url, token, content, "uploadforStaticMetadata", cancellationToken);
    }

    private async Task<ResultForHttpsCode> UploadIndexedAsync(
        byte[] bytes,
        string fileName,
        string fieldsJson,
        string token,
        CancellationToken cancellationToken)
    {
        var url = $"{_ezofisBaseUrl}/api/uploadAndIndex/upload";
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(_repositoryId.ToString(CultureInfo.InvariantCulture)), "repositoryId");
        content.Add(new StringContent(fileName), "filename");
        content.Add(new StringContent(fileName), "fileName");
        content.Add(new StringContent(""), "itemId");
        content.Add(new StringContent(""), "workspaceId");
        content.Add(new StringContent("true"), "isVerified");
        content.Add(new StringContent(""), "comments");
        content.Add(new StringContent(fieldsJson), "fields");
        content.Add(new StringContent(_workflowId.ToString(CultureInfo.InvariantCulture)), "workflowId");
        content.Add(CreateFileContent(bytes, fileName), "file", fileName);
        content.Add(new StringContent("false"), "isValidateFile");
        content.Add(new StringContent(""), "formFields");

        return await SendMultipartAsync(url, token, content, "upload", cancellationToken);
    }

    private async Task<ResultForHttpsCode> SubmitTransactionAsync(
        string jsonBody,
        string token,
        CancellationToken cancellationToken)
    {
        var path = _transactionPath.StartsWith('/') ? _transactionPath : "/" + _transactionPath;
        var url = $"{_ezofisBaseUrl}{path}";

        try
        {
            // 1) Encrypt plaintext transaction JSON via encryptAES
            // EncMiddleware strips surrounding quotes then DecryptStringAES on the body.
            var (encOk, cipherOrErr) = await _formDetailsService.EncryptAesAsync(token, jsonBody);
            if (!encOk || string.IsNullOrWhiteSpace(cipherOrErr))
                return Error(cipherOrErr ?? "encryptAES failed for transaction payload");

            // 2) POST encrypted ciphertext to /api/workflow/transaction
            // EncMiddleware does Substring(1,len-2) then FromBase64String — it does NOT JSON-parse.
            // Default System.Text.Json escapes '+' as \u002B which breaks Base64. Match curl: "<cipher>".
            var cipher = cipherOrErr.Trim().Trim('"');
            var wireBody = "\"" + cipher + "\"";

            var client = _httpClientFactory.CreateClient("InvoiceOcr");
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("Token", NormalizeBearerToken(token));
            request.Headers.TryAddWithoutValidation("Accept", "text/plain");
            request.Content = new StringContent(wireBody, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), "application/json");

            var response = await client.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Error($"transaction error {(int)response.StatusCode}: {text}");

            var responseBody = UnwrapQuotedString(text.Trim());
            if (responseBody.StartsWith("3..", StringComparison.Ordinal)
                || responseBody.StartsWith("1..", StringComparison.Ordinal)
                || responseBody.StartsWith("2..", StringComparison.Ordinal)
                || responseBody.StartsWith("4..", StringComparison.Ordinal))
            {
                return Error($"transaction middleware error: {responseBody}");
            }

            // Response is already plaintext (GENERICAPI / EncMiddleware) — return as-is.
            return Ok(responseBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "transaction call failed");
            return Error("transaction failed: " + ex.Message);
        }
    }

    private static string UnwrapQuotedString(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var current = text.Trim();
        for (var i = 0; i < 3; i++)
        {
            if (!(current.Length >= 2 && current[0] == '"' && current[^1] == '"'))
                break;
            try
            {
                current = JsonSerializer.Deserialize<string>(current)?.Trim() ?? current;
            }
            catch
            {
                break;
            }
        }

        return current;
    }

    private static string NormalizeBearerToken(string token)
    {
        var t = token.Trim();
        if (t.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return t;
        return "Bearer " + t;
    }

    private async Task<ResultForHttpsCode> SendMultipartAsync(
        string url,
        string token,
        MultipartFormDataContent content,
        string stepName,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("InvoiceOcr");
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("Token", token);
            request.Headers.TryAddWithoutValidation("Accept", "text/plain");
            request.Content = content;

            var response = await client.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Error($"{stepName} error {(int)response.StatusCode}: {text}");

            return Ok(text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Step} call failed", stepName);
            return Error($"{stepName} failed: {ex.Message}");
        }
    }

    private static ByteArrayContent CreateFileContent(byte[] bytes, string fileName)
    {
        var content = new ByteArrayContent(bytes);
        var mediaType = fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "application/octet-stream";
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return content;
    }

    private string BuildRepositoryFieldsJson(Dictionary<string, JsonElement> ocrByName)
    {
        var firstLine = GetFirstLineItem(ocrByName);
        var list = new List<object>();

        foreach (var (id, name, type) in RepositoryFields)
        {
            object value = "";

            // Prefer non-empty OCR value under the exact repo field name.
            if (TryGetNonEmptyOcrValue(ocrByName, name, out var direct))
            {
                value = direct;
            }
            else if (string.Equals(name, "Billing To", StringComparison.OrdinalIgnoreCase)
                     && TryGetNonEmptyOcrValue(ocrByName, "Bill To", out var billTo))
            {
                value = billTo;
            }
            else if (string.Equals(name, "Tax(13%)", StringComparison.OrdinalIgnoreCase)
                     && TryGetNonEmptyOcrValue(ocrByName, "Tax (13%)", out var tax))
            {
                value = tax;
            }
            else if (firstLine != null)
            {
                if (string.Equals(name, "Description", StringComparison.OrdinalIgnoreCase))
                    value = GetPropString(firstLine.Value, "Description");
                else if (string.Equals(name, "Type", StringComparison.OrdinalIgnoreCase))
                    value = GetPropString(firstLine.Value, "Type");
                else if (string.Equals(name, "Quantity", StringComparison.OrdinalIgnoreCase))
                    value = GetPropString(firstLine.Value, "Quantity");
                else if (string.Equals(name, "Rate", StringComparison.OrdinalIgnoreCase))
                    value = StripCurrency(GetPropString(firstLine.Value, "Unit Price", "Rate"));
                else if (string.Equals(name, "Amount", StringComparison.OrdinalIgnoreCase))
                    value = GetPropString(firstLine.Value, "Amount");
            }

            list.Add(new { id, name, type, value });
        }

        return JsonSerializer.Serialize(list);
    }

    /// <summary>
    /// Returns true when OCR has a usable (non-whitespace) value for <paramref name="name"/>.
    /// Strings become string values; other JSON kinds stay as JsonNode.
    /// </summary>
    private static bool TryGetNonEmptyOcrValue(
        Dictionary<string, JsonElement> ocrByName,
        string name,
        out object value)
    {
        value = "";
        if (!TryGetOcrValue(ocrByName, name, out var el) || el.ValueKind == JsonValueKind.Null)
            return false;

        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(s))
                return false;
            value = s;
            return true;
        }

        value = JsonNode.Parse(el.GetRawText())!;
        return true;
    }

    private string BuildTransactionPayload(
        Dictionary<string, JsonElement> ocrByName,
        List<FormControlRow> controls,
        long fileId,
        string fileName,
        long fileSize,
        string createdBy)
    {
        var topLevel = controls.Where(c => c.ParentId == 0 && !IsDeleted(c)).ToList();
        var childrenByParent = controls
            .Where(c => c.ParentId != 0 && !IsDeleted(c))
            .GroupBy(c => c.ParentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var fields = new JsonObject();
        string? fileUploadJsonId = null;

        foreach (var control in topLevel)
        {
            if (string.Equals(control.Type, "FILE_UPLOAD", StringComparison.OrdinalIgnoreCase))
            {
                fileUploadJsonId = control.JsonId;
                var fileMeta = new[]
                {
                    new
                    {
                        name = fileName,
                        size = fileSize,
                        uploadedPercentage = 100,
                        fileId,
                        createdBy,
                        createdAt = FormatCreatedAt(DateTime.Now)
                    }
                };
                fields[control.JsonId] = JsonSerializer.Serialize(fileMeta);
                continue;
            }

            if (string.Equals(control.Type, "TABLE", StringComparison.OrdinalIgnoreCase))
            {
                var childCols = childrenByParent.TryGetValue(control.Id, out var kids)
                    ? kids
                    : new List<FormControlRow>();
                fields[control.JsonId] = BuildLineItemRows(ocrByName, childCols);
                continue;
            }

            if (string.Equals(control.Type, "SINGLE_SELECT", StringComparison.OrdinalIgnoreCase)
                && string.Equals(control.Name, "Status", StringComparison.OrdinalIgnoreCase))
            {
                fields[control.JsonId] = "";
                continue;
            }

            if (TryGetOcrValue(ocrByName, control.Name, out var ocrVal))
            {
                fields[control.JsonId] = ocrVal.ValueKind switch
                {
                    JsonValueKind.String => ocrVal.GetString() ?? "",
                    JsonValueKind.Number => ocrVal.GetRawText(),
                    JsonValueKind.True or JsonValueKind.False => ocrVal.GetRawText(),
                    _ => ocrVal.GetRawText()
                };
            }
            else
            {
                fields[control.JsonId] = "";
            }
        }

        fileUploadJsonId ??= topLevel
            .FirstOrDefault(c => string.Equals(c.Type, "FILE_UPLOAD", StringComparison.OrdinalIgnoreCase))
            ?.JsonId ?? "0DsN_Q-0avkqC-t8hjbkK";

        var payload = new JsonObject
        {
            ["workflowId"] = _workflowId,
            ["review"] = DefaultReview,
            ["comments"] = new JsonArray(),
            ["formData"] = new JsonObject
            {
                ["formId"] = _formId.ToString(CultureInfo.InvariantCulture),
                ["fields"] = fields,
                ["formUpload"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["jsonId"] = fileUploadJsonId,
                        ["fileIds"] = new JsonArray { fileId },
                        ["rowid"] = 0
                    }
                }
            },
            ["fileIds"] = new JsonArray(),
            ["fileChecklistStatus"] = 0,
            ["fileInfo"] = new JsonArray(),
            ["portalId"] = _portalId,
            ["hasFormPDF"] = 0
        };

        return payload.ToJsonString();
    }

    private static JsonArray BuildLineItemRows(
        Dictionary<string, JsonElement> ocrByName,
        List<FormControlRow> childCols)
    {
        var rows = new JsonArray();
        if (!TryGetOcrValue(ocrByName, "Line Items", out var lineItems)
            || lineItems.ValueKind != JsonValueKind.Array)
            return rows;

        foreach (var item in lineItems.EnumerateArray())
        {
            var row = new JsonObject();
            foreach (var col in childCols)
            {
                var value = col.Name switch
                {
                    var n when EqualsNorm(n, "Description") => GetPropString(item, "Description"),
                    var n when EqualsNorm(n, "Type") => GetPropString(item, "Type"),
                    var n when EqualsNorm(n, "Quantity") => GetPropRaw(item, "Quantity"),
                    var n when EqualsNorm(n, "Rate") => StripCurrency(GetPropString(item, "Unit Price", "Rate")),
                    var n when EqualsNorm(n, "Amount") => GetPropString(item, "Amount"),
                    _ => GetPropString(item, col.Name)
                };

                if (col.Name.Equals("Quantity", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var qtyInt))
                {
                    row[col.JsonId] = qtyInt;
                }
                else if (col.Name.Equals("Quantity", StringComparison.OrdinalIgnoreCase)
                         && double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                             NumberStyles.Any, CultureInfo.InvariantCulture, out var qtyDbl))
                {
                    row[col.JsonId] = qtyDbl;
                }
                else
                {
                    row[col.JsonId] = value?.ToString() ?? "";
                }
            }

            rows.Add(row);
        }

        return rows;
    }

    private async Task<List<FormControlRow>> LoadFormControlsAsync(int formId)
    {
        var list = new List<FormControlRow>();
        var cs = _apiKeyService.GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs))
            return FallbackFormControls();

        try
        {
            await using var conn = new SqlConnection(cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                @"SELECT id, wFormId, jsonId, name, type, parentId, isDeleted
                  FROM wformcontrols
                  WHERE wFormId = @formId
                  ORDER BY id", conn);
            cmd.Parameters.AddWithValue("@formId", formId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new FormControlRow
                {
                    Id = reader.GetInt32(0),
                    FormId = reader.GetInt32(1),
                    JsonId = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Name = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Type = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    ParentId = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5)),
                    IsDeleted = reader.IsDBNull(6) ? 0 : Convert.ToInt32(reader.GetValue(6))
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load wformcontrols for form {FormId}; using fallback map", formId);
            return FallbackFormControls();
        }

        return list.Count > 0 ? list : FallbackFormControls();
    }

    private async Task<string?> ResolveTenantEmailAsync(string tenantId)
    {
        var cs = _apiKeyService.GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return null;

        try
        {
            await using var conn = new SqlConnection(cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT TOP 1 email FROM tenant WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", tenantId);
            var result = await cmd.ExecuteScalarAsync();
            var email = result?.ToString();
            if (!string.IsNullOrWhiteSpace(email)) return email;

            await using var cmd2 = new SqlCommand(
                "SELECT TOP 1 email FROM tenantUser WHERE tenantId = @id ORDER BY id", conn);
            cmd2.Parameters.AddWithValue("@id", tenantId);
            var result2 = await cmd2.ExecuteScalarAsync();
            return result2?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to resolve email for tenant {TenantId}", tenantId);
            return null;
        }
    }

    private static List<FormControlRow> FallbackFormControls() =>
    [
        new() { Id = 34048, FormId = 141, JsonId = "4aLUV4JxBiKL8wQgFhebs", Name = "Invoice Number", Type = "SHORT_TEXT", ParentId = 0 },
        new() { Id = 34049, FormId = 141, JsonId = "TaeCgq7OzxoyAA0o2v8ix", Name = "Supplier Name", Type = "SHORT_TEXT", ParentId = 0 },
        new() { Id = 34050, FormId = 141, JsonId = "HxtFeDMScd1LPjrYf6v8T", Name = "PO Number", Type = "SHORT_TEXT", ParentId = 0 },
        new() { Id = 34051, FormId = 141, JsonId = "fj3TUS79vGvd_ifEdOsE4", Name = "Invoice Date", Type = "DATE", ParentId = 0 },
        new() { Id = 34052, FormId = 141, JsonId = "-k3D7jU77XRWgtfcil1dG", Name = "Address", Type = "LONG_TEXT", ParentId = 0 },
        new() { Id = 34053, FormId = 141, JsonId = "gsA2hzqoY9fMCwm42hKrh", Name = "Bill To", Type = "SHORT_TEXT", ParentId = 0 },
        new() { Id = 34054, FormId = 141, JsonId = "FmdAgT2UC6LC1xsUP6_BH", Name = "Line Items", Type = "TABLE", ParentId = 0 },
        new() { Id = 34055, FormId = 141, JsonId = "zlWNOsO9uKLmi4OFPwwPL", Name = "Description", Type = "SHORT_TEXT", ParentId = 34054 },
        new() { Id = 34056, FormId = 141, JsonId = "Qjqsx2a95LUijC2j8LQ9i", Name = "Type", Type = "SHORT_TEXT", ParentId = 34054 },
        new() { Id = 34057, FormId = 141, JsonId = "e57PUxiqXSefFbDp5wt3I", Name = "Quantity", Type = "SHORT_TEXT", ParentId = 34054 },
        new() { Id = 34058, FormId = 141, JsonId = "ila1u-gw9OiASx5-QeOTa", Name = "Rate", Type = "SHORT_TEXT", ParentId = 34054 },
        new() { Id = 34059, FormId = 141, JsonId = "cJ9aKTkwvdSTv_XK5Z9dM", Name = "Amount", Type = "SHORT_TEXT", ParentId = 34054 },
        new() { Id = 34060, FormId = 141, JsonId = "z4hqS0dfNuqd8jIneIW3H", Name = "Subtotal", Type = "SHORT_TEXT", ParentId = 0 },
        new() { Id = 34061, FormId = 141, JsonId = "MC5iT7wcEMrKk4cmqg-7h", Name = "Tax (13%)", Type = "SHORT_TEXT", ParentId = 0 },
        new() { Id = 34062, FormId = 141, JsonId = "ZGN3pv6w7DJsQYWPhWNFW", Name = "Total Due", Type = "SHORT_TEXT", ParentId = 0 },
        new() { Id = 34063, FormId = 141, JsonId = "UVbvHIwz-amlBAGs7VV1n", Name = "Payment Terms", Type = "SHORT_TEXT", ParentId = 0 },
        new() { Id = 34064, FormId = 141, JsonId = "9yKjxAOWa39mM21dAtvxM", Name = "Remit To", Type = "SHORT_TEXT", ParentId = 0 },
        new() { Id = 34065, FormId = 141, JsonId = "Au2F1GHCn86aMqEgmGikV", Name = "Status", Type = "SINGLE_SELECT", ParentId = 0 },
        new() { Id = 34066, FormId = 141, JsonId = "0DsN_Q-0avkqC-t8hjbkK", Name = "Invoice Document", Type = "FILE_UPLOAD", ParentId = 0 }
    ];

    private static Dictionary<string, JsonElement> BuildOcrLookup(JsonDocument doc)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (!doc.RootElement.TryGetProperty("ocrResult", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var item in arr.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var nameEl)) continue;
            var name = nameEl.GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (item.TryGetProperty("value", out var valueEl))
                map[name] = valueEl.Clone();
        }

        return map;
    }

    private static bool TryGetOcrValue(Dictionary<string, JsonElement> map, string name, out JsonElement value)
    {
        if (map.TryGetValue(name, out value))
            return true;

        var target = NormalizeName(name);
        foreach (var kv in map)
        {
            if (NormalizeName(kv.Key) == target)
            {
                value = kv.Value;
                return true;
            }
        }

        // Billing To ↔ Bill To, Tax(13%) ↔ Tax (13%)
        if (target is "billingto" or "billto")
        {
            foreach (var kv in map)
            {
                var n = NormalizeName(kv.Key);
                if (n is "billingto" or "billto")
                {
                    value = kv.Value;
                    return true;
                }
            }
        }

        if (target.Contains("tax", StringComparison.Ordinal) && target.Contains("13", StringComparison.Ordinal))
        {
            foreach (var kv in map)
            {
                var n = NormalizeName(kv.Key);
                if (n.Contains("tax", StringComparison.Ordinal) && n.Contains("13", StringComparison.Ordinal))
                {
                    value = kv.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string GetOcrString(Dictionary<string, JsonElement> map, string name)
    {
        return TryGetOcrValue(map, name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? ""
            : "";
    }

    private static JsonElement? GetFirstLineItem(Dictionary<string, JsonElement> map)
    {
        if (!TryGetOcrValue(map, "Line Items", out var items) || items.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var item in items.EnumerateArray())
            return item.Clone();
        return null;
    }

    private static string NormalizeName(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static bool EqualsNorm(string a, string b) =>
        NormalizeName(a) == NormalizeName(b);

    private static bool IsDeleted(FormControlRow row) => row.IsDeleted != 0;

    private static string GetPropString(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var el))
            {
                return el.ValueKind switch
                {
                    JsonValueKind.String => el.GetString() ?? "",
                    JsonValueKind.Number => el.GetRawText(),
                    JsonValueKind.True or JsonValueKind.False => el.GetRawText(),
                    _ => el.GetRawText()
                };
            }
        }

        return "";
    }

    private static object GetPropRaw(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetProperty(name, out var el)) continue;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d)) return d;
            if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? "";
            return el.GetRawText();
        }

        return "";
    }

    private static string StripCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return value.Replace("$", "", StringComparison.Ordinal).Trim();
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "invoice" : name.Trim();
    }

    private static string FormatCreatedAt(DateTime dt) =>
        dt.ToString("yyyy-MM-dd h:mm tt", CultureInfo.GetCultureInfo("en-US")).ToLowerInvariant()
            .Replace(" am", " a.m.", StringComparison.Ordinal)
            .Replace(" pm", " p.m.", StringComparison.Ordinal);

    private static bool TryGetFileId(JsonElement root, out long fileId)
    {
        fileId = 0;
        if (root.TryGetProperty("fileId", out var el))
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out fileId))
                return true;
            if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out fileId))
                return true;
        }

        return false;
    }

    private static JsonDocument? UnwrapJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var current = text.Trim();

        for (var i = 0; i < 5; i++)
        {
            try
            {
                using var probe = JsonDocument.Parse(current);
                if (probe.RootElement.ValueKind == JsonValueKind.String)
                {
                    current = probe.RootElement.GetString()?.Trim() ?? current;
                    continue;
                }

                return JsonDocument.Parse(current);
            }
            catch (JsonException)
            {
                if (current.Length >= 2 && current[0] == '"' && current[^1] == '"')
                {
                    try
                    {
                        current = JsonSerializer.Deserialize<string>(current)?.Trim() ?? current;
                        continue;
                    }
                    catch
                    {
                        return null;
                    }
                }

                return null;
            }
        }

        try { return JsonDocument.Parse(current); }
        catch { return null; }
    }

    private static object? UnwrapToObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var doc = UnwrapJson(text);
        if (doc == null) return text;
        return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText());
    }

    private static ResultForHttpsCode Ok(string output) => new()
    {
        id = 1,
        output = output,
        EncryptOutput = null
    };

    private static ResultForHttpsCode Error(string message) => new()
    {
        id = 0,
        EncryptOutput = message
    };

    private sealed class FormControlRow
    {
        public int Id { get; init; }
        public int FormId { get; init; }
        public string JsonId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
        public int ParentId { get; init; }
        public int IsDeleted { get; init; }
    }
}
