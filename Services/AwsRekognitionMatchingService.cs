using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon;
using Microsoft.AspNetCore.Http;

namespace QRCodeAPI.Services;

/// <summary>
/// Face matching service using AWS Rekognition.
/// Provides cloud-based face detection and verification without native dependencies.
/// </summary>
public class AwsRekognitionMatchingService
{
    private readonly ILogger<AwsRekognitionMatchingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly AmazonRekognitionClient _rekognitionClient;
    private readonly string _region;

    public AwsRekognitionMatchingService(
        ILogger<AwsRekognitionMatchingService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        // Get AWS Rekognition configuration
        var accessKey = _configuration["ExternalApis:AwsRekognition:AccessKey"]
            ?? throw new InvalidOperationException("AWS Rekognition Access Key not configured. Please set ExternalApis:AwsRekognition:AccessKey in appsettings.json");

        var secretKey = _configuration["ExternalApis:AwsRekognition:SecretKey"]
            ?? throw new InvalidOperationException("AWS Rekognition Secret Key not configured. Please set ExternalApis:AwsRekognition:SecretKey in appsettings.json");

        _region = _configuration["ExternalApis:AwsRekognition:Region"] ?? "APSouth1";

        // Create AWS Rekognition client
        var regionEndpoint = RegionEndpoint.GetBySystemName(_region);
        _rekognitionClient = new AmazonRekognitionClient(accessKey, secretKey, regionEndpoint);

        _logger.LogInformation("AwsRekognitionMatchingService initialized with region: {Region}", _region);
    }

    /// <summary>
    /// Processes and compares two face images using AWS Rekognition.
    /// Returns the same interface as the original FaceMatchingService for backward compatibility.
    /// </summary>
    public async Task<(byte[]? licenseFace, byte[]? selfieFace, bool match, int matchScore, string message)> ProcessAndCompare(
        IFormFile licenseImage,
        IFormFile selfieImage)
    {
        try
        {
            _logger.LogInformation("Starting face matching with AWS Rekognition");

            // Step 1: Detect faces in both images
            var licenseFaceResult = await DetectFaceAsync(licenseImage, "license");
            var selfieFaceResult = await DetectFaceAsync(selfieImage, "selfie");

            if (licenseFaceResult == null)
            {
                return (null, null, false, 0, "❌ Photo Verification Failed<br>No face detected in license image.");
            }

            if (selfieFaceResult == null)
            {
                return (null, null, false, 0, "❌ Photo Verification Failed<br>No face detected in selfie image.");
            }

            // Step 2: Compare faces using the original images
            var compareResult = await CompareFacesAsync(licenseImage, selfieImage);

            if (compareResult == null)
            {
                return (null, null, false, 0, "❌ Photo Verification Failed<br>Unable to compare faces.");
            }

            // Convert similarity (0.0-1.0) to match score (0-5)
            var matchScore = ConvertSimilarityToMatchScore(compareResult.Similarity);
            var threshold = _configuration.GetValue<int>("KycVerification:FaceMatchThreshold", 4);
            var match = compareResult.Similarity >= 0.8 && matchScore >= threshold; // AWS uses 0.8 as typical match threshold

            var resultMessage = match
                ? $"✅ Photo Verification Passed<br>Match Score: {matchScore}/5 (Similarity: {compareResult.Similarity:P0})"
                : $"❌ Photo Verification Failed<br>Match Score: {matchScore}/5 (Similarity: {compareResult.Similarity:P0}, Required: {threshold}/5)";

            // AWS Rekognition doesn't return cropped face images, so return null for backward compatibility
            return (null, null, match, matchScore, resultMessage);
        }
        catch (AmazonRekognitionException ex)
        {
            _logger.LogError(ex, "AWS Rekognition error: {ErrorCode} - {Message}", ex.ErrorCode, ex.Message);
            return (null, null, false, 0, $"❌ Face matching error: AWS Rekognition error ({ex.ErrorCode}): {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AWS Rekognition face matching");
            return (null, null, false, 0, $"❌ Face matching error: {ex.Message}");
        }
    }

