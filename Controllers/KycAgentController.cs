using Microsoft.AspNetCore.Mvc;
using QRCodeAPI.Models;
using QRCodeAPI.Services;

namespace QRCodeAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class KycAgentController : ControllerBase
{
    private readonly KycAgentService _kycAgentService;
    private readonly KycVerificationService _kycVerificationService;
    private readonly ILogger<KycAgentController> _logger;

    public KycAgentController(
        KycAgentService kycAgentService, 
        KycVerificationService kycVerificationService,
        ILogger<KycAgentController> logger)
    {
        _kycAgentService = kycAgentService;
        _kycVerificationService = kycVerificationService;
        _logger = logger;
    }

    // [HttpPost("process")]
    // public async Task<ActionResult<ResultForHttpsCode>> ProcessKycAgent(
    //     [FromForm] List<IFormFile> documents,
    //     [FromForm] IFormFile? licenseImage = null,
    //     [FromForm] IFormFile? selfieImage = null)
    // {
    //     if (documents == null || documents.Count == 0)
    //     {
    //         return BadRequest(new ResultForHttpsCode
    //         {
    //             id = 0,
    //             EncryptOutput = "At least one document is required"
    //         });
    //     }

    //     try
    //     {
    //         // Process multiple documents
    //         var result = await _kycAgentService.ProcessMultipleDocuments(documents, licenseImage, selfieImage);
    //         
    //         if (result.id == 0)
    //         {
    //             return BadRequest(result);
    //         }

    //         return Ok(result);
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.LogError(ex, "Error processing KYC Agent");
    //         return StatusCode(500, new ResultForHttpsCode
    //         {
    //             id = 0,
    //             EncryptOutput = "Internal server error: " + ex.Message
    //         });
    //     }
    // }

    [HttpPost("verify")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<KycVerificationResult>> VerifyKyc([FromForm] KycVerifyFormRequest request)
    {
        var documents = request?.Documents;
        var expectedAddress = request?.ExpectedAddress;
        var modelChoice = string.IsNullOrWhiteSpace(request?.ModelChoice) ? "Mistral" : request!.ModelChoice!;
        var consistencyThreshold = request?.ConsistencyThreshold ?? 0.82;

        if (documents == null || documents.Count < 2)
        {
            return BadRequest(new KycVerificationResult
            {
                StatusHtml = "❌ <b style='color:red;'>Please upload at least two documents.</b>"
            });
        }

        if (string.IsNullOrWhiteSpace(expectedAddress))
        {
            return BadRequest(new KycVerificationResult
            {
                StatusHtml = "❌ <b style='color:red;'>Expected address is required.</b>"
            });
        }

        try
        {
            var verifyRequest = new KycVerificationRequest
            {
                Documents = documents,
                ExpectedAddress = expectedAddress,
                ModelChoice = modelChoice,
                ConsistencyThreshold = consistencyThreshold,
                LicenseImage = request?.LicenseImage,
                SelfieImage = request?.SelfieImage
            };

            var result = await _kycVerificationService.VerifyKyc(verifyRequest);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying KYC");
            return StatusCode(500, new KycVerificationResult
            {
                StatusHtml = $"❌ <b style='color:red;'>Internal server error: {ex.Message}</b>"
            });
        }
    }
}

public class KycVerifyFormRequest
{
    public List<IFormFile>? Documents { get; set; }
    public string? ExpectedAddress { get; set; }
    public string? ModelChoice { get; set; } = "Mistral";
    public double ConsistencyThreshold { get; set; } = 0.82;
    public IFormFile? LicenseImage { get; set; }
    public IFormFile? SelfieImage { get; set; }
}

