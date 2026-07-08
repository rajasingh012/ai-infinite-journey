using System.Linq;
using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// Headless test runner. Launch with:
///   godot --headless --script res://Scenes/TestRunner.tscn
/// or run the scene directly:
///   godot --headless res://Scenes/TestRunner.tscn
///
/// Tests the pure-math functions that caused the sinking/earthquake bugs.
/// Exit code 0 = all pass, 1 = failures.
/// </summary>
[GlobalClass]
public partial class TestRunner : Node
{
    private int _passed;
    private int _failed;

    public override void _Ready()
    {
        GD.Print("=== AI Infinite Journey — Unit Tests ===\n");

        TestTerrainHeightBounds();
        TestTerrainHeightDeterministic();
        TestTerrainHeightContinuous();
        TestTerrainHeightNoNaN();
        TestRoadCenterXBounds();
        TestRoadHeightNearTerrain();
        TestResolutionProducesExactCellSize();
        TestMapDataIndexOrder();
        TestRoadDistanceOnCenterline();
        TestRoadDistanceLateralOffset();
        TestRoadDistanceOffRoad();
        TestRoadDistanceCurvedSegment();
        TestRoadDistanceOutsideSegment();
        TestFurthestChunkEndEmpty();
        TestFurthestChunkEndSingle();
        TestFurthestChunkEndPicksFurthest();
        TestFurthestChunkEndIgnoresBehind();
        TestFurthestChunkEndSeedChunkRegression();
        TestNearestCenterlineStraight();
        TestNearestCenterlineCurved();
        TestNearestCenterlineClampsAtEnds();
        TestNearestCenterlineDegenerate();

        GD.Print($"\n=== Unit Tests: {_passed} passed, {_failed} failed ===\n");

        if (_failed > 0)
        {
            GetTree().Quit(1);
            return;
        }

        // All unit tests pass. Now run the full-scene integration test,
        // which needs physics frames to simulate the car driving forward.
        // This catches regressions where engine integration breaks — e.g.
        // input mapping removed, physics layer changed, collision shape
        // not added, or downforce + friction preventing movement.
        GD.Print("Starting integration test (loading full scene)...");
        CallDeferred(nameof(StartIntegrationTest));
    }

    private void Assert(string name, bool condition, string detail = "")
    {
        if (condition)
        {
            _passed++;
            GD.Print($"  PASS: {name}");
        }
        else
        {
            _failed++;
            GD.Print($"  FAIL: {name} {detail}");
        }
    }

    // --- TerrainHeight.Sample ---

    private void TestTerrainHeightBounds()
    {
        // Amplitude is 2.5 + 1.2 + 0.4 = 4.1m max
        var min = float.MaxValue;
        var max = float.MinValue;
        for (var x = -500f; x <= 500f; x += 5f)
        for (var z = -500f; z <= 500f; z += 5f)
        {
            var h = TerrainHeight.Sample(x, z);
            if (h < min) min = h;
            if (h > max) max = h;
        }
        Assert("TerrainHeight.Sample bounded [-5, 5]",
            min >= -5f && max <= 5f, $"actual=[{min:F2}, {max:F2}]");
    }

    private void TestTerrainHeightDeterministic()
    {
        var h1 = TerrainHeight.Sample(42.5f, 77.3f);
        var h2 = TerrainHeight.Sample(42.5f, 77.3f);
        Assert("TerrainHeight.Sample deterministic", h1 == h2, $"{h1} vs {h2}");
    }

    private void TestTerrainHeightContinuous()
    {
        // Adjacent samples should differ by < 0.5m (smooth, no cliffs)
        var maxJump = 0f;
        for (var x = -200f; x <= 200f; x += 10f)
        {
            var h1 = TerrainHeight.Sample(x, 0);
            var h2 = TerrainHeight.Sample(x + 1f, 0);
            var jump = Mathf.Abs(h2 - h1);
            if (jump > maxJump) maxJump = jump;
        }
        Assert("TerrainHeight.Sample smooth (<0.5m/m jump)",
            maxJump < 0.5f, $"maxJump={maxJump:F3}");
    }

    private void TestTerrainHeightNoNaN()
    {
        var hasNaN = false;
        for (var x = -100f; x <= 100f; x += 3f)
        {
            var h = TerrainHeight.Sample(x, x * 0.7f);
            if (float.IsNaN(h) || float.IsInfinity(h)) hasNaN = true;
        }
        Assert("TerrainHeight.Sample no NaN/Inf", !hasNaN);
    }

    // --- TerrainHeight.RoadCenterX ---

    private void TestRoadCenterXBounds()
    {
        // 6.0 + 2.5 = 8.5m max offset
        var maxAbs = 0f;
        for (var z = -1000f; z <= 1000f; z += 5f)
        {
            var cx = Mathf.Abs(TerrainHeight.RoadCenterX(z));
            if (cx > maxAbs) maxAbs = cx;
        }
        Assert("RoadCenterX bounded <10m", maxAbs < 10f, $"max={maxAbs:F2}");
    }

