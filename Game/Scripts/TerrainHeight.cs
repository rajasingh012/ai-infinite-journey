using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Global deterministic terrain height and road centerline functions.
/// Both RoadChunk (terrain) and SplineRoad use these, guaranteeing the
/// road sits exactly on the terrain surface with no seams between chunks.
/// </summary>
public static class TerrainHeight
{
    /// <summary>
    /// Terrain height at any world (X, Z) position. Layered sine waves
    /// in world space → smooth, seamless hills across all chunks.
    /// Amplitude ~4m, gentle enough for the arcade car physics.
    /// </summary>
    public static float Sample(float worldX, float worldZ)
    {
        // Large rolling hills (~100m wavelength, ±2.5m)
        var h = 2.5f * Mathf.Sin(0.012f * worldX + 0.010f * worldZ + 0.5f);
        // Medium bumps (~50m wavelength, ±1.2m)
        h += 1.2f * Mathf.Sin(0.035f * worldX - 0.028f * worldZ + 1.2f);
        // Fine detail (~20m wavelength, ±0.4m)
        h += 0.4f * Mathf.Sin(0.08f * worldX + 0.065f * worldZ);
        return h;
    }

    /// <summary>
    /// Road centerline X offset as a function of world Z.
    /// Two layered sines → gentle winding curves (~8m max offset).
    /// Deterministic: same Z always produces same X.
    /// </summary>
    public static float RoadCenterX(float worldZ)
    {
        return 6.0f * Mathf.Sin(0.008f * worldZ)
             + 2.5f * Mathf.Sin(0.019f * worldZ + 1.3f);
    }

    /// <summary>
    /// Smoothed terrain height for the road surface at a given world Z.
    /// Averaging terrain height over a ±10m window creates natural bridges
    /// (where terrain dips below the smoothed road) and cuttings/tunnels
    /// (where terrain rises above). The +0.3 offset keeps the road surface
    /// slightly above the terrain so it's always visible.
    /// </summary>
    public static float RoadHeightAt(float worldZ)
    {
        const float window = 10f;
        const float step = 2f;
        var sum = 0f;
        var count = 0;
        for (var dz = -window; dz <= window; dz += step)
        {
            var z = worldZ + dz;
            var x = RoadCenterX(z);
            sum += Sample(x, z);
            count++;
        }
        return sum / count + 0.3f;
    }
}
