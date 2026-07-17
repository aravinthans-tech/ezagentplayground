using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QRCodeAPI.Models;
using QRCodeAPI.Services;

namespace QRCodeAPI.Controllers;

/// <summary>
/// Invoice OCR Agent playground APIs — same upstream behavior as Access2Pay, renamed routes.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class InvoiceOcrController : ControllerBase
{
    private readonly Access2PayService _access2PayService;

    public InvoiceOcrController(Access2PayService access2PayService)
    {
        _access2PayService = access2PayService;
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
