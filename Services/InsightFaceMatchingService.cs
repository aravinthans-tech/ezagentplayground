using Microsoft.AspNetCore.Http;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace QRCodeAPI.Services;

/// <summary>
/// Face matching service using InsightFace Python service.
/// This implementation calls a local Python FastAPI service that uses InsightFace
/// for face detection and matching, eliminating the need for native OpenCV libraries.
/// </summary>
public class InsightFaceMatchingService : IFaceMatchingService, IDisposable
{
    private readonly ILogger<InsightFaceMatchingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly string _serviceUrl;
    private bool _disposed = false;

    public InsightFaceMatchingService(
        ILogger<InsightFaceMatchingService> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient("InsightFaceAPI");
        
        // Get InsightFace service URL (defaults to localhost:5001)
        _serviceUrl = _configuration["ExternalApis:InsightFace:ServiceUrl"] 
            ?? "http://localhost:5001";

        // Configure HttpClient for InsightFace service
        _httpClient.BaseAddress = new Uri(_serviceUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(60); // Longer timeout for face processing

        _logger.LogInformation("InsightFaceMatchingService initialized with service URL: {ServiceUrl}", _serviceUrl);
    }

    /// <summary>
    /// Compares faces from license and selfie images using InsightFace Python service.
    /// </summary>
    public async Task<(byte[]? licenseFace, byte[]? selfieFace, bool match, int matchScore, string message)> ProcessAndCompare(
        IFormFile licenseImage,
        IFormFile selfieImage)
    {
        try
        {
            _logger.LogInformation("Starting face matching with InsightFace service");

            // Step 1: Detect faces in both images
            var licenseFaceResult = await DetectFaceAsync(licenseImage, "license");
            var selfieFaceResult = await DetectFaceAsync(selfieImage, "selfie");

            if (licenseFaceResult == null || string.IsNullOrEmpty(licenseFaceResult.FaceId))
            {
                return (null, null, false, 0, "❌ Photo Verification Failed<br>No face detected in license image.");
            }

            if (selfieFaceResult == null || string.IsNullOrEmpty(selfieFaceResult.FaceId))
            {
                return (null, null, false, 0, "❌ Photo Verification Failed<br>No face detected in selfie image.");
            }

            // Step 2: Verify if faces match
            var verifyResult = await VerifyFacesAsync(licenseFaceResult.FaceId, selfieFaceResult.FaceId);

            if (verifyResult == null)
            {
                return (null, null, false, 0, "❌ Photo Verification Failed<br>Unable to verify faces.");
            }

            var matchScore = verifyResult.MatchScore;
            var threshold = _configuration.GetValue<int>("KycVerification:FaceMatchThreshold", 4);
            var match = verifyResult.IsIdentical && matchScore >= threshold;

            var resultMessage = match
                ? $"✅ Photo Verification Passed<br>Match Score: {matchScore}/5 (Confidence: {verifyResult.Confidence:P0})"
                : $"❌ Photo Verification Failed<br>Match Score: {matchScore}/5 (Confidence: {verifyResult.Confidence:P0}, Required: {threshold}/5)";

            // InsightFace service doesn't return cropped face images, so we return null for images
            // The UI can still display the original images if needed
            return (null, null, match, matchScore, resultMessage);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling InsightFace service");
            return (null, null, false, 0, $"❌ Face matching error: Unable to connect to InsightFace service. Please ensure the Python service is running. {ex.Message}");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Timeout calling InsightFace service");
            return (null, null, false, 0, "❌ Face matching error: Request to InsightFace service timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in InsightFace face matching");
            return (null, null, false, 0, $"❌ Face matching error: {ex.Message}");
        }
    }

    /// <summary>
    /// Detects a face in the given image and returns the face ID (embedding).
    /// </summary>
    private async Task<FaceDetectionResult?> DetectFaceAsync(IFormFile image, string imageType)
    {
        try
        {
            _logger.LogDebug("Detecting face in {ImageType} image", imageType);

            // Prepare multipart form data
            using var content = new MultipartFormDataContent();
            using var imageStream = image.OpenReadStream();
            using var streamContent = new StreamContent(imageStream);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(image.ContentType ?? "image/jpeg");
            content.Add(streamContent, "file", image.FileName ?? $"{imageType}.jpg");

            // Call InsightFace service detect endpoint
            // Use full URL to avoid BaseAddress issues
            var detectUrl = _serviceUrl.TrimEnd('/') + "/detect";
            var response = await _httpClient.PostAsync(detectUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("InsightFace service detect failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var detectResult = JsonSerializer.Deserialize<FaceDetectionResult>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (detectResult == null || string.IsNullOrEmpty(detectResult.FaceId))
            {
                _logger.LogWarning("No face detected in {ImageType} image", imageType);
                return null;
            }

            _logger.LogInformation("Face detected in {ImageType} image. Confidence: {Confidence}", imageType, detectResult.Confidence);
            return detectResult;
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
    private async Task<FaceVerificationResult?> VerifyFacesAsync(string faceId1, string faceId2)
    {
        try
        {
            _logger.LogDebug("Verifying faces");

            // Prepare request body
            var requestBody = new
            {
                faceId1 = faceId1,
                faceId2 = faceId2
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Call InsightFace service verify endpoint
            // Use full URL to avoid BaseAddress issues
            var verifyUrl = _serviceUrl.TrimEnd('/') + "/verify";
            var response = await _httpClient.PostAsync(verifyUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("InsightFace service verify failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var verifyResult = JsonSerializer.Deserialize<FaceVerificationResult>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (verifyResult != null)
            {
                _logger.LogInformation("Face verification result: IsIdentical={IsIdentical}, Confidence={Confidence}, MatchScore={MatchScore}", 
                    verifyResult.IsIdentical, verifyResult.Confidence, verifyResult.MatchScore);
            }

            return verifyResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying faces");
            return null;
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

    // Helper classes for JSON deserialization
    private class FaceDetectionResult
    {
        public string? FaceId { get; set; }
        public double Confidence { get; set; }
        public BoundingBox? Bbox { get; set; }
        public string? Message { get; set; }
    }

    private class BoundingBox
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    private class FaceVerificationResult
    {
        public bool IsIdentical { get; set; }
        public double Confidence { get; set; }
        public int MatchScore { get; set; }
        public double Threshold { get; set; }
    }
}