    /// <summary>
    /// Detects a face in the given image and returns face details.
    /// </summary>
    private async Task<FaceDetail?> DetectFaceAsync(IFormFile image, string imageType)
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

            // Create DetectFaces request
            var request = new DetectFacesRequest
            {
                Image = new Amazon.Rekognition.Model.Image
                {
                    Bytes = new MemoryStream(imageBytes)
                },
                Attributes = new List<string> { "ALL" } // Get all face attributes
            };

            // Call AWS Rekognition
            var response = await _rekognitionClient.DetectFacesAsync(request);

            if (response.FaceDetails == null || response.FaceDetails.Count == 0)
            {
                _logger.LogWarning("No face detected in {ImageType} image", imageType);
                return null;
            }

            // Use the face with highest confidence (usually the largest/main face)
            var bestFace = response.FaceDetails
                .OrderByDescending(f => f.Confidence)
                .FirstOrDefault();

            if (bestFace != null)
            {
                _logger.LogInformation("Face detected in {ImageType} image. Confidence: {Confidence}", 
                    imageType, bestFace.Confidence);
            }

            return bestFace;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting face in {ImageType} image", imageType);
            return null;
        }
    }

    /// <summary>
    /// Compares two faces and returns similarity score.
    /// </summary>
    private async Task<CompareFacesMatch?> CompareFacesAsync(IFormFile image1, IFormFile image2)
    {
        try
        {
            _logger.LogDebug("Comparing faces");

            // Read both images
            byte[] image1Bytes;
            using (var stream = image1.OpenReadStream())
            using (var memoryStream = new MemoryStream())
            {
                await stream.CopyToAsync(memoryStream);
                image1Bytes = memoryStream.ToArray();
            }

            byte[] image2Bytes;
            using (var stream = image2.OpenReadStream())
            using (var memoryStream = new MemoryStream())
            {
                await stream.CopyToAsync(memoryStream);
                image2Bytes = memoryStream.ToArray();
            }

            // Create CompareFaces request
            var request = new CompareFacesRequest
            {
                SourceImage = new Amazon.Rekognition.Model.Image
                {
                    Bytes = new MemoryStream(image1Bytes)
                },
                TargetImage = new Amazon.Rekognition.Model.Image
                {
                    Bytes = new MemoryStream(image2Bytes)
                },
                SimilarityThreshold = 0.0f // Get all matches, we'll filter by score
            };

            // Call AWS Rekognition
            var response = await _rekognitionClient.CompareFacesAsync(request);

            if (response.FaceMatches == null || response.FaceMatches.Count == 0)
            {
                _logger.LogWarning("No face matches found");
                return null;
            }

            // Get the best match (highest similarity)
            var bestMatch = response.FaceMatches
                .OrderByDescending(m => m.Similarity)
                .FirstOrDefault();

            if (bestMatch != null)
            {
                _logger.LogInformation("Face comparison result: Similarity={Similarity}, Confidence={Confidence}", 
                    bestMatch.Similarity, bestMatch.Face?.Confidence);
            }

            return bestMatch;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comparing faces");
            return null;
        }
    }

    /// <summary>
    /// Converts AWS Rekognition similarity (0.0-1.0) to match score (0-5).
    /// </summary>
    private int ConvertSimilarityToMatchScore(float similarity)
    {
        // Map similarity to 0-5 scale
        // 0.0-0.5 -> 0
        // 0.5-0.6 -> 1
        // 0.6-0.7 -> 2
        // 0.7-0.8 -> 3
        // 0.8-0.9 -> 4
        // 0.9-1.0 -> 5
        if (similarity < 0.5f) return 0;
        if (similarity < 0.6f) return 1;
        if (similarity < 0.7f) return 2;
        if (similarity < 0.8f) return 3;
        if (similarity < 0.9f) return 4;
        return 5;
    }
}

