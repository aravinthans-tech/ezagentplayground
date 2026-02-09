using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;

namespace QRCodeAPI.Services;

public class DocumentProcessingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DocumentProcessingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _unstractApiKey;
    private readonly string _unstractBaseUrl;
    private readonly string _openRouterApiKey;
    private readonly string _openRouterBaseUrl;

    public DocumentProcessingService(
        IHttpClientFactory httpClientFactory,
        ILogger<DocumentProcessingService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
        _unstractApiKey = _configuration["ExternalApis:Unstract:ApiKey"] ?? string.Empty;
        _unstractBaseUrl = _configuration["ExternalApis:Unstract:BaseUrl"] ?? "https://llmwhisperer-api.us-central.unstract.com/api/v2";
        _openRouterApiKey = _configuration["ExternalApis:OpenRouter:ApiKey"] ?? string.Empty;
        _openRouterBaseUrl = _configuration["ExternalApis:OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";
    }

    public async Task<string> ExtractTextFromFile(IFormFile file)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            var httpClient = _httpClientFactory.CreateClient("Unstract");
            _logger.LogInformation("Starting OCR text extraction for file: {FileName}", file.FileName);
            var fileName = file.FileName;
            var contentType = file.ContentType ?? "application/octet-stream";

            // Step 1: Upload file to Unstract Whisperer
            using var fileStream = file.OpenReadStream();
            using var content = new StreamContent(fileStream);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

            var uploadRequest = new HttpRequestMessage(HttpMethod.Post, $"{_unstractBaseUrl}/whisper");
            uploadRequest.Headers.Add("unstract-key", _unstractApiKey);
            uploadRequest.Content = content;

            var uploadResponse = await httpClient.SendAsync(uploadRequest);
            if (uploadResponse.StatusCode != System.Net.HttpStatusCode.OK && uploadResponse.StatusCode != System.Net.HttpStatusCode.Accepted)
            {
                var errorContent = await uploadResponse.Content.ReadAsStringAsync();
                if (uploadResponse.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    throw new Exception($"Unstract OCR service temporarily unavailable (503). Please try again in a few moments. Error: {errorContent}");
                }
                throw new Exception($"OCR upload failed ({uploadResponse.StatusCode}): {errorContent}");
            }

            var uploadContent = await uploadResponse.Content.ReadAsStringAsync();
            JsonElement uploadJson;
            try
            {
                uploadJson = JsonSerializer.Deserialize<JsonElement>(uploadContent);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse upload response JSON: {Content}", uploadContent);
                throw new Exception($"Invalid JSON response from OCR upload: {uploadContent.Substring(0, Math.Min(100, uploadContent.Length))}");
            }

            if (!uploadJson.TryGetProperty("whisper_hash", out var whisperHash))
            {
                throw new Exception($"No whisper_hash in response: {uploadContent}");
            }
            var token = whisperHash.GetString() ?? throw new Exception("whisper_hash is null");

            // Step 2: Poll for status (up to 3 minutes) - optimized polling interval
            for (int pollCount = 0; pollCount < 360; pollCount++) // 360 * 500ms = 180 seconds (3 minutes)
            {
                await Task.Delay(500); // Reduced from 1000ms to 500ms for faster response
                var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"{_unstractBaseUrl}/whisper-status?whisper_hash={token}");
                statusRequest.Headers.Add("unstract-key", _unstractApiKey);

                var statusResponse = await httpClient.SendAsync(statusRequest);
                if (!statusResponse.IsSuccessStatusCode)
                {
                    var errorContent = await statusResponse.Content.ReadAsStringAsync();
                    if (statusResponse.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                    {
                        throw new Exception($"Unstract OCR service temporarily unavailable (503). Please try again in a few moments. Error: {errorContent}");
                    }
                    throw new Exception($"Status check failed ({statusResponse.StatusCode}): {errorContent}");
                }

                var statusContent = await statusResponse.Content.ReadAsStringAsync();
                JsonElement statusJson;
                try
                {
                    statusJson = JsonSerializer.Deserialize<JsonElement>(statusContent);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse status response JSON: {Content}", statusContent);
                    throw new Exception($"Invalid JSON response from status check: {statusContent.Substring(0, Math.Min(100, statusContent.Length))}");
                }

                if (!statusJson.TryGetProperty("status", out var statusProp))
                {
                    throw new Exception($"No status in response: {statusContent}");
                }
                var status = statusProp.GetString();

                if (status == "processed")
                {
                    break;
                }
                else if (status == "failed")
                {
                    throw new Exception($"Unstract Whisperer processing failed: {statusJson}");
                }
            }

            // Step 3: Retrieve result
            var retrieveRequest = new HttpRequestMessage(HttpMethod.Get, $"{_unstractBaseUrl}/whisper-retrieve?whisper_hash={token}&text_only=true");
            retrieveRequest.Headers.Add("unstract-key", _unstractApiKey);

            var retrieveResponse = await httpClient.SendAsync(retrieveRequest);
            
            // Check if response is successful
            if (!retrieveResponse.IsSuccessStatusCode)
            {
                var errorContent = await retrieveResponse.Content.ReadAsStringAsync();
                if (retrieveResponse.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    throw new Exception($"Unstract OCR service temporarily unavailable (503). Please try again in a few moments. Error: {errorContent}");
                }
                throw new Exception($"Failed to retrieve OCR result: {retrieveResponse.StatusCode} - {errorContent}");
            }

            // Check content type and response format
            var responseContentType = retrieveResponse.Content.Headers.ContentType?.MediaType ?? "";
            var responseContent = await retrieveResponse.Content.ReadAsStringAsync();
            
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogInformation("OCR text extraction completed in {ElapsedSeconds:F2} seconds", elapsedTime);
            
            // When text_only=true, Unstract API returns plain text (text/plain content type)
            // Handle text/plain explicitly as it's the expected format
            if (responseContentType.Contains("text/plain"))
            {
                // Response is plain text OCR result (expected when text_only=true)
                _logger.LogInformation("Received plain text OCR result (Content-Type: text/plain)");
                return responseContent.Trim();
            }
            // Try to parse as JSON if content type is JSON or response looks like JSON
            else if (responseContentType.Contains("application/json") || responseContent.TrimStart().StartsWith("{"))
            {
                try
                {
                    var retrieveJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    if (retrieveJson.TryGetProperty("result_text", out var resultText))
                    {
                        var extractedText = resultText.GetString();
                        if (!string.IsNullOrWhiteSpace(extractedText))
                        {
                            return extractedText;
                        }
                    }
                    // If result_text not found or empty, check if the JSON itself contains text
                    // Some APIs might return the text in a different property
                    if (retrieveJson.TryGetProperty("text", out var textProp))
                    {
                        var textValue = textProp.GetString();
                        if (!string.IsNullOrWhiteSpace(textValue))
                        {
                            return textValue;
                        }
                    }
                    // If no text property found, return the full JSON as string (fallback)
                    return responseContent;
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse JSON response, treating as plain text");
                    // If JSON parsing fails, treat as plain text OCR result
                    return responseContent.Trim();
                }
            }
            else
            {
                // Unknown content type, but treat as plain text (most likely OCR result)
                _logger.LogInformation("Received response with content type: {ContentType}, treating as plain text", responseContentType);
                return responseContent.Trim();
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.CancellationToken.IsCancellationRequested)
        {
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "Timeout error extracting text from file after {ElapsedSeconds:F2} seconds. Request may have exceeded timeout limit.", elapsedTime);
            throw new Exception($"OCR request timed out after {elapsedTime:F2} seconds. The file may be too large or the service is slow. Please try again.");
        }
        catch (TimeoutException ex)
        {
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "Timeout error extracting text from file after {ElapsedSeconds:F2} seconds", elapsedTime);
            throw new Exception($"OCR request timed out after {elapsedTime:F2} seconds. Please try again.");
        }
        catch (Exception ex)
        {
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "Error extracting text from file after {ElapsedSeconds:F2} seconds", elapsedTime);
            throw;
        }
    }

    public async Task<string> ExtractAddressWithLLM(string text, string modelChoice)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            var httpClient = _httpClientFactory.CreateClient("OpenRouter");
            _logger.LogInformation("Starting address extraction with LLM (Model: {Model})", modelChoice);
            var modelMap = new Dictionary<string, string>
            {
                { "Mistral", "mistralai/Mistral-7B-Instruct-v0.2" },
                { "OpenAI", "openai/gpt-4o" }
            };

            var modelName = modelMap.GetValueOrDefault(modelChoice, "mistralai/Mistral-7B-Instruct-v0.2");

            string template;
            if (modelChoice == "Mistral")
            {
                template = @"You are a strict document parser extracting addresses from identity documents.

Your task is to extract ONLY the full mailing address from the document text. The address format depends on the country:

**For Canadian addresses:**
- House/building number, Street name, City, Province (two-letter code like ON, NL), Postal code (A1A 1A1 format)
- Example: 742 Evergreen Terrace, Ottawa, ON K1A 0B1

**For Indian addresses:**
- House/building number, Street name, Area/Locality, City, State (full name like Tamil Nadu or abbreviation like TN), PIN code (6 digits)
- Example: 10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004
- Example: 10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, TN 627004

**IMPORTANT RULES:**
- DO NOT include section numbers (e.g., '8.', '9.', '8)', '9)') or labels like 'Eyes:', 'Class:', etc.
- Ignore any lines starting with numbers followed by a dot or parenthesis (e.g., '8.', '8.2', '9)') as these are section headers, not addresses.
- The address should begin with the actual building number (e.g., '10 F2', '2', '742')
- Never assume or hallucinate building numbers.
- If multiple addresses exist, pick the one that is clearly a residential or mailing address.
- If no address is found, return ""None"" (exactly this word, no quotes).

Return ONLY the address in one line. No extra words, explanations, or labels.

Text:
{document_text}

Extracted Address:";
            }
            else
            {
                template = @"Extract the full mailing address from the following text. Include street, city, state/province, and postal/PIN code. Support both Canadian and Indian address formats. If no address is found, return ""None"".

Text: {document_text}

Address:";
            }

            var prompt = template.Replace("{document_text}", text);

            var requestBody = new
            {
                model = modelName,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.2,
                max_tokens = 2000
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_openRouterBaseUrl}/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {_openRouterApiKey}");
            request.Headers.Add("HTTP-Referer", "https://ezofis.com");
            request.Headers.Add("X-Title", "EZOFIS KYC Agent");
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(request);
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("OpenRouter API request failed after {ElapsedSeconds:F2} seconds. Status: {StatusCode}, Error: {Error}", elapsedTime, response.StatusCode, errorContent);
                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    throw new Exception($"OpenRouter API service temporarily unavailable (503). Please try again in a few moments. Error: {errorContent}");
                }
                throw new Exception($"OpenRouter API request failed ({response.StatusCode}): {errorContent}");
            }

            _logger.LogInformation("Address extraction completed in {ElapsedSeconds:F2} seconds", elapsedTime);
            var responseJson = await response.Content.ReadFromJsonAsync<JsonElement>();
            var extractedAddress = responseJson.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

            // Clean and validate the extracted address
            var cleanedAddress = CleanAddressMistral(extractedAddress, text);
            
            // If address is empty or "None", try to extract from text as fallback
            if (string.IsNullOrWhiteSpace(cleanedAddress) || cleanedAddress.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("LLM returned empty or 'None' for address. Attempting fallback extraction from text.");
                // Try to extract address directly from text using regex patterns
                cleanedAddress = ExtractAddressFallback(text);
            }
            
            return cleanedAddress;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.CancellationToken.IsCancellationRequested)
        {
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "Timeout error extracting address with LLM after {ElapsedSeconds:F2} seconds. Attempting fallback extraction.", elapsedTime);
            // On timeout, try fallback extraction instead of throwing
            try
            {
                return ExtractAddressFallback(text);
            }
            catch
            {
                _logger.LogError("Fallback address extraction also failed");
                return "None";
            }
        }
        catch (TimeoutException ex)
        {
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "Timeout error extracting address with LLM after {ElapsedSeconds:F2} seconds. Attempting fallback extraction.", elapsedTime);
            // On timeout, try fallback extraction instead of throwing
            try
            {
                return ExtractAddressFallback(text);
            }
            catch
            {
                _logger.LogError("Fallback address extraction also failed");
                return "None";
            }
        }
        catch (Exception ex)
        {
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "Error extracting address with LLM after {ElapsedSeconds:F2} seconds. Attempting fallback extraction.", elapsedTime);
            // On error, try fallback extraction instead of throwing
            try
            {
                return ExtractAddressFallback(text);
            }
            catch
            {
                _logger.LogError("Fallback address extraction also failed");
                return "None";
            }
        }
    }

    public async Task<Dictionary<string, object>> ExtractKycFields(string text, string modelChoice)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            var httpClient = _httpClientFactory.CreateClient("OpenRouter");
            _logger.LogInformation("Starting KYC fields extraction with LLM (Model: {Model})", modelChoice);
            var modelMap = new Dictionary<string, string>
            {
                { "Mistral", "mistralai/Mistral-7B-Instruct-v0.2" },
                { "OpenAI", "openai/gpt-4o" }
            };

            var modelName = modelMap.GetValueOrDefault(modelChoice, "mistralai/Mistral-7B-Instruct-v0.2");

            var template = @"
