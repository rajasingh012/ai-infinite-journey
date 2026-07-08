using System;
using System.Linq;
using AiInfiniteJourney.Shared;

namespace AiInfiniteJourney.Api.Services;

/// <summary>
/// Deterministic road chunk generator. Mirrors <see cref="SyntheticFallback"/>:
/// when the live LLM is unavailable, produce a reasonable RoadChunkPlan so the
/// game stays playable.
///
/// Geometry: chunks go straight forward (negative Z in world), each 50-80m
/// long. To create the "curvy" feel, alternate lateral offset (max ±15m) and
/// rotate the heading slightly between chunks. Dead-ends fire every ~4th
/// chunk (deterministic from theme + chunk counter).
/// </summary>
public static class SyntheticChunkFallback
{
    /// <summary>
    /// Generate 2 chunks ahead of <paramref name="prevEnd"/> matching the
    /// given <paramref name="theme"/>. Lateral drift is deterministic per
    /// (theme, chunk_id) so the same theme always produces the same road
    /// shape — the player gets a consistent route for a given prompt.
    /// </summary>
    public static RoadChunkPlan Build(
        float prevEndX, float prevEndZ, string theme, float distanceAhead)
    {
        var rng = new Rng(Hash(theme ?? "") ^ Hash($"{prevEndX:F1},{prevEndZ:F1}"));

        // Heading in degrees (0 = facing -Z). Start from prev chunk's heading.
        // We approximate by walking lateral offset over distance.
        var headingDeg = rng.Range(-25f, 25f);
        var posX = prevEndX;
        var posZ = prevEndZ;

        var chunks = new List<RoadChunk>();
        var chunkIdBase = (int)(prevEndZ / 50f);
        var coverageRemaining = distanceAhead;

        while (coverageRemaining > 0)
        {
            // Length 50-80m, walk along current heading.
            var length = rng.Range(50f, 80f);
            if (length > coverageRemaining) length = coverageRemaining;

            // Lateral drift: turn heading 5-25° toward target corridor (±15m).
            var turn = rng.Range(-15f, 15f);
            headingDeg += turn;
            headingDeg = Math.Clamp(headingDeg, -45f, 45f);

            var rad = headingDeg * MathF.PI / 180f;
            // Forward is -Z; lateral is +X for positive heading.
            var dx = MathF.Sin(rad) * length;
            var dz = -MathF.Cos(rad) * length;

            var startX = posX;
            var startZ = posZ;
            var endX = posX + dx;
            var endZ = posZ + dz;

            // Dead-end every 4th chunk (deterministic by chunk index).
            var deadEnd = (chunkIdBase + chunks.Count) % 4 == 3;

            chunks.Add(new RoadChunk
            {
                ChunkId = chunkIdBase + chunks.Count,
                Start = new[] { startX, 0f, startZ },
                End = new[] { endX, 0f, endZ },
                Width = 8f,
                DeadEnd = deadEnd,
                SideLeft = PickSide(rng),
                SideRight = PickSide(rng),
                TerrainBias = "",
            });

            posX = endX;
            posZ = endZ;
            coverageRemaining -= length;

            // Safety: stop after 6 chunks even if coverage not exhausted.
            if (chunks.Count >= 6) break;
        }

        // Force 2 chunks minimum so the road always progresses visibly.
        while (chunks.Count < 2)
        {
            chunks.Add(new RoadChunk
            {
                ChunkId = chunkIdBase + chunks.Count,
                Start = new[] { posX, 0f, posZ },
                End = new[] { posX, 0f, posZ - 60f },
                Width = 8f,
                DeadEnd = false,
                SideLeft = PickSide(rng),
                SideRight = PickSide(rng),
                TerrainBias = "",
            });
            posZ -= 60f;
        }

        // If none of the chunks were a dead-end (short plans with chunkIdBase
        // not aligned to the %4 cycle), force the last one. ~30% of plans,
        // decided by the same RNG so it stays deterministic per (theme, prev).
        if (!chunks.Any(c => c.DeadEnd) && rng.Next() < 0.30f)
        {
            var last = chunks.Count - 1;
            var replaced = chunks[last];
            chunks[last] = new RoadChunk
            {
                ChunkId = replaced.ChunkId,
                Start = replaced.Start,
                End = replaced.End,
                Width = replaced.Width,
                DeadEnd = true,
                SideLeft = replaced.SideLeft,
                SideRight = replaced.SideRight,
                TerrainBias = replaced.TerrainBias,
            };
        }

        return new RoadChunkPlan { Chunks = chunks };
    }

    private static string PickSide(Rng rng) => rng.Next() switch
    {
        < 0.30f => "buildings",
        < 0.55f => "sparse_buildings",
        < 0.75f => "trees",
        < 0.85f => "rocks",
        _ => "none",
    };

    private static uint Hash(string s) => unchecked((uint)(s?.GetHashCode() ?? 0));

    /// <summary>Deterministic 0..1 RNG. Same seed → same sequence.</summary>
    private sealed class Rng
    {
        private uint _state;
        public Rng(uint seed) => _state = seed == 0 ? 0xdeadbeefu : seed;

        public float Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state & 0x00FFFFFF) / (float)0x01000000;
        }

        public float Range(float lo, float hi) => lo + (hi - lo) * Next();
    }
}