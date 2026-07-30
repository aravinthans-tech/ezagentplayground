using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QRCodeAPI.Models;
using QRCodeAPI.Services;

namespace QRCodeAPI.Controllers;

/// <summary>
/// Access2Pay playground APIs.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class Access2PayController : ControllerBase
{
    private readonly Access2PayService _access2PayService;
    private readonly InvoiceOcrPipelineService _pipelineService;

    public Access2PayController(Access2PayService access2PayService, InvoiceOcrPipelineService pipelineService)
    {
        _access2PayService = access2PayService;
        _pipelineService = pipelineService;
    }

    /// <summary>
    /// InitiateProcess — process an invoice file and return Access2Pay payload.
    /// </summary>
    /// <remarks>
    /// **Input**
    /// | Name | In | Required | Description |
    /// |------|----|----------|-------------|
    /// | `X-API-Key` | header | yes | Playground API key |
    /// | `file` | form-data | yes | Invoice PDF/image |
    /// | `storageCallbackUrl` | form-data | no | If set, POST payload JSON to this URL before returning |
    ///
    /// **Output (`output`)**
    /// ```json
    /// {
    ///   "referenceNo": "REQ-71",
    ///   "submission": { },
    ///   "sourceDocument": { },
    ///   "Vendor": { },
    ///   "Invoice": { }
    /// }
    /// ```
    /// </remarks>
    /// <param name="request">Multipart form with invoice <c>file</c> and optional <c>storageCallbackUrl</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Success — payload JSON in <c>output</c>.</response>
    /// <response code="400">Validation or processing failure.</response>
    [HttpPost("InitiateProcess")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(52_428_800)]
    [ProducesResponseType(typeof(ResultForHttpsCode), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResultForHttpsCode), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ResultForHttpsCode>> InitiateProcess(
        [FromForm] Access2PayProcessInitiateFormRequest request,
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
    /// GetProcessTickets — list / filter Access2Pay tickets.
    /// </summary>
    /// <remarks>
    /// **Input**
    /// | Name | In | Required | Description |
    /// |------|----|----------|-------------|
    /// | `X-API-Key` | header | yes | Playground API key |
    /// | body | JSON | yes | Browse/filter query |
    ///
    /// **Sample body**
    /// ```json
    /// {
    ///   "sortBy": { "criteria": "id", "order": "DESC" },
    ///   "filterBy": [],
    ///   "currentPage": 1,
    ///   "itemsPerPage": 0,
    ///   "mode": "browse"
    /// }
    /// ```
    /// </remarks>
    /// <param name="body">Query JSON (sortBy, filterBy, paging, mode).</param>
    /// <response code="200">Success — tickets in <c>output</c>.</response>
    /// <response code="400">Missing body or error.</response>
    [HttpPost("GetProcessTickets")]
    [ProducesResponseType(typeof(ResultForHttpsCode), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResultForHttpsCode), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ResultForHttpsCode>> GetProcessTickets([FromBody] JsonElement body)
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
    /// RouteProcessTicket — update an Access2Pay ticket by id.
    /// </summary>
    /// <remarks>
    /// **Input**
    /// | Name | In | Required | Description |
    /// |------|----|----------|-------------|
    /// | `X-API-Key` | header | yes | Playground API key |
    /// | `id` | path | yes | Ticket id |
    /// | body | JSON string | yes | Fields to update |
    ///
    /// **Sample body**
    /// ```json
    /// {
    ///   "referenceNo": "EZ-00012",
    ///   "transactionStatus": "processed"
    /// }
    /// ```
    /// </remarks>
    /// <param name="id">Ticket id in the path.</param>
    /// <param name="body">Update JSON as a string.</param>
    /// <response code="200">Success — result in <c>output</c>.</response>
    /// <response code="400">Missing body or error.</response>
    [HttpPut("RouteProcessTicket/{id}")]
    [ProducesResponseType(typeof(ResultForHttpsCode), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResultForHttpsCode), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ResultForHttpsCode>> RouteProcessTicket(string id, [FromBody] string? body)
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

/// <summary>
/// Multipart form for InitiateProcess.
/// </summary>
public class Access2PayProcessInitiateFormRequest
{
    /// <summary>
    /// Invoice file (PDF or image). Form field name: <c>file</c>.
    /// </summary>
    public IFormFile? File { get; set; }

    /// <summary>
    /// Optional. If provided, the request payload JSON is POSTed to this URL before the API returns.
    /// </summary>
    public string? StorageCallbackUrl { get; set; }
}
