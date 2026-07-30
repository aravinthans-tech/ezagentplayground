using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QRCodeAPI.Models;
using QRCodeAPI.Services;

namespace QRCodeAPI.Controllers;

/// <summary>
/// Invoice OCR Agent playground APIs — Access2Pay proxy routes plus OCR→upload→transaction pipeline.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class InvoiceOcrController : ControllerBase
{
    private readonly Access2PayService _access2PayService;
    private readonly InvoiceOcrPipelineService _pipelineService;

    public InvoiceOcrController(Access2PayService access2PayService, InvoiceOcrPipelineService pipelineService)
    {
        _access2PayService = access2PayService;
        _pipelineService = pipelineService;
    }

    /// <summary>
    /// One-input pipeline: OCR (uploadforStaticMetadata) → upload (repo 214) → transaction (form 141 / workflow 104).
    /// Uses tenant 2 token from authenticate. Only the invoice file is required.
    /// </summary>
    [HttpPost("process")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(52_428_800)]
    public async Task<ActionResult<ResultForHttpsCode>> Process(
        [FromForm] InvoiceOcrProcessFormRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.File == null || request.File.Length == 0)
        {
            return BadRequest(new ResultForHttpsCode
            {
                id = 0,
                EncryptOutput = "File is required"
            });
        }

        var apiKey = Request.Headers["X-API-Key"].ToString();
        var result = await _pipelineService.ProcessInvoiceAsync(
            request.File,
            apiKey,
            request.StorageCallbackUrl,
            cancellationToken);
        if (result.id == 0)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Same as Access2Pay connectorInsert — proxies Ezofis access2payConnectorInsert.
    /// </summary>
    [HttpPost("insert")]
    public async Task<ActionResult<ResultForHttpsCode>> Insert([FromBody] JsonElement body)
    {
        if (body.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return BadRequest(new ResultForHttpsCode
            {
                id = 0,
                EncryptOutput = "JSON body is required"
            });
        }

        var result = await _access2PayService.ConnectorInsertAsync(body);
        if (result.id == 0)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Same as Access2Pay get — proxies Ezofis access2payGet.
    /// </summary>
    [HttpPost("get")]
    public async Task<ActionResult<ResultForHttpsCode>> Get([FromBody] JsonElement body)
    {
        if (body.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return BadRequest(new ResultForHttpsCode
            {
                id = 0,
                EncryptOutput = "JSON body is required"
            });
        }

        var result = await _access2PayService.GetAsync(body);
        if (result.id == 0)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Same as Access2Pay update — proxies Ezofis access2payUpdate/{id}.
    /// </summary>
    [HttpPut("update/{id}")]
    public async Task<ActionResult<ResultForHttpsCode>> Update(string id, [FromBody] string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return BadRequest(new ResultForHttpsCode
            {
                id = 0,
                EncryptOutput = "Request body string is required"
            });
        }

        var result = await _access2PayService.UpdateAsync(id, body);
        if (result.id == 0)
            return BadRequest(result);

        return Ok(result);
    }
}

public class InvoiceOcrProcessFormRequest
{
    public IFormFile? File { get; set; }
    public string? StorageCallbackUrl { get; set; }
}
