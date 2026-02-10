using Microsoft.AspNetCore.Http;

namespace QRCodeAPI.Services;

/// <summary>
/// Interface for face matching services.
/// Allows both OpenCV and stub implementations to be used interchangeably.
/// </summary>
public interface IFaceMatchingService
{
    Task<(byte[]? licenseFace, byte[]? selfieFace, bool match, int matchScore, string message)> ProcessAndCompare(
        IFormFile licenseImage, 
        IFormFile selfieImage);
}