You are an expert KYC document parser. Extract only factual data from the document.
If any field is missing, set it to ""Not provided"". DO NOT infer.

The address must include building/house number, street, city, province, postal code.

Return only the JSON below:

{
  ""document_type"": ""string or 'Not provided'"",
  ""document_number"": ""string or 'Not provided'"",
  ""country_of_issue"": ""string or 'Not provided'"",
  ""issuing_authority"": ""string or 'Not provided'"",
  ""full_name"": ""string or 'Not provided'"",
  ""first_name"": ""string or 'Not provided'"",
  ""middle_name"": ""string or 'Not provided'"",
  ""last_name"": ""string or 'Not provided'"",
  ""gender"": ""string or 'Not provided'"",
  ""date_of_birth"": ""string or 'Not provided'"",
  ""place_of_birth"": ""string or 'Not provided'"",
  ""nationality"": ""string or 'Not provided'"",
  ""address"": ""string or 'Not provided'"",
  ""date_of_issue"": ""string or 'Not provided'"",
  ""date_of_expiry"": ""string or 'Not provided'"",
  ""blood_group"": ""string or 'Not provided'"",
  ""personal_id_number"": ""string or 'Not provided'"",
  ""father_name"": ""string or 'Not provided'"",
  ""mother_name"": ""string or 'Not provided'"",
  ""marital_status"": ""string or 'Not provided'"",
  ""photo_base64"": ""string or 'Not provided'"",
  ""signature_base64"": ""string or 'Not provided'"",
  ""additional_info"": ""string or 'Not provided'""
}

