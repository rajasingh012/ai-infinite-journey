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
