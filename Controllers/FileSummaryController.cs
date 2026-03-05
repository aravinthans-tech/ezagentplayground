using Microsoft.AspNetCore.Mvc;
using QRCodeAPI.Models;
using QRCodeAPI.Services;

namespace QRCodeAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileSummaryController : ControllerBase
{
    private readonly FileSummaryService _fileSummaryService;
    private readonly ILogger<FileSummaryController> _logger;

    public FileSummaryController(FileSummaryService fileSummaryService, ILogger<FileSummaryController> logger)
    {
        _fileSummaryService = fileSummaryService;
        _logger = logger;
    }

    [HttpPost("getSummary")]
    public async Task<ActionResult<ResultForHttpsCode>> GetFileSummary(
        [FromForm] IFormFile file,
        [FromForm] string token)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ResultForHttpsCode
            {
                id = 0,
                EncryptOutput = "File is required"
            });
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new ResultForHttpsCode
            {
                id = 0,
                EncryptOutput = "Token is required"
            });
        }

        try
        {
            // Step 1: Upload file and get itemId
            string itemId;
            using (var fileStream = file.OpenReadStream())
            {
                itemId = await _fileSummaryService.UploadFileAndGetItemId(fileStream, file.FileName, token);
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                return BadRequest(new ResultForHttpsCode
                {
                    id = 0,
                    EncryptOutput = "Failed to get itemId from file upload"
                });
            }

            // Step 2: Call File Summary with repositoryId=1059 and itemId
            var result = await _fileSummaryService.GetFileSummary(token, "1059", itemId);
            
            if (result.id == 0)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file summary");
            return StatusCode(500, new ResultForHttpsCode
            {
                id = 0,
                EncryptOutput = "Internal server error: " + ex.Message
            });
        }
    }
}