Text:
{text}
";

            var prompt = template.Replace("{text}", text);

            var requestBody = new
            {
                model = modelName,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.2,
                max_tokens = 2000
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_openRouterBaseUrl}/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {_openRouterApiKey}");
            request.Headers.Add("HTTP-Referer", "https://ezofis.com");
            request.Headers.Add("X-Title", "EZOFIS KYC Agent");
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(request);
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("OpenRouter API request failed after {ElapsedSeconds:F2} seconds. Status: {StatusCode}, Error: {Error}", elapsedTime, response.StatusCode, errorContent);
                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    throw new Exception($"OpenRouter API service temporarily unavailable (503). Please try again in a few moments. Error: {errorContent}");
                }
                throw new Exception($"OpenRouter API request failed ({response.StatusCode}): {errorContent}");
            }

            _logger.LogInformation("KYC fields extraction completed in {ElapsedSeconds:F2} seconds", elapsedTime);
            var responseJson = await response.Content.ReadFromJsonAsync<JsonElement>();
            var rawOutput = responseJson.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

            try
            {
                var jsonMatch = Regex.Match(rawOutput, @"\{[\s\S]+\}");
                if (jsonMatch.Success)
                {
                    var fields = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonMatch.Value) ?? new Dictionary<string, object>();
                    if (fields.ContainsKey("address") && fields["address"]?.ToString() != "Not provided")
                    {
                        fields["address"] = CleanAddressMistral(fields["address"]?.ToString() ?? string.Empty, text);
                    }
                    return FilterNonNullFields(fields);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse KYC fields JSON");
            }

            // Return default structure if parsing fails
            return new Dictionary<string, object>
            {
                { "error", "Failed to parse KYC fields" },
                { "raw_output", rawOutput }
            };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.CancellationToken.IsCancellationRequested)
        {
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "Timeout error extracting KYC fields after {ElapsedSeconds:F2} seconds. Request may have exceeded timeout limit.", elapsedTime);
            throw new Exception($"KYC fields extraction timed out after {elapsedTime:F2} seconds. Please try again.");
        }
        catch (TimeoutException ex)
        {
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "Timeout error extracting KYC fields after {ElapsedSeconds:F2} seconds", elapsedTime);
            throw new Exception($"KYC fields extraction timed out after {elapsedTime:F2} seconds. Please try again.");
        }
        catch (Exception ex)
        {
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogError(ex, "Error extracting KYC fields after {ElapsedSeconds:F2} seconds", elapsedTime);
            throw;
        }
    }

    private string ExtractAddressFallback(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "None";

        // Try to find Indian address pattern: number + street + city + state + PIN
        var indianAddressPattern = @"(\d+\s*[A-Z0-9/]*(?:\s+[A-Z][a-z]+)+(?:\s+Street|St|Road|Rd|Avenue|Ave|Lane|Ln|Nagar|Colony|Area|Locality|Pettai|Kovil|Street|St)?(?:\s*,\s*[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)*(?:\s*,\s*[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)*(?:\s*,\s*(?:Tamil Nadu|TN|Andhra Pradesh|AP|Maharashtra|MH|Karnataka|KA|Kerala|KL|Gujarat|GJ|Rajasthan|RJ|Madhya Pradesh|MP|Uttar Pradesh|UP|West Bengal|WB|Bihar|BR|Odisha|OR|Punjab|PB|Haryana|HR|Assam|AS|Jharkhand|JH|Chhattisgarh|CT|Himachal Pradesh|HP|Uttarakhand|UT|Goa|GA|Manipur|MN|Meghalaya|ML|Mizoram|MZ|Nagaland|NL|Tripura|TR|Sikkim|SK|Telangana|TG|Arunachal Pradesh|AR))(?:\s+\d{6})?)";
        var indianMatch = Regex.Match(text, indianAddressPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (indianMatch.Success)
        {
            var address = indianMatch.Groups[0].Value.Trim();
            if (address.Length > 10 && address.Length < 200) // Reasonable address length
            {
                return address;
            }
        }

        // Try to find Canadian address pattern
        var canadianAddressPattern = @"(\d+\s+[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*(?:\s+(?:Street|St|Road|Rd|Avenue|Ave|Lane|Ln|Drive|Dr|Boulevard|Blvd|Court|Ct|Crescent|Cres|Way|Terrace|Ter))(?:\s*,\s*[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)*(?:\s*,\s*[A-Z]{2})(?:\s+[A-Z]\d[A-Z]\s?\d[A-Z]\d)?)";
        var canadianMatch = Regex.Match(text, canadianAddressPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (canadianMatch.Success)
        {
            var address = canadianMatch.Groups[0].Value.Trim();
            if (address.Length > 10 && address.Length < 200)
            {
                return address;
            }
        }

        return "None";
    }

    private string CleanAddressMistral(string rawResponse, string originalText = "")
    {
        var flattened = rawResponse.Replace("\n", ", ").Replace("  ", " ").Trim();
        flattened = Regex.Replace(flattened, @"^(?:\s*(\d{1,2}(?:\.\d)?[\.\):])\s*)+", "");
        flattened = Regex.Replace(flattened, @"(?i)section\s*\d{1,2}(?:\.\d)?[\.\):]?\s*", "");
        flattened = Regex.Replace(flattened, @"^\d+\.\d+\s+", "");

        var match = Regex.Match(flattened, @"^\d{1,5}[A-Za-z\-]?\s+[\w\s.,'-]+?,\s*\w+,\s*[A-Z]{2},?\s*[A-Z]\d[A-Z][ ]?\d[A-Z]\d", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[0].Value.Trim();
        }

        var fallback = Regex.Match(originalText.Replace("\n", " "), @"^\d{1,5}[A-Za-z\-]?\s+[\w\s.,'-]+?,\s*\w+,\s*[A-Z]{2},?\s*[A-Z]\d[A-Z][ ]?\d[A-Z]\d", RegexOptions.IgnoreCase);
        if (fallback.Success)
        {
            return fallback.Groups[0].Value.Trim();
        }

        return flattened;
    }

    private Dictionary<string, object> FilterNonNullFields(Dictionary<string, object> data)
    {
        return data.Where(kvp => kvp.Value != null && 
            kvp.Value.ToString() != "null" && 
            kvp.Value.ToString() != "" && 
            kvp.Value.ToString() != "None" && 
            kvp.Value.ToString() != "Not provided")
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}

