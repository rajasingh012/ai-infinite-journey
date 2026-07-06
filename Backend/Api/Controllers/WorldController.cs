using AiInfiniteJourney.Api.Services;
using AiInfiniteJourney.Shared;
using Microsoft.AspNetCore.Mvc;

namespace AiInfiniteJourney.Api.Controllers;

[ApiController]
[Route("api/world")]
public sealed class WorldController : ControllerBase
{
    private readonly IWorldDirector _director;
    private readonly ILogger<WorldController> _log;

    public WorldController(IWorldDirector director, ILogger<WorldController> log)
    {
        _director = director;
        _log = log;
    }

    /// <summary>
    /// Send a journey prompt, receive a structured JourneyPlan.
    /// </summary>
    /// <example>
    /// POST /api/world/direct
    /// { "prompt": "a peaceful mountain drive" }
    /// </example>
    [HttpPost("direct")]
    [ProducesResponseType(typeof(JourneyPlan), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<JourneyPlan>> Direct(
        [FromBody] DirectRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Prompt))
            return BadRequest(new { error = "prompt is required" });

        try
        {
            var plan = await _director.DirectAsync(req.Prompt, ct);
            return Ok(plan);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "World director failed for prompt: {Prompt}", req.Prompt);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "world director failed", detail = ex.Message });
        }
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", ts = DateTimeOffset.UtcNow });
}
