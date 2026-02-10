using Microsoft.AspNetCore.Http;

namespace QRCodeAPI.Services;

/// <summary>
/// Face matching service wrapper that uses Azure Face API.
/// Maintains backward compatibility with existing code.
/// </summary>
public class FaceMatchingService
{
    private readonly AzureFaceMatchingService _azureFaceMatchingService;
    private readonly ILogger<FaceMatchingService> _logger;

    public FaceMatchingService(
        ILogger<FaceMatchingService> logger,
        IConfiguration configuration,
        AzureFaceMatchingService azureFaceMatchingService)
    {
        _logger = logger;
        _azureFaceMatchingService = azureFaceMatchingService;
        _logger.LogInformation("FaceMatchingService initialized using Azure Face API");
    }

    /// <summary>
    /// Processes and compares two face images.
    /// Delegates to Azure Face API for face matching.
    /// </summary>
    public async Task<(byte[]? licenseFace, byte[]? selfieFace, bool match, int matchScore, string message)> ProcessAndCompare(
        IFormFile licenseImage,
        IFormFile selfieImage)
    {
        return await _azureFaceMatchingService.ProcessAndCompare(licenseImage, selfieImage);
    }
}