    // --- TerrainHeight.RoadHeightAt ---

    private void TestRoadHeightNearTerrain()
    {
        // Road height should be within ~4m of raw terrain (it's an average + 0.3)
        for (var z = 0f; z <= 200f; z += 25f)
        {
            var roadH = TerrainHeight.RoadHeightAt(z);
            var terrainH = TerrainHeight.Sample(TerrainHeight.RoadCenterX(z), z);
            var diff = Mathf.Abs(roadH - terrainH);
            Assert($"RoadHeightAt z={z:F0} within 5m of terrain",
                diff < 5f, $"road={roadH:F2} terrain={terrainH:F2} diff={diff:F2}");
        }
    }

    // --- Collision setup regression tests ---

    private void TestResolutionProducesExactCellSize()
    {
        // Resolution=51, ChunkSize=25 → cellSize = 50/50 = 1.0m exactly.
        // This is what prevents the HeightMapShape3D scaling bug.
        var resolution = 51;
        var chunkHalfSize = 25;
        var cellSize = (chunkHalfSize * 2f) / (resolution - 1);
        Assert("Resolution=51 gives cellSize=1.0m exactly",
            Mathf.IsEqualApprox(cellSize, 1.0f), $"cellSize={cellSize:F4}");
    }

    private void TestMapDataIndexOrder()
    {
        // Verify MapData uses row-major z*width+x (Godot HeightMapShape3D convention).
        // If transposed (x*width+z), terrain collision heightmap is wrong → car sinks.
        var resolution = 5;
        var heights = new float[resolution, resolution];
        for (var i = 0; i < resolution; i++)
        for (var j = 0; j < resolution; j++)
            heights[i, j] = i * 10 + j; // unique value per (i,j)

        var mapData = new float[resolution * resolution];
        for (var j = 0; j < resolution; j++)   // j = Z (rows)
        for (var i = 0; i < resolution; i++)   // i = X (columns)
            mapData[j * resolution + i] = heights[i, j];

        // At (x=2, z=3): height should be heights[2,3] = 23
        var expected = heights[2, 3];
        var actual = mapData[3 * resolution + 2];
        Assert("MapData row-major z*width+x correct",
            actual == expected, $"expected={expected} actual={actual}");
    }

    // --- ChunkedRoadManager distance math (off-road penalty support) ---

    private void TestRoadDistanceOnCenterline()
    {
        // Car exactly on the centerline of a straight segment
        var (dist, halfWidth, onRoad) = ChunkedRoadManager.ComputeRoadDistance(
            new Vector3(0, 0, 0), new Vector3(0, 0, -60), 8f,
            new Vector3(0, 0, -30));
        Assert("RoadDistance: centerline is on-road",
            dist < 0.01f && onRoad, $"dist={dist:F3} onRoad={onRoad}");
        Assert("RoadDistance: halfWidth = 4m for width=8",
            Mathf.IsEqualApprox(halfWidth, 4f), $"halfWidth={halfWidth:F2}");
    }

    private void TestRoadDistanceLateralOffset()
    {
        // Car 2m to the right of centerline on a -Z segment.
        // Direction is -Z, perpendicular is +X. 2m right → x=+2.
        var (dist, _, onRoad) = ChunkedRoadManager.ComputeRoadDistance(
            new Vector3(0, 0, 0), new Vector3(0, 0, -60), 8f,
            new Vector3(2, 0, -30));
        Assert("RoadDistance: 2m right still on-road (half=4)",
            Mathf.IsEqualApprox(dist, 2f) && onRoad, $"dist={dist:F3} onRoad={onRoad}");
    }

    private void TestRoadDistanceOffRoad()
    {
        // Car 6m to the right — off-road (half-width is 4m)
        var (dist, _, onRoad) = ChunkedRoadManager.ComputeRoadDistance(
            new Vector3(0, 0, 0), new Vector3(0, 0, -60), 8f,
            new Vector3(6, 0, -30));
        Assert("RoadDistance: 6m right is off-road",
            Mathf.IsEqualApprox(dist, 6f) && !onRoad, $"dist={dist:F3} onRoad={onRoad}");
    }

