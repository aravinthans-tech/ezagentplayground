using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;

namespace QRCodeAPI.Services;

/// <summary>
/// Face matching service using Azure Face API.
/// Provides cloud-based face detection and verification without native dependencies.
/// </summary>
public class AzureFaceMatchingService
{
    private readonly ILogger<AzureFaceMatchingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _subscriptionKey;

    public AzureFaceMatchingService(
        ILogger<AzureFaceMatchingService> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient("AzureFaceAPI");

        // Get Azure Face API configuration
        _endpoint = _configuration["ExternalApis:AzureFaceApi:Endpoint"] 
            ?? throw new InvalidOperationException("Azure Face API endpoint not configured. Please set ExternalApis:AzureFaceApi:Endpoint in appsettings.json");
        
        _subscriptionKey = _configuration["ExternalApis:AzureFaceApi:SubscriptionKey"]
            ?? throw new InvalidOperationException("Azure Face API subscription key not configured. Please set ExternalApis:AzureFaceApi:SubscriptionKey in appsettings.json");

        // Configure HttpClient for Azure Face API
        _httpClient.BaseAddress = new Uri(_endpoint);
        _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _subscriptionKey);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        _logger.LogInformation("AzureFaceMatchingService initialized with endpoint: {Endpoint}", _endpoint);
    }

    /// <summary>
    /// Processes and compares two face images using Azure Face API.
    /// Returns the same interface as the original FaceMatchingService for backward compatibility.
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
            var verifyResult = await VerifyFacesAsync(licenseFaceId, selfieFaceId);

            if (verifyResult == null)
            {
                return (null, null, false, 0, "❌ Photo Verification Failed<br>Unable to verify faces.");
            }

            // Convert confidence (0.0-1.0) to match score (0-5)
            var matchScore = ConvertConfidenceToMatchScore(verifyResult.Confidence);
            var threshold = _configuration.GetValue<int>("KycVerification:FaceMatchThreshold", 4);
            var match = verifyResult.IsIdentical && matchScore >= threshold;

            var resultMessage = match
                ? $"✅ Photo Verification Passed<br>Match Score: {matchScore}/5 (Confidence: {verifyResult.Confidence:P0})"
                : $"❌ Photo Verification Failed<br>Match Score: {matchScore}/5 (Confidence: {verifyResult.Confidence:P0}, Required: {threshold}/5)";

            // Azure Face API doesn't return cropped face images, so return null for backward compatibility
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

            // Prepare request
            using var content = new StreamContent(image.OpenReadStream());
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(image.ContentType ?? "image/jpeg");

            // Call Azure Face API detect endpoint
            var detectUrl = $"{_endpoint.TrimEnd('/')}/detect?returnFaceId=true&returnFaceLandmarks=false";
            var response = await _httpClient.PostAsync(detectUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Azure Face API detect failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var detectResults = JsonSerializer.Deserialize<List<FaceDetectionResult>>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (detectResults == null || detectResults.Count == 0)
            {
                _logger.LogWarning("No face detected in {ImageType} image", imageType);
                return null;
            }

            // Use the first detected face (largest if multiple)
            var faceId = detectResults[0].FaceId;
            _logger.LogInformation("Face detected in {ImageType} image. Face ID: {FaceId}", imageType, faceId);
            return faceId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting face in {ImageType} image", imageType);
            return null;
        }
    }

    /// <summary>
    /// Verifies if two face IDs belong to the same person.
    /// </summary>
    private async Task<FaceVerificationResult?> VerifyFacesAsync(string faceId1, string faceId2)
    {
        try
        {
            _logger.LogDebug("Verifying faces");

            var requestBody = new
            {
                faceId1 = faceId1,
                faceId2 = faceId2
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var verifyUrl = $"{_endpoint.TrimEnd('/')}/verify";
            var response = await _httpClient.PostAsync(verifyUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Azure Face API verify failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var verifyResult = JsonSerializer.Deserialize<FaceVerificationResult>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (verifyResult != null)
            {
                _logger.LogInformation("Face verification result: IsIdentical={IsIdentical}, Confidence={Confidence}", 
                    verifyResult.IsIdentical, verifyResult.Confidence);
            }

            return verifyResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying faces");
            return null;
        }
    }

    /// <summary>
    /// Converts Azure Face API confidence (0.0-1.0) to match score (0-5).
    /// </summary>
    private int ConvertConfidenceToMatchScore(double confidence)
    {
        // Map confidence to 0-5 scale
        // 0.0-0.5 -> 0
        // 0.5-0.6 -> 1
        // 0.6-0.7 -> 2
        // 0.7-0.8 -> 3
        // 0.8-0.9 -> 4
        // 0.9-1.0 -> 5
        if (confidence < 0.5) return 0;
        if (confidence < 0.6) return 1;
        if (confidence < 0.7) return 2;
        if (confidence < 0.8) return 3;
        if (confidence < 0.9) return 4;
        return 5;
    }

    private class FaceDetectionResult
    {
        public string? FaceId { get; set; }
    }

    private class FaceVerificationResult
    {
        public bool IsIdentical { get; set; }
        public double Confidence { get; set; }
    }
}

