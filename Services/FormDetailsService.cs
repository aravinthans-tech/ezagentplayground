using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using QRCodeAPI.Models;

namespace QRCodeAPI.Services;

/// <summary>
/// Proxies Ezofis form list API with optional AES encrypt/decrypt via ez tapi.
/// </summary>
public class FormDetailsService
{
    public const string EncryptedPayloadPropertyName = "encryptedPayload";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FormDetailsService> _logger;
    private readonly string _ezofisBaseUrl;

    private static readonly JsonSerializerOptions QueryJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions NoUnicodeEscapeJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public FormDetailsService(
        IHttpClientFactory httpClientFactory,
        ILogger<FormDetailsService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _ezofisBaseUrl = (configuration["ExternalApis:Ezofis:BaseUrl"] ?? "https://eztapi.ezofis.com").TrimEnd('/');
    }

    /// <summary>
    /// Encrypt payload using Ezofis encryptAES.
    /// Sends JSON text directly (no wrapper object).
    /// </summary>
    public async Task<(bool ok, string? cipherOrError)> EncryptAesAsync(string bearerToken, string plainText)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
            return (false, "Ezofis token is required");

        var httpClient = _httpClientFactory.CreateClient();
        var url = $"{_ezofisBaseUrl}/api/authentication/encryptAES";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Token", NormalizeBearerToken(bearerToken));
        // Local middleware trims first/last char; send as JSON string literal so it unwraps to valid JSON object.
        var middlewareSafePayload = " " + JsonSerializer.Serialize(plainText) + " ";
        request.Content = new StringContent(middlewareSafePayload, Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return (false, $"encryptAES error {(int)response.StatusCode}: {body}");

        var trimmed = body.Trim();
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"') && trimmed.Length >= 2)
        {
            try
            {
                return (true, JsonSerializer.Deserialize<string>(trimmed));
            }
            catch
            {
                /* fall through */
            }
        }

        return (true, trimmed);
    }

    public async Task<(bool ok, string? plainOrError)> DecryptAesAsync(string bearerToken, string encryptedText)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
            return (false, "Ezofis token is required");

