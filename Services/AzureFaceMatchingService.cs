using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;

namespace QRCodeAPI.Services;

/// <summary>
/// Face matching service using Azure Face API.
/// This implementation uses Azure Cognitive Services Face API via REST API,
/// eliminating the need for native OpenCV libraries.
/// </summary>
public class AzureFaceMatchingService : IFaceMatchingService, IDisposable
{
    private readonly ILogger<AzureFaceMatchingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly string _apiEndpoint;
    private readonly string _subscriptionKey;
    private bool _disposed = false;

    public AzureFaceMatchingService(
        ILogger<AzureFaceMatchingService> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient("AzureFaceAPI");
        
        // Get Azure Face API configuration
        _apiEndpoint = _configuration["ExternalApis:AzureFace:Endpoint"] 
            ?? throw new InvalidOperationException("Azure Face API endpoint not configured. Please set ExternalApis:AzureFace:Endpoint in appsettings.json");
        
        _subscriptionKey = _configuration["ExternalApis:AzureFace:SubscriptionKey"]
            ?? throw new InvalidOperationException("Azure Face API subscription key not configured. Please set ExternalApis:AzureFace:SubscriptionKey in appsettings.json");

        // Configure HttpClient for Azure Face API
        _httpClient.BaseAddress = new Uri(_apiEndpoint);
        _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _subscriptionKey);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        _logger.LogInformation("AzureFaceMatchingService initialized with endpoint: {Endpoint}", _apiEndpoint);
    }

    /// <summary>
    /// Compares faces from license and selfie images using Azure Face API.
    /// </summary>
    public async Task<(byte[]? licenseFace, byte[]? selfieFace, bool match, int matchScore, string message)> ProcessAndCompare(
        IFormFile licenseImage,
        IFormFile selfieImage)
    {
        try
        {
            _logger.LogInformation("Starting face matching with Azure Face API");

            // Step 1: Detect faces in both images
            var licenseFaceId = await DetectFaceAsync(licenseImage, "license");
            var selfieFaceId = await DetectFaceAsync(selfieImage, "selfie");

            if (string.IsNullOrEmpty(licenseFaceId))
            {
                return (null, null, false, 0, "❌ Photo Verification Failed<br>No face detected in license image.");
            }

            if (string.IsNullOrEmpty(selfieFaceId))
            {
                return (null, null, false, 0, "❌ Photo Verification Failed<br>No face detected in selfie image.");
            }

            // Step 2: Verify if faces match
            var (isIdentical, confidence) = await VerifyFacesAsync(licenseFaceId, selfieFaceId);

            // Convert confidence (0.0-1.0) to match score (0-5) for consistency with OpenCV implementation
            var matchScore = (int)Math.Round(confidence * 5);
            var threshold = _configuration.GetValue<int>("KycVerification:FaceMatchThreshold", 4);
            var match = isIdentical && matchScore >= threshold;

            var resultMessage = match
                ? $"✅ Photo Verification Passed<br>Match Score: {matchScore}/5 (Confidence: {confidence:P0})"
                : $"❌ Photo Verification Failed<br>Match Score: {matchScore}/5 (Confidence: {confidence:P0}, Required: {threshold}/5)";

            // Azure Face API doesn't return cropped face images, so we return null for images
            // The UI can still display the original images if needed
            return (null, null, match, matchScore, resultMessage);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling Azure Face API");
            return (null, null, false, 0, $"❌ Face matching error: Unable to connect to Azure Face API. {ex.Message}");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Timeout calling Azure Face API");
            return (null, null, false, 0, "❌ Face matching error: Request to Azure Face API timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Azure Face API face matching");
            return (null, null, false, 0, $"❌ Face matching error: {ex.Message}");
        }
    }

    /// <summary>
    /// Detects a face in the given image and returns the face ID.
    /// </summary>
    private async Task<string?> DetectFaceAsync(IFormFile image, string imageType)
    {
        try
        {
            _logger.LogDebug("Detecting face in {ImageType} image", imageType);

            // Read image bytes
            byte[] imageBytes;
            using (var stream = image.OpenReadStream())
            using (var memoryStream = new MemoryStream())
            {
                await stream.CopyToAsync(memoryStream);
                imageBytes = memoryStream.ToArray();
            }

            // Prepare request - use relative path since BaseAddress is already set
            var requestUri = "/face/v1.0/detect?returnFaceId=true&returnFaceLandmarks=false&returnFaceAttributes=";
            var content = new ByteArrayContent(imageBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            // Call Azure Face API detect endpoint
            var response = await _httpClient.PostAsync(requestUri, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Azure Face API detect failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var faceDetections = JsonSerializer.Deserialize<JsonElement[]>(jsonResponse);

            if (faceDetections == null || faceDetections.Length == 0)
            {
                _logger.LogWarning("No faces detected in {ImageType} image", imageType);
                return null;
            }

            // Get the first (largest) face
            var firstFace = faceDetections[0];
            if (firstFace.TryGetProperty("faceId", out var faceIdElement))
            {
                var faceId = faceIdElement.GetString();
                _logger.LogInformation("Face detected in {ImageType} image. FaceId: {FaceId}", imageType, faceId);
                return faceId;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting face in {ImageType} image", imageType);
            return null;
        }
    }

    /// <summary>
    /// Verifies if two faces belong to the same person.
    /// </summary>
    private async Task<(bool isIdentical, double confidence)> VerifyFacesAsync(string faceId1, string faceId2)
    {
        try
        {
            _logger.LogDebug("Verifying faces: {FaceId1} and {FaceId2}", faceId1, faceId2);

            // Prepare request body
            var requestBody = new
            {
                faceId1 = faceId1,
                faceId2 = faceId2
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Call Azure Face API verify endpoint - use relative path since BaseAddress is already set
            var requestUri = "/face/v1.0/verify";
            var response = await _httpClient.PostAsync(requestUri, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Azure Face API verify failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return (false, 0.0);
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var verifyResult = JsonSerializer.Deserialize<JsonElement>(jsonResponse);

            if (verifyResult.TryGetProperty("isIdentical", out var isIdenticalElement) &&
                verifyResult.TryGetProperty("confidence", out var confidenceElement))
            {
                var isIdentical = isIdenticalElement.GetBoolean();
                var confidence = confidenceElement.GetDouble();
                
                _logger.LogInformation("Face verification result: IsIdentical={IsIdentical}, Confidence={Confidence}", 
                    isIdentical, confidence);
                
                return (isIdentical, confidence);
            }

            return (false, 0.0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying faces");
            return (false, 0.0);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _httpClient?.Dispose();
            }
            _disposed = true;
        }
    }
}

