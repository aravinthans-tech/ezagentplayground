using Microsoft.AspNetCore.Http;

namespace QRCodeAPI.Services;

/// <summary>
/// Wrapper that makes StubFaceMatchingService work as FaceMatchingService.
/// This allows existing code expecting FaceMatchingService to work with the stub.
/// </summary>
public class FaceMatchingServiceWrapper : FaceMatchingService
{
    private readonly IFaceMatchingService _stubService;

    public FaceMatchingServiceWrapper(StubFaceMatchingService stubService, ILogger<FaceMatchingService> logger, IConfiguration configuration)
        : base(logger, configuration, skipInitialization: true)
    {
        _stubService = stubService;
    }

    public override async Task<(byte[]? licenseFace, byte[]? selfieFace, bool match, int matchScore, string message)> ProcessAndCompare(
        IFormFile licenseImage, 
        IFormFile selfieImage)
    {
        return await _stubService.ProcessAndCompare(licenseImage, selfieImage);
    }
}

