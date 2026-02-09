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
    public async Task<ActionResult<KycVerificationResult>> VerifyKyc(
        [FromForm] List<IFormFile> documents,
        [FromForm] string expectedAddress,
        [FromForm] string modelChoice = "Mistral",
        [FromForm] double consistencyThreshold = 0.82,
        [FromForm] IFormFile? licenseImage = null,
        [FromForm] IFormFile? selfieImage = null)
    {
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
            var request = new KycVerificationRequest
            {
                Documents = documents,
                ExpectedAddress = expectedAddress,
                ModelChoice = modelChoice,
                ConsistencyThreshold = consistencyThreshold,
                LicenseImage = licenseImage,
                SelfieImage = selfieImage
            };

            var result = await _kycVerificationService.VerifyKyc(request);
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

