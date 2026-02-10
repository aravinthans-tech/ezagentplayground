using Microsoft.AspNetCore.Http;

namespace QRCodeAPI.Services;

/// <summary>
/// Wrapper that makes any IFaceMatchingService work as FaceMatchingService.
/// This allows existing code expecting FaceMatchingService to work with any implementation.
/// </summary>
public class FaceMatchingServiceWrapper : FaceMatchingService
{
    private readonly IFaceMatchingService _faceMatchingService;

    public FaceMatchingServiceWrapper(IFaceMatchingService faceMatchingService, ILogger<FaceMatchingService> logger, IConfiguration configuration)
        : base(logger, configuration, skipInitialization: true)
    {
        _faceMatchingService = faceMatchingService;
    }

    public override async Task<(byte[]? licenseFace, byte[]? selfieFace, bool match, int matchScore, string message)> ProcessAndCompare(
        IFormFile licenseImage, 
        IFormFile selfieImage)
    {
        return await _faceMatchingService.ProcessAndCompare(licenseImage, selfieImage);
    }
}

