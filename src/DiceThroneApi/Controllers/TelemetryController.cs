using DiceThroneApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace DiceThroneApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TelemetryController : ControllerBase
{
    private readonly TelemetryService _telemetryService;

    public TelemetryController(TelemetryService telemetryService)
    {
        _telemetryService = telemetryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetSummary()
    {
        var summary = await _telemetryService.GetSummaryAsync();
        return Ok(summary);
    }

    [HttpPost("visit")]
    public async Task<IActionResult> RecordVisit([FromBody] RecordVisitRequest? request)
    {
        var visitorId = request?.VisitorId;
        if (string.IsNullOrWhiteSpace(visitorId) && Request.Headers.TryGetValue("X-Visitor-Id", out var headerVisitorId))
        {
            visitorId = headerVisitorId.ToString();
        }

        await _telemetryService.RecordVisitAsync(visitorId, request?.Page);
        return Accepted();
    }
}

public class RecordVisitRequest
{
    public string? VisitorId { get; set; }
    public string? Page { get; set; }
}