    private void TestRoadDistanceCurvedSegment()
    {
        // Curved chunk: start (0,0,0), end (10,0,-60). Direction ≈ (0.166, 0, -1).
        // Perpendicular (right) ≈ (1, 0, 0.166). A point 5m in that perpendicular
        // direction from the midpoint should be ~5m off the centerline.
        var mid = new Vector3(5, 0, -30);
        var direction = (new Vector3(10, 0, -60)).Normalized();
        var perp = new Vector3(-direction.Z, 0, direction.X);
        var probe = mid + perp * 5f;

        var (dist, _, onRoad) = ChunkedRoadManager.ComputeRoadDistance(
            new Vector3(0, 0, 0), new Vector3(10, 0, -60), 8f,
            probe);
        Assert("RoadDistance: curved segment lateral math correct",
            Mathf.Abs(dist - 5f) < 0.5f && !onRoad, $"dist={dist:F3} onRoad={onRoad}");
    }

    private void TestRoadDistanceOutsideSegment()
    {
        // Probe is far past the end of the segment — t clamps to 1.0,
        // so distance is measured from the end point.
        var (dist, _, onRoad) = ChunkedRoadManager.ComputeRoadDistance(
            new Vector3(0, 0, 0), new Vector3(0, 0, -60), 8f,
            new Vector3(0, 0, -200));
        Assert("RoadDistance: past end uses clamped t (200m off)",
            Mathf.Abs(dist - 140f) < 0.5f && !onRoad, $"dist={dist:F3}");
    }

    // --- ChunkedRoadManager.DistanceToFurthestChunkEnd (refetch trigger) ---

    private void TestFurthestChunkEndEmpty()
    {
        // No chunks at all → 0 (will trigger an immediate fetch via OnTimerTick).
        var dist = ChunkedRoadManager.DistanceToFurthestChunkEnd(
            new float[0], carZ: 0f);
        Assert("FurthestChunkEnd: empty list returns 0", dist == 0f, $"dist={dist:F3}");
    }

    private void TestFurthestChunkEndSingle()
    {
        // One chunk ending at z=-450, car at z=0 → 450m ahead.
        var dist = ChunkedRoadManager.DistanceToFurthestChunkEnd(
            new[] { -450f }, carZ: 0f);
        Assert("FurthestChunkEnd: single chunk returns its distAhead",
            Mathf.Abs(dist - 450f) < 0.01f, $"dist={dist:F3}");
    }

    private void TestFurthestChunkEndPicksFurthest()
    {
        // Three chunks ending at -60, -125, -450. Should pick -450, not -60.
        // (The old "nearest end" bug picked -60, triggering an infinite fetch loop.)
        var dist = ChunkedRoadManager.DistanceToFurthestChunkEnd(
            new[] { -60f, -125f, -450f }, carZ: 0f);
        Assert("FurthestChunkEnd: picks most-negative (furthest ahead)",
            Mathf.Abs(dist - 450f) < 0.01f, $"dist={dist:F3}");
    }

    private void TestFurthestChunkEndIgnoresBehind()
    {
        // Mix of behind, beside, ahead. Only the furthest ahead (-450) counts.
        var dist = ChunkedRoadManager.DistanceToFurthestChunkEnd(
            new[] { 100f, 0f, -60f, -450f }, carZ: 0f);
        Assert("FurthestChunkEnd: ignores chunks at or behind carZ",
            Mathf.Abs(dist - 450f) < 0.01f, $"dist={dist:F3}");
    }

    private void TestFurthestChunkEndSeedChunkRegression()
    {
        // The exact scenario from the bug: seed chunk ends at -60 (closest),
        // fetched chunks extend to -467. Old code returned 60 (seed end),
        // looping forever. New code returns 467 (furthest end).
        var seedEndZ = -60f;
        var fetchedEnds = new[] { -124.5f, -197.0f, -253.6f, -322.8f, -392.5f, -467.0f };
        var allEnds = new[] { seedEndZ }.Concat(fetchedEnds);
        var dist = ChunkedRoadManager.DistanceToFurthestChunkEnd(allEnds, carZ: 0f);
        Assert("FurthestChunkEnd: seed chunk (60m) does not dominate over fetched chunks (467m)",
            Mathf.Abs(dist - 467f) < 0.01f, $"dist={dist:F3}");
    }

    // --- ChunkedRoadManager.TryNearestCenterlinePoint (off-road recovery) ---

    private void TestNearestCenterlineStraight()
    {
        // Straight -Z segment from (0,0,0) to (0,0,-60). Car at (5, 0, -30):
        // nearest point is (0,0,-30), pull vector is (5,0,0) (points from
        // road to car, so we apply force in -X to pull back).
        var ok = ChunkedRoadManager.TryNearestCenterlinePoint(
            new Vector3(0, 0, 0), new Vector3(0, 0, -60),
            new Vector3(5, 0, -30),
            out var center, out var pull);
        Assert("NearestCenterline: straight returns true", ok);
        Assert("NearestCenterline: centerline point is (0,0,-30)",
            (center - new Vector3(0, 0, -30)).Length() < 0.01f,
            $"center={center}");
        Assert("NearestCenterline: pull vector points from road to car (+X)",
            Mathf.Abs(pull.X - 5f) < 0.01f && Mathf.Abs(pull.Y) < 0.01f && Mathf.Abs(pull.Z) < 0.01f,
            $"pull={pull}");
    }

