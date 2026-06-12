using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QRCodeAPI.Models;
using QRCodeAPI.Services;

namespace QRCodeAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class Access2PayController : ControllerBase
{
    private readonly Access2PayService _access2PayService;

    public Access2PayController(Access2PayService access2PayService)
    {
        _access2PayService = access2PayService;
    }

    /// <summary>
    /// Proxies POST https://eztapi.ezofis.com/api/external/access2payConnectorInsert (tenantId 2 token).
    /// </summary>
    [HttpPost("connectorInsert")]
    public async Task<ActionResult<ResultForHttpsCode>> ConnectorInsert([FromBody] JsonElement body)
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
    /// Proxies POST https://eztapi.ezofis.com/api/external/access2payGet
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
    /// Proxies PUT https://eztapi.ezofis.com/api/external/access2payUpdate/{id}
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
