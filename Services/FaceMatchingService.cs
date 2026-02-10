using Microsoft.AspNetCore.Http;

namespace QRCodeAPI.Services;

/// <summary>
/// Face matching service wrapper that uses AWS Rekognition.
/// Maintains backward compatibility with existing code.
/// </summary>
public class FaceMatchingService
{
    private readonly AwsRekognitionMatchingService _awsRekognitionMatchingService;
    private readonly ILogger<FaceMatchingService> _logger;

    public FaceMatchingService(
        ILogger<FaceMatchingService> logger,
        IConfiguration configuration,
        AwsRekognitionMatchingService awsRekognitionMatchingService)
    {
        _logger = logger;
        _awsRekognitionMatchingService = awsRekognitionMatchingService;
        _logger.LogInformation("FaceMatchingService initialized using AWS Rekognition");
    }

    /// <summary>
    /// Processes and compares two face images.
    /// Delegates to AWS Rekognition for face matching.
    /// </summary>
    public async Task<(byte[]? licenseFace, byte[]? selfieFace, bool match, int matchScore, string message)> ProcessAndCompare(
        IFormFile licenseImage,
        IFormFile selfieImage)
    {
        return await _awsRekognitionMatchingService.ProcessAndCompare(licenseImage, selfieImage);
    }
}
