using QRCodeAPI.Models;
using System.Text;
using System.Text.Json;

namespace QRCodeAPI.Services;

public class FileSummaryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FileSummaryService> _logger;
    private const string SUMMARY_API_BASE = "https://eztapi.ezofis.com";
    private const string DECRYPT_API_BASE = "https://eztapi.ezofis.com";
    private const string FILE_UPLOAD_API_BASE = "https://eztapi.ezofis.com";
    private const string REPOSITORY_ID = "1059";

    public FileSummaryService(IHttpClientFactory httpClientFactory, ILogger<FileSummaryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ResultForHttpsCode> GetFileSummary(string token, string rId, string itemId)
    {
        var result = new ResultForHttpsCode();

        try
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(rId) || string.IsNullOrWhiteSpace(itemId))
            {
                result.id = 0;
                result.EncryptOutput = "Token, rId, and itemId are required";
                return result;
            }

            var httpClient = _httpClientFactory.CreateClient();

            // Step 1: Call summary API
            var summaryUrl = $"{SUMMARY_API_BASE}/api/file/summary/{rId}/{itemId}";
            var summaryRequest = new HttpRequestMessage(HttpMethod.Get, summaryUrl);
            summaryRequest.Headers.Add("Token", token);
            summaryRequest.Headers.Add("Accept", "application/json");

            var summaryResponse = await httpClient.SendAsync(summaryRequest);

            if (!summaryResponse.IsSuccessStatusCode)
            {
                var errorContent = await summaryResponse.Content.ReadAsStringAsync();
                result.id = 0;
                result.EncryptOutput = $"Summary API error: {summaryResponse.StatusCode} - {errorContent}";
                return result;
            }

            var summaryContent = await summaryResponse.Content.ReadAsStringAsync();
            
            // The summaryContent is already the encrypted output, use it directly
            var encryptedOutput = summaryContent.Trim();

            if (string.IsNullOrEmpty(encryptedOutput))
            {
                result.id = 0;
                result.EncryptOutput = "No encrypted output received from summary API";
                return result;
            }

            // Step 2: Decrypt the output
            var decryptUrl = $"{DECRYPT_API_BASE}/api/authentication/decryptAES";
            var decryptRequest = new HttpRequestMessage(HttpMethod.Post, decryptUrl);
            decryptRequest.Headers.Add("Token", $"Bearer {token}");
            decryptRequest.Headers.Add("Accept", "text/plain");

            // Send encrypted string directly as body (not wrapped in JSON)
            decryptRequest.Content = new StringContent(
                JsonSerializer.Serialize(encryptedOutput), // Serialize the string to JSON string format
                Encoding.UTF8,
                "application/json"
            );

            var decryptResponse = await httpClient.SendAsync(decryptRequest);

            if (!decryptResponse.IsSuccessStatusCode)
            {
                result.id = 0;
                result.EncryptOutput = $"Decrypt API error: {decryptResponse.StatusCode} - {await decryptResponse.Content.ReadAsStringAsync()}";
                return result;
            }

            var decryptedHtml = await decryptResponse.Content.ReadAsStringAsync();

            // Return success with decrypted HTML
            result.id = 1;
            result.output = decryptedHtml;
            result.EncryptOutput = null;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file summary");
            result.id = 0;
            result.EncryptOutput = "ERROR CODE:WDBR740F300DB30 " + ex.ToString();
            return result;
        }
    }

    public async Task<string> UploadFileAndGetItemId(Stream fileStream, string fileName, string token)
    {
        try
        {
            if (fileStream == null || fileStream.Length == 0)
            {
                throw new ArgumentException("File stream is required");
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("Token is required");
            }

            var httpClient = _httpClientFactory.CreateClient();

            // Hardcoded fields JSON as string
            var fieldsJson = @"[
  {
    ""name"": ""Vendor"",
    ""value"": ""ABC Company"",
    ""type"": ""SINGLE_SELECT""
  },
  {
    ""name"": ""Document Type"",
    ""value"": ""Invoice"",
    ""type"": ""SINGLE_SELECT""
  },
  {
    ""name"": ""PO Number"",
    ""value"": ""890"",
    ""type"": ""SHORT_TEXT""
  },
  {
    ""name"": ""Document Number"",
    ""value"": ""90899"",
    ""type"": ""SHORT_TEXT""
  },
  {
    ""name"": ""Amount"",
    ""value"": ""500"",
    ""type"": ""SHORT_TEXT""
  },
  {
    ""name"": ""Items"",
    ""value"": [],
    ""type"": ""TABLE""
  },
  {
    ""name"": ""Document Date"",
    ""value"": ""2026-02-12"",
    ""type"": ""DATE""
  }
]";

            var uploadUrl = $"{FILE_UPLOAD_API_BASE}/api/file/fileArchiveWordPlugin";
            
            using (var multipartContent = new MultipartFormDataContent())
            {
                // Add file
                if (fileStream.CanSeek)
                {
                    fileStream.Position = 0;
                }
                var fileContent = new StreamContent(fileStream);
                // Determine content type from file extension
                var contentType = "application/pdf";
                if (fileName.EndsWith(".doc", StringComparison.OrdinalIgnoreCase) || 
                    fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                }
                else if (fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = "text/plain";
                }
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                multipartContent.Add(fileContent, "file", fileName);

                // Add repositoryId
                multipartContent.Add(new StringContent(REPOSITORY_ID), "repositoryId");

                // Add fields
                multipartContent.Add(new StringContent(fieldsJson), "fields");

                var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
                request.Headers.Add("Token", token);
                request.Headers.Add("Accept", "*/*");
                request.Content = multipartContent;
                
                _logger.LogDebug("Uploading file to {Url} with repositoryId {RepoId}", uploadUrl, REPOSITORY_ID);

                var response = await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("File upload API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    throw new Exception($"File upload failed: {response.StatusCode} - {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                
                // Check if response is just a number (not JSON object)
                responseContent = responseContent.Trim();
                if (int.TryParse(responseContent, out var numberValue))
                {
                    return numberValue.ToString();
                }
                
                // Try to parse as JSON object
                try
                {
                    using (var doc = JsonDocument.Parse(responseContent))
                    {
                        // If root element is a number, return it
                        if (doc.RootElement.ValueKind == JsonValueKind.Number)
                        {
                            return doc.RootElement.GetInt32().ToString();
                        }
                        
                        // If root element is a JSON object, look for itemId or id property
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            if (doc.RootElement.TryGetProperty("itemId", out var itemIdElement))
                            {
                                if (itemIdElement.ValueKind == JsonValueKind.Number)
                                {
                                    return itemIdElement.GetInt32().ToString();
                                }
                                else if (itemIdElement.ValueKind == JsonValueKind.String)
                                {
                                    return itemIdElement.GetString() ?? throw new Exception("itemId is null");
                                }
                                else
                                {
                                    return itemIdElement.ToString();
                                }
                            }
                            else if (doc.RootElement.TryGetProperty("id", out var idElement))
                            {
                                if (idElement.ValueKind == JsonValueKind.Number)
                                {
                                    return idElement.GetInt32().ToString();
                                }
                                else if (idElement.ValueKind == JsonValueKind.String)
                                {
                                    return idElement.GetString() ?? throw new Exception("id is null");
                                }
                                else
                                {
                                    return idElement.ToString();
                                }
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // If JSON parsing fails, response might be just a number string
                    // Already handled above with TryParse
                }
                
                throw new Exception($"Could not extract itemId from response: {responseContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file and getting itemId");
            throw;
        }
    }
}