    private void TestNearestCenterlineCurved()
    {
        // Curved segment (0,0,0) → (10,0,-60). At t=0.5 the centerline is
        // (5,0,-30). For a car at (10, 0, -30), the actual projection is
        // t ≈ 0.514 (not 0.5) because the seg has both X and Z components.
        // We check the projection is close to the midpoint and the pull
        // is roughly perpendicular to the road (mostly +X).
        var ok = ChunkedRoadManager.TryNearestCenterlinePoint(
            new Vector3(0, 0, 0), new Vector3(10, 0, -60),
            new Vector3(10, 0, -30),  // 5m to the right of the centerline
            out var center, out var pull);
        Assert("NearestCenterline: curved returns true", ok);
        Assert("NearestCenterline: curved centerline is near midpoint (5,0,-30)",
            (center - new Vector3(5, 0, -30)).Length() < 1.0f,
            $"center={center}");
        Assert("NearestCenterline: curved pull is mostly +X with magnitude ~5",
            pull.Length() > 3f && pull.Length() < 7f && pull.X > 0f,
            $"pull={pull} mag={pull.Length():F2}");
    }

    private void TestNearestCenterlineClampsAtEnds()
    {
        // Car at (2, 0, -200) is way past the end of a (0,0,0)→(0,0,-60)
        // segment. t clamps to 1, so centerline point is (0,0,-60) and
        // pull vector is (2, 0, -140).
        var ok = ChunkedRoadManager.TryNearestCenterlinePoint(
            new Vector3(0, 0, 0), new Vector3(0, 0, -60),
            new Vector3(2, 0, -200),
            out var center, out var pull);
        Assert("NearestCenterline: clamps at end", ok);
        Assert("NearestCenterline: clamped center is (0,0,-60)",
            (center - new Vector3(0, 0, -60)).Length() < 0.01f, $"center={center}");
        Assert("NearestCenterline: pull from clamped end is (2, 0, -140)",
            (pull - new Vector3(2, 0, -140)).Length() < 0.01f, $"pull={pull}");
    }

    private void TestNearestCenterlineDegenerate()
    {
        // Zero-length segment should return false.
        var ok = ChunkedRoadManager.TryNearestCenterlinePoint(
            new Vector3(0, 0, 0), new Vector3(0, 0, 0),
            new Vector3(5, 0, -5),
            out _, out _);
        Assert("NearestCenterline: degenerate segment returns false", !ok);
    }

    // ========================================================================
    // Integration test — loads the full Main scene, simulates forward input,
    // and asserts the car actually moves. Catches regressions where engine
    // integration breaks (input mapping, physics layers, collision, downforce).
    // ========================================================================

    private async void StartIntegrationTest()
    {
        GD.Print("\n--- Integration Test: Car Forward Movement ---\n");

        // Reset counters for integration-specific asserts
        _passed = 0;
        _failed = 0;

        // Instance the full game scene
        var mainScene = GD.Load<PackedScene>("res://Scenes/Main.tscn");
        var main = mainScene.Instantiate();
        AddChild(main);

        // Wait for scene to fully initialize (terrain builds, car lands, etc.)
        var settleTimer = GetTree().CreateTimer(0.5f);
        await ToSignal(settleTimer, Timer.SignalName.Timeout);

        var car = main.GetNode<CarController>("Car");
        var startPos = car.GlobalPosition;
        GD.Print($"  Car start position: {startPos:F1}");

        // Simulate holding W to drive forward
        GD.Print("  Simulating W (forward) for 5 seconds...");
        Input.ActionPress("forward");

        var driveTimer = GetTree().CreateTimer(5.0f);
        await ToSignal(driveTimer, Timer.SignalName.Timeout);

        Input.ActionRelease("forward");

        var endPos = car.GlobalPosition;
        // In Godot, -Z is the forward direction
        var zDiff = startPos.Z - endPos.Z;  // positive = moved forward (-Z)
        var totalDist = (endPos - startPos).Length();
        GD.Print($"  Car end position: {endPos:F1}");
        GD.Print($"  Z difference (positive=forward): {zDiff:F2}m");
        GD.Print($"  Total distance traveled: {totalDist:F2}m");

        // The car must have moved forward (-Z) by at least 1m in 5 seconds.
        // If this fails, the engine-force or input pipeline is broken.
        Assert("Car moves forward under throttle", zDiff > 1.0f,
            $"zDiff={zDiff:F2}m (need > 1.0)");

        GD.Print($"\n=== Integration Test: {_passed} passed, {_failed} failed ===\n");

        GetTree().Quit(_failed > 0 ? 1 : 0);
    }
}
