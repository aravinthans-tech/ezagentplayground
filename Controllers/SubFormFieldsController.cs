using Microsoft.AspNetCore.Mvc;
using QRCodeAPI.Models;
using QRCodeAPI.Services;

namespace QRCodeAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SubFormFieldsController : ControllerBase
{
    private readonly FormDetailsService _formDetailsService;

    public SubFormFieldsController(FormDetailsService formDetailsService)
    {
        _formDetailsService = formDetailsService;
    }

    [HttpPost]
    public async Task<ActionResult<ResultForHttpsCode>> PostSubFormFields([FromBody] SubFormFieldsApiRequest? body)
    {
        if (body == null || body.FormId <= 0)
        {
            return BadRequest(new ResultForHttpsCode
            {
                id = 0,
                EncryptOutput = "formId is required and must be greater than 0"
            });
        }

        if (body.Subforms == null || body.Subforms.Count == 0)
        {
            return BadRequest(new ResultForHttpsCode
            {
                id = 0,
                EncryptOutput = "subforms is required"
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

        var result = await _formDetailsService.GetSubFormFieldsAsync(ezofisToken, body.FormId, body.Subforms);
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