        var httpClient = _httpClientFactory.CreateClient();
        var url = $"{_ezofisBaseUrl}/api/authentication/decryptAES";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Token", NormalizeBearerToken(bearerToken));
        request.Content = new StringContent(
            JsonSerializer.Serialize(encryptedText, NoUnicodeEscapeJsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return (false, $"decryptAES error {(int)response.StatusCode}: {body}");

        var trimmed = body.Trim();
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"') && trimmed.Length >= 2)
        {
            try
            {
                return (true, JsonSerializer.Deserialize<string>(trimmed));
            }
            catch
            {
                // keep as-is
            }
        }
        return (true, trimmed);
    }

    public async Task<ResultForHttpsCode> GetFormDetailsAsync(string ezofisBearerToken, FormDetailsApiRequest apiRequest)
    {
        var result = new ResultForHttpsCode();

        if (apiRequest.Query == null)
        {
            result.id = 0;
            result.EncryptOutput = "query is required";
            return result;
        }

        if (string.IsNullOrWhiteSpace(ezofisBearerToken))
        {
            result.id = 0;
            result.EncryptOutput = "Ezofis JWT is required (Authorization: Bearer or Ezofis-Token header)";
            return result;
        }

        try
        {
            var queryJson = JsonSerializer.Serialize(apiRequest.Query, QueryJsonOptions);
            _logger.LogDebug("Form/all query JSON length: {Length}", queryJson.Length);

            string formAllBody;
            if (apiRequest.UseEncryptedRequest)
            {
                var (encOk, cipherOrErr) = await EncryptAesAsync(ezofisBearerToken, queryJson);
                if (!encOk || string.IsNullOrEmpty(cipherOrErr))
                {
                    result.id = 0;
                    result.EncryptOutput = cipherOrErr ?? "encryptAES failed";
                    return result;
                }
                // Send encrypted value directly as JSON string literal to form/all (no wrapper object key).
                // Keep Base64 characters like '+' intact (avoid \u002B escaping).
                formAllBody = JsonSerializer.Serialize(cipherOrErr, NoUnicodeEscapeJsonOptions);
            }
            else
            {
                formAllBody = queryJson;
            }

            var httpClient = _httpClientFactory.CreateClient();
            var formAllUrl = $"{_ezofisBaseUrl}/api/form/all";
            var formRequest = new HttpRequestMessage(HttpMethod.Post, formAllUrl);
            formRequest.Headers.TryAddWithoutValidation("Token", NormalizeBearerToken(ezofisBearerToken));
            formRequest.Content = new StringContent(formAllBody, Encoding.UTF8, "application/json");

            var formResponse = await httpClient.SendAsync(formRequest);
            var formResponseText = await formResponse.Content.ReadAsStringAsync();

            if (!formResponse.IsSuccessStatusCode)
            {
                result.id = 0;
                result.EncryptOutput = $"form/all error {(int)formResponse.StatusCode}: {formResponseText}";
                return result;
            }

        var responseTrim = formResponseText.Trim();
        string formsSourceJson;
        //if (apiRequest.UseEncryptedResponse)
        //{
        //    var (decOk, decOutputOrErr) = await DecryptAesAsync(ezofisBearerToken, responseTrim);
        //    if (!decOk || string.IsNullOrWhiteSpace(decOutputOrErr))
        //    {
        //        result.id = 0;
        //        result.EncryptOutput = decOutputOrErr ?? "decryptAES failed";
        //        return result;
        //    }

        //    formsSourceJson = decOutputOrErr;
        //}
        //else
        
            formsSourceJson = responseTrim;
        

        var forms = ExtractForms(formsSourceJson);
            result.id = 1;
            result.output = JsonSerializer.Serialize(forms, NoUnicodeEscapeJsonOptions);
            result.EncryptOutput = null;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FormDetails failed");
            result.id = 0;
            result.EncryptOutput = "ERROR: " + ex.Message;
            return result;
        }
    }

    public async Task<ResultForHttpsCode> GetSubFormDetailsAsync(string ezofisBearerToken, int formId)
    {
        var result = new ResultForHttpsCode();
        if (string.IsNullOrWhiteSpace(ezofisBearerToken))
        {
            result.id = 0;
            result.EncryptOutput = "Ezofis JWT is required";
            return result;
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var url = $"{_ezofisBaseUrl}/api/form/{formId}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Token", NormalizeBearerToken(ezofisBearerToken));

            var response = await httpClient.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                result.id = 0;
                result.EncryptOutput = $"form/{{id}} error {(int)response.StatusCode}: {responseText}";
                return result;
            }

            var normalized = NormalizeJsonDocumentText(responseText);
            var titles = ExtractPanelTitles(normalized);
            var outputPayload = new Dictionary<string, object>
            {
                ["subforms"] = titles.Select(x => x["title"]).ToList()
            };
            result.id = 1;
            result.output = JsonSerializer.Serialize(outputPayload, NoUnicodeEscapeJsonOptions);
            result.EncryptOutput = null;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SubFormDetails failed");
            result.id = 0;
            result.EncryptOutput = "ERROR: " + ex.Message;
            return result;
        }
    }

    public async Task<ResultForHttpsCode> GetSubFormFieldsAsync(string ezofisBearerToken, int formId, IReadOnlyCollection<string> subformTitles)
    {
        var result = new ResultForHttpsCode();
        if (string.IsNullOrWhiteSpace(ezofisBearerToken))
        {
            result.id = 0;
            result.EncryptOutput = "Ezofis JWT is required";
            return result;
        }

        if (subformTitles == null || subformTitles.Count == 0)
        {
            result.id = 0;
            result.EncryptOutput = "subforms titles are required";
            return result;
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var url = $"{_ezofisBaseUrl}/api/form/{formId}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Token", NormalizeBearerToken(ezofisBearerToken));

            var response = await httpClient.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                result.id = 0;
                result.EncryptOutput = $"form/{{id}} error {(int)response.StatusCode}: {responseText}";
                return result;
            }

            var normalized = NormalizeJsonDocumentText(responseText);
            var (commonFields, panelFields) = ExtractPanelFieldsByTitles(normalized, subformTitles);
            var outputPayload = new Dictionary<string, object>
            {
                ["commonFields"] = commonFields,
                ["fieldspanelname"] = panelFields
            };

            result.id = 1;
            result.output = JsonSerializer.Serialize(outputPayload, NoUnicodeEscapeJsonOptions);
            result.EncryptOutput = null;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SubFormFields failed");
            result.id = 0;
            result.EncryptOutput = "ERROR: " + ex.Message;
            return result;
        }
    }

    public async Task<ResultForHttpsCode> SubmitSubFormAndArchiveAsync(string ezofisBearerToken, SubFormSubmitAndArchiveApiRequest request)
    {
        var result = new ResultForHttpsCode();
        if (string.IsNullOrWhiteSpace(ezofisBearerToken))
        {
            result.id = 0;
            result.EncryptOutput = "Ezofis JWT is required";
            return result;
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();

            // Per API behavior: send only fields payload (encrypted), not a wrapper object.
            var entryPayloadJson = request.Fields.GetRawText();

            var (encOk, cipherOrErr) = await EncryptAesAsync(ezofisBearerToken, entryPayloadJson);
            if (!encOk || string.IsNullOrWhiteSpace(cipherOrErr))
            {
                result.id = 0;
                result.EncryptOutput = cipherOrErr ?? "encryptAES failed";
                return result;
            }

            var submitUrl = $"{_ezofisBaseUrl}/api/form/{request.FormId}/entry/{request.EntryIdForFormEntry}";
            var submitReq = new HttpRequestMessage(HttpMethod.Post, submitUrl);
            submitReq.Headers.TryAddWithoutValidation("Token", NormalizeBearerToken(ezofisBearerToken));
            submitReq.Content = new StringContent(
                JsonSerializer.Serialize(cipherOrErr, NoUnicodeEscapeJsonOptions),
                Encoding.UTF8,
                "application/json");

            var submitRes = await httpClient.SendAsync(submitReq);
            var submitText = await submitRes.Content.ReadAsStringAsync();
            if (!submitRes.IsSuccessStatusCode)
            {
                result.id = 0;
                result.EncryptOutput = $"form entry error {(int)submitRes.StatusCode}: {submitText}";
                return result;
            }

            // form/{id}/entry response is encrypted; decrypt and read output as entry id.
            var submitEncrypted = submitText.Trim();
            

            var extractedEntryId = ExtractEntryIdFromFormEntryResult(submitEncrypted);
            var effectiveEntryId = request.EntryId > 0 ? request.EntryId : extractedEntryId;
            if (effectiveEntryId <= 0)
            {
                result.id = 0;
                result.EncryptOutput = "Unable to resolve entryId from form entry output. Provide entryId explicitly.";
                return result;
            }

            var archiveUrl =
                $"{_ezofisBaseUrl}/api/form/generatePdfUploadAndArchive" +
                $"?templateid={request.TemplateId}" +
                $"&formid={request.FormId}" +
                $"&entryid={effectiveEntryId}" +
                $"&panelname={Uri.EscapeDataString(request.PanelName ?? string.Empty)}" +
                $"&ismultiple={request.IsMultiple.ToString().ToLowerInvariant()}";

            var archiveReq = new HttpRequestMessage(HttpMethod.Post, archiveUrl);
            archiveReq.Headers.TryAddWithoutValidation("Token", NormalizeBearerToken(ezofisBearerToken));
            var archiveRes = await httpClient.SendAsync(archiveReq);
            var archiveText = await archiveRes.Content.ReadAsStringAsync();
            if (!archiveRes.IsSuccessStatusCode)
            {
                result.id = 0;
                result.EncryptOutput = $"generatePdfUploadAndArchive error {(int)archiveRes.StatusCode}: {archiveText}";
                return result;
            }

            var archiveEncrypted = archiveText.Trim();
            //var (archiveDecOk, archiveDecTextOrErr) = await DecryptAesAsync(ezofisBearerToken, archiveEncrypted);
            //if (!archiveDecOk || string.IsNullOrWhiteSpace(archiveDecTextOrErr))
            //{
            //    result.id = 0;
            //    result.EncryptOutput = archiveDecTextOrErr ?? "decryptAES failed for generatePdfUploadAndArchive response";
            //    return result;
            //}

            var output = new Dictionary<string, object?>
            {
                ["entryId"] = effectiveEntryId,
                ["submitEntryResponse"] = TryParseJsonOrRaw(submitEncrypted),
                ["archiveResponse"] = TryParseJsonOrRaw(archiveEncrypted)
            };

            result.id = 1;
            result.output = JsonSerializer.Serialize(output, NoUnicodeEscapeJsonOptions);
            result.EncryptOutput = null;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SubmitSubFormAndArchive failed");
            result.id = 0;
            result.EncryptOutput = "ERROR: " + ex.Message;
            return result;
        }
    }

    public async Task<ResultForHttpsCode> GetDataFromSalesforceAsync(string ezofisBearerToken, JsonElement payload)
    {
        var result = new ResultForHttpsCode();
        if (string.IsNullOrWhiteSpace(ezofisBearerToken))
        {
            result.id = 0;
            result.EncryptOutput = "Ezofis JWT is required";
            return result;
        }

        try
        {
            var payloadJson = payload.GetRawText();
            var (encOk, cipherOrErr) = await EncryptAesAsync(ezofisBearerToken, payloadJson);
            if (!encOk || string.IsNullOrWhiteSpace(cipherOrErr))
            {
                result.id = 0;
                result.EncryptOutput = cipherOrErr ?? "encryptAES failed";
                return result;
            }

            var httpClient = _httpClientFactory.CreateClient();
            var url = $"{_ezofisBaseUrl}/api/file/GetDataFromSalesforce";
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("Token", NormalizeBearerToken(ezofisBearerToken));
            req.Content = new StringContent(
                JsonSerializer.Serialize(cipherOrErr, NoUnicodeEscapeJsonOptions),
                Encoding.UTF8,
                "application/json");

            var res = await httpClient.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                result.id = 0;
                result.EncryptOutput = $"GetDataFromSalesforce error {(int)res.StatusCode}: {text}";
                return result;
            }

            var (decOk, decTextOrErr) = await DecryptAesAsync(ezofisBearerToken, text.Trim());
            if (!decOk || string.IsNullOrWhiteSpace(decTextOrErr))
            {
                result.id = 0;
                result.EncryptOutput = decTextOrErr ?? "decryptAES failed";
                return result;
            }

            result.id = 1;
            result.output = prettyJsonIfPossible(NormalizeJsonDocumentText(decTextOrErr));
            result.EncryptOutput = null;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetDataFromSalesforce failed");
            result.id = 0;
            result.EncryptOutput = "ERROR: " + ex.Message;
            return result;
        }
    }

    /// <summary>
    /// If response parses as JSON object/array, treat as plaintext to encrypt; otherwise assume ciphertext.
    /// </summary>
    private static bool ShouldEncryptResponse(string trimmed)
    {
        if (string.IsNullOrEmpty(trimmed))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            return doc.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }

    private static List<Dictionary<string, object>> ExtractForms(string decryptedJson)
    {
        var forms = new List<Dictionary<string, object>>();
        try
        {
            using var doc = JsonDocument.Parse(decryptedJson);
            if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                dataEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var dataItem in dataEl.EnumerateArray())
                {
                    if (!dataItem.TryGetProperty("value", out var valueEl) || valueEl.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var form in valueEl.EnumerateArray())
                    {
                        if (form.TryGetProperty("name", out var nameEl) &&
                            form.TryGetProperty("id", out var idEl))
                        {
                            var n = nameEl.GetString();
                            if (!string.IsNullOrWhiteSpace(n))
                            {
                                forms.Add(new Dictionary<string, object>
                                {
                                    ["formId"] = idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out var i) ? i : idEl.ToString(),
                                    ["name"] = n
                                });
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Return empty list if format is unexpected.
        }
        // Deduplicate by formId + name
        return forms
            .GroupBy(x => $"{x["formId"]}|{x["name"]}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static string NormalizeJsonDocumentText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var trimmed = text.Trim();
        var firstObj = trimmed.IndexOf('{');
        var firstArr = trimmed.IndexOf('[');
        var start = -1;
        if (firstObj >= 0 && firstArr >= 0) start = Math.Min(firstObj, firstArr);
        else if (firstObj >= 0) start = firstObj;
        else if (firstArr >= 0) start = firstArr;
        return start > 0 ? trimmed[start..] : trimmed;
    }

    private static string prettyJsonIfPossible(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
        catch
        {
            return text;
        }
    }

    private static List<Dictionary<string, string>> ExtractPanelTitles(string jsonText)
    {
        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("formJson", out var formJsonEl) &&
                formJsonEl.ValueKind == JsonValueKind.String)
            {
                var formJsonText = formJsonEl.GetString();
                if (!string.IsNullOrWhiteSpace(formJsonText))
                {
                    try
                    {
                        using var formDoc = JsonDocument.Parse(formJsonText);
                        if (formDoc.RootElement.TryGetProperty("panels", out var panelsEl) &&
                            panelsEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var panel in panelsEl.EnumerateArray())
                            {
                                if (panel.ValueKind != JsonValueKind.Object)
                                    continue;

                                if (!panel.TryGetProperty("settings", out var settingsEl) ||
                                    settingsEl.ValueKind != JsonValueKind.Object)
                                    continue;

                                if (!settingsEl.TryGetProperty("title", out var titleEl) ||
                                    titleEl.ValueKind != JsonValueKind.String)
                                    continue;

                                var t = titleEl.GetString();
                                if (!string.IsNullOrWhiteSpace(t))
                                    titles.Add(t.Trim());
                            }
                        }
                    }
                    catch
                    {
                        // Ignore malformed formJson
                    }
                }
            }
        }
        catch
        {
            // Keep empty list for unexpected payload.
        }

        return titles
            .Select(t => new Dictionary<string, string> { ["title"] = t })
            .ToList();
    }

    private static (List<Dictionary<string, string>> commonFields, List<Dictionary<string, object>> panelFields) ExtractPanelFieldsByTitles(string jsonText, IReadOnlyCollection<string> subformTitles)
    {
        var wanted = new HashSet<string>(
            subformTitles.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var output = new List<Dictionary<string, object>>();

        if (wanted.Count == 0)
            return (new List<Dictionary<string, string>>(), output);

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("formJson", out var formJsonEl) ||
                formJsonEl.ValueKind != JsonValueKind.String)
                return (new List<Dictionary<string, string>>(), output);

            var formJsonText = formJsonEl.GetString();
            if (string.IsNullOrWhiteSpace(formJsonText))
                return (new List<Dictionary<string, string>>(), output);

            using var formDoc = JsonDocument.Parse(formJsonText);
            if (!formDoc.RootElement.TryGetProperty("panels", out var panelsEl) ||
                panelsEl.ValueKind != JsonValueKind.Array)
                return (new List<Dictionary<string, string>>(), output);

            var commonFields = new List<Dictionary<string, string>>();
            var titledPanels = new List<(string title, List<Dictionary<string, string>> fields)>();

            foreach (var panel in panelsEl.EnumerateArray())
            {
                if (panel.ValueKind != JsonValueKind.Object)
                    continue;

                var panelTitle = GetPanelTitle(panel);
                var fields = new List<Dictionary<string, string>>();
                if (panel.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in fieldsEl.EnumerateArray())
                    {
                        if (field.ValueKind != JsonValueKind.Object)
                            continue;
                        if (!field.TryGetProperty("label", out var labelEl) || labelEl.ValueKind != JsonValueKind.String)
                            continue;
                        var label = labelEl.GetString();
                        if (!string.IsNullOrWhiteSpace(label))
                        {
                            var type = field.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                                ? (typeEl.GetString() ?? "")
                                : "";
                            var fieldId = field.TryGetProperty("id", out var idEl)
                                ? idEl.ToString()
                                : "";
                            fields.Add(new Dictionary<string, string>
                            {
                                ["id"] = fieldId,
                                ["label"] = label.Trim(),
                                ["type"] = type
                            });
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(panelTitle))
                {
                    // Untitled panel fields are treated as shared/common fields.
                    commonFields.AddRange(fields);
                    continue;
                }

                if (wanted.Contains(panelTitle))
                    titledPanels.Add((panelTitle, fields));
            }

            var distinctCommon = commonFields
                .Where(x => x.TryGetValue("label", out var l) && !string.IsNullOrWhiteSpace(l))
                .GroupBy(x => $"{x["id"]}|{x["label"]}|{x["type"]}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            foreach (var panel in titledPanels)
            {
                var panelOnlyFields = panel.fields
                    .Where(x => x.TryGetValue("label", out var l) && !string.IsNullOrWhiteSpace(l))
                    .GroupBy(x => $"{x["id"]}|{x["label"]}|{x["type"]}", StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                output.Add(new Dictionary<string, object>
                {
                    ["panelName"] = panel.title,
                    ["fields"] = panelOnlyFields
                });
            }

            return (distinctCommon, output);
        }
        catch
        {
            // Return empty on unexpected payload.
            return (new List<Dictionary<string, string>>(), output);
        }
    }

    private static string? GetPanelTitle(JsonElement panel)
    {
        if (!panel.TryGetProperty("settings", out var settingsEl) || settingsEl.ValueKind != JsonValueKind.Object)
            return null;
        if (!settingsEl.TryGetProperty("title", out var titleEl) || titleEl.ValueKind != JsonValueKind.String)
            return null;
        var title = titleEl.GetString();
        return string.IsNullOrWhiteSpace(title) ? null : title.Trim();
    }

    private static int ExtractEntryId(string responseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
            return ExtractEntryId(doc.RootElement);
        }
        catch
        {
            return 0;
        }
    }

    private static int ExtractEntryIdFromFormEntryResult(string decryptedResponseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(decryptedResponseText);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return 0;

            if (doc.RootElement.TryGetProperty("output", out var outputEl))
            {
                if (outputEl.ValueKind == JsonValueKind.Number && outputEl.TryGetInt32(out var n) && n > 0)
                    return n;

                if (outputEl.ValueKind == JsonValueKind.String &&
                    int.TryParse(outputEl.GetString(), out var parsed) &&
                    parsed > 0)
                    return parsed;
            }

            return ExtractEntryId(doc.RootElement);
        }
        catch
        {
            return 0;
        }
    }

    private static int ExtractEntryId(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if ((prop.Name.Equals("entryId", StringComparison.OrdinalIgnoreCase) ||
                     prop.Name.Equals("id", StringComparison.OrdinalIgnoreCase)) &&
                    prop.Value.ValueKind == JsonValueKind.Number &&
                    prop.Value.TryGetInt32(out var id) && id > 0)
                {
                    return id;
                }

                var nested = ExtractEntryId(prop.Value);
                if (nested > 0) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = ExtractEntryId(item);
                if (nested > 0) return nested;
            }
        }

        return 0;
    }

    private static object TryParseJsonOrRaw(string text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText()) ?? trimmed;
        }
        catch
        {
            return trimmed;
        }
    }

    /// <summary>
    /// encryptAES uses the same header style as decryptAES in FileSummaryService.
    /// </summary>
    private static string NormalizeBearerToken(string token)
    {
        var t = token.Trim();
        if (t.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return t;
        return "Bearer " + t;
    }

}
