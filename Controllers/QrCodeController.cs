using Microsoft.AspNetCore.Mvc;
using QRCodeAPI.Models;
using QRCodeAPI.Services;

namespace QRCodeAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QrCodeController : ControllerBase
{
    private readonly QrCodeService _qrCodeService;
    private readonly ILogger<QrCodeController> _logger;

    public QrCodeController(QrCodeService qrCodeService, ILogger<QrCodeController> logger)
    {
        _qrCodeService = qrCodeService;
        _logger = logger;
    }

    // [HttpPost("generate")]
    // public ActionResult<ResultForHttpsCode> GenerateQrCode([FromBody] QrCodeRequest request)
    // {
    //     if (string.IsNullOrWhiteSpace(request?.qrvalue))
    //     {
    //         return BadRequest(new ResultForHttpsCode
    //         {
    //             id = 0,
    //             EncryptOutput = "QR value is required"
    //         });
    //     }

    //     try
    //     {
    //         var result = _qrCodeService.GenerateQrCode(request.qrvalue);
    //         return Ok(result);
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.LogError(ex, "Error generating QR code");
    //         return StatusCode(500, new ResultForHttpsCode
    //         {
    //             id = 0,
    //             EncryptOutput = "Internal server error: " + ex.Message
    //         });
    //     }
    // }
}

public class QrCodeRequest
{
    public string qrvalue { get; set; } = string.Empty;
}

