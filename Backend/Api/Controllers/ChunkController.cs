using AiInfiniteJourney.Api.Services;
using AiInfiniteJourney.Shared;
using Microsoft.AspNetCore.Mvc;

namespace AiInfiniteJourney.Api.Controllers;

/// <summary>
/// Road chunk planning endpoint. Called by the game on a timer to refill
/// its ahead-of-car chunk queue. Always returns a usable plan: on LLM
/// failure falls back to <see cref="SyntheticChunkFallback"/>.
/// </summary>
[ApiController]
[Route("api/world")]
public sealed class ChunkController : ControllerBase
{
    private readonly IChunkWorldDirector _director;
    private readonly ILogger<ChunkController> _log;

    public ChunkController(IChunkWorldDirector director, ILogger<ChunkController> log)
    {
        _director = director;
        _log = log;
    }

    /// <example>
    /// POST /api/world/chunk
    /// { "prev_end_x": 0, "prev_end_z": 0, "theme": "Mountain", "distance_ahead": 150 }
    /// </example>
    [HttpPost("chunk")]
    [ProducesResponseType(typeof(RoadChunkPlan), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RoadChunkPlan>> Chunk(
        [FromBody] ChunkRequest req,
        CancellationToken ct)
    {
        if (req is null)
            return BadRequest(new { error = "request body required" });

        if (req.DistanceAhead <= 0 || req.DistanceAhead > 1000)
            return BadRequest(new { error = "distance_ahead must be in (0, 1000]" });

        try
        {
            var plan = await _director.DirectAsync(
                req.PrevEndX, req.PrevEndZ,
                req.Theme ?? "", req.DistanceAhead, ct);
            return Ok(plan);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Chunk director failed for theme={Theme}, prev=({X},{Z})",
                req.Theme, req.PrevEndX, req.PrevEndZ);

            // Fallback: deterministic generator keeps the road visible.
            _log.LogWarning("Falling back to synthetic chunk plan");
            return Ok(SyntheticChunkFallback.Build(
                req.PrevEndX, req.PrevEndZ,
                req.Theme ?? "", req.DistanceAhead));
        }
    }
}