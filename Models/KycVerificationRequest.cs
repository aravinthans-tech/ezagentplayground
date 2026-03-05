using Microsoft.AspNetCore.Http;

namespace QRCodeAPI.Models;

public class KycVerificationRequest
{
    public List<IFormFile> Documents { get; set; } = new List<IFormFile>();
    public string ExpectedAddress { get; set; } = string.Empty;
    public string ModelChoice { get; set; } = "Mistral"; // "Mistral" or "OpenAI"
    public double ConsistencyThreshold { get; set; } = 0.82;
    public IFormFile? LicenseImage { get; set; }
    public IFormFile? SelfieImage { get; set; }
}

