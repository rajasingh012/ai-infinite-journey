using AiInfiniteJourney.Shared;

namespace AiInfiniteJourney.Api.Services;

/// <summary>
/// Plans a small batch of road chunks ahead of the player. Implemented by
/// <see cref="ChunkWorldDirector"/> (LLM-backed) and replaceable for tests.
/// </summary>
public interface IChunkWorldDirector
{
    Task<RoadChunkPlan> DirectAsync(
        float prevEndX, float prevEndZ, string theme, float distanceAhead,
        CancellationToken ct = default);
}