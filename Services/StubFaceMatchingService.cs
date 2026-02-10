using Microsoft.AspNetCore.Http;

namespace QRCodeAPI.Services;

/// <summary>
/// Stub implementation of face matching that doesn't use OpenCV.
/// This allows the app to run without OpenCV dependencies for testing purposes.
/// </summary>
public class StubFaceMatchingService : IFaceMatchingService, IDisposable
{
    private readonly ILogger<StubFaceMatchingService> _logger;
    private readonly IConfiguration _configuration;
    private bool _disposed = false;

    public StubFaceMatchingService(ILogger<StubFaceMatchingService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _logger.LogInformation("StubFaceMatchingService initialized - OpenCV is disabled. Face matching will return unavailable messages.");
    }

    /// <summary>
    /// Stub implementation that returns unavailable message without using OpenCV.
    /// </summary>
    public async Task<(byte[]? licenseFace, byte[]? selfieFace, bool match, int matchScore, string message)> ProcessAndCompare(
        IFormFile licenseImage, 
        IFormFile selfieImage)
    {
        _logger.LogWarning("Face matching requested but OpenCV is disabled. Using stub implementation.");
        
        // Return a clear message that face matching is unavailable
        return (null, null, false, 0, "❌ Face matching is currently unavailable. OpenCV has been disabled for testing purposes.");
    }

    // Expose logger and configuration for wrapper class
    public ILogger<StubFaceMatchingService> GetLogger() => _logger;
    public IConfiguration GetConfiguration() => _configuration;

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
                // No resources to dispose in stub implementation
            }
            _disposed = true;
        }
    }
}

