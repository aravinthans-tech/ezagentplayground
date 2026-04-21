using Microsoft.AspNetCore.Mvc;
using QRCodeAPI.Models;
using QRCodeAPI.Services;

namespace QRCodeAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FormDetailsController : ControllerBase
{
    private readonly FormDetailsService _formDetailsService;

    public FormDetailsController(FormDetailsService formDetailsService)
    {
        _formDetailsService = formDetailsService;
    }

    /// <summary>
    /// Proxies Ezofis POST /api/form/all with X-API-Key (middleware) and Ezofis JWT.
    /// Optional AES on request/response via ez tapi encryptAES.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ResultForHttpsCode>> PostFormDetails([FromBody] FormDetailsApiRequest? body)
    {
        if (body == null)
        {
            return BadRequest(new ResultForHttpsCode
            {
                id = 0,
                EncryptOutput = "JSON body is required"
            });
        }

        var ezofisToken = ResolveEzofisBearerToken();
        if (string.IsNullOrWhiteSpace(ezofisToken))
        {
            return BadRequest(new ResultForHttpsCode
            {
                id = 0,
                EncryptOutput = "Provide Ezofis JWT via Authorization: Bearer <jwt> or Ezofis-Token: <jwt> header"
            });
        }

        var result = await _formDetailsService.GetFormDetailsAsync(ezofisToken, body);

        if (result.id == 0)
            return BadRequest(result);

        return Ok(result);
    }

    private string? ResolveEzofisBearerToken()
    {
        if (Request.Headers.TryGetValue("Ezofis-Token", out var ezTok))
        {
            var v = ezTok.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        if (Request.Headers.TryGetValue("Authorization", out var auth))
        {
            var v = auth.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(v))
                return null;

            v = v.Trim();
            if (v.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return v["Bearer ".Length..].Trim();

            return v;
        }

        return null;
    }
}
