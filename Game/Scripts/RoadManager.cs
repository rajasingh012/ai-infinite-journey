using Godot;
using System.Collections.Generic;

namespace AiInfiniteJourney;

/// <summary>
/// Spawns and despawns RoadStrip instances as the car drives, creating an
/// endless road. Maintains a sliding window of strips centered on the car's
/// Z position. Also applies theme-based colour tinting from the current
/// JourneyPlan so each journey looks different.
/// </summary>
[GlobalClass]
public partial class RoadManager : Node3D
{
    [Export] public NodePath? CarPath { get; set; }
    [Export] public NodePath? GroundPath { get; set; }
    [Export] public int LookAhead = 6;   // strips ahead of the car
    [Export] public int LookBehind = 4;  // strips behind the car
    [Export] public float StripLength = 50.0f;

    private readonly List<RoadStrip> _strips = new();
    private RigidBody3D? _car;
    private RoadChunk? _ground;
    private int _centerIndex; // chunk index of the strip currently under the car

    public override void _Ready()
    {
        if (CarPath is not null)
            _car = GetNodeOrNull<RigidBody3D>(CarPath);
        if (_car is null)
            _car = GetNodeOrNull<RigidBody3D>("../Car");

        if (GroundPath is not null)
            _ground = GetNodeOrNull<RoadChunk>(GroundPath);
        if (_ground is null)
            _ground = GetNodeOrNull<RoadChunk>("../World");

        // Spawn initial window centred on the car start position (Z=0).
        _centerIndex = 0;
        RefreshWindow();

        // Listen for journey plan changes so we can re-tint the road.
        JourneyState.Instance.Connect(nameof(JourneyState.PlanChanged),
            Callable.From(() => ApplyTheme()));
        ApplyTheme();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_car is null) return;

        var carZ = _car.GlobalPosition.Z;
        var newCenter = (int)(carZ / StripLength);

        if (newCenter != _centerIndex)
        {
            _centerIndex = newCenter;
            RefreshWindow();
        }
    }

    private void RefreshWindow()
    {
        // Determine which chunk indices should exist.
        var first = _centerIndex - LookBehind;
        var last = _centerIndex + LookAhead;

        var wanted = new HashSet<int>();
        for (var i = first; i <= last; i++)
            wanted.Add(i);

        // Remove strips outside the window.
        for (var i = _strips.Count - 1; i >= 0; i--)
        {
            var strip = _strips[i];
            var idx = (int)(strip.Position.Z / StripLength);
            if (!wanted.Contains(idx))
            {
                strip.QueueFree();
                _strips.RemoveAt(i);
            }
        }

        // Add strips that are missing.
        var existing = new HashSet<int>();
        foreach (var s in _strips)
            existing.Add((int)(s.Position.Z / StripLength));

        for (var i = first; i <= last; i++)
        {
            if (existing.Contains(i)) continue;
            var strip = new RoadStrip { Position = new Vector3(0, 0, i * StripLength) };
            AddChild(strip);
            _strips.Add(strip);
        }

        // Apply theme tint to any strips that don't have it yet.
        ApplyTheme();

        // Tint the ground, too.
        var groundColor = ThemeToGroundColor(JourneyState.Instance.Plan?.Theme);
        _ground?.SetGroundTint(groundColor);
    }

    /// <summary>
    /// Re-apply theme colour to all current strips. Called when a new
    /// JourneyPlan arrives.
    /// </summary>
    private void ApplyTheme()
    {
        var roadColor = ThemeToRoadColor(JourneyState.Instance.Plan?.Theme);
        foreach (var strip in _strips)
            strip.SetColor(roadColor);
    }

    // --- Theme colour lookup ------------------------------------------------

    private static Color ThemeToRoadColor(string? theme) => theme?.ToLowerInvariant() switch
    {
        "mountain" => new Color(0.25f, 0.27f, 0.30f),   // gray-blue asphalt
        "desert" => new Color(0.35f, 0.30f, 0.22f),     // sandy
        "coastal" => new Color(0.28f, 0.28f, 0.30f),    // warm gray
        "forest" => new Color(0.22f, 0.26f, 0.22f),     // dark green
        "countryside" => new Color(0.30f, 0.28f, 0.24f), // warm brown
        "urban" => new Color(0.18f, 0.18f, 0.20f),      // dark gray
        _ => new Color(0.20f, 0.20f, 0.22f),             // default asphalt
    };

    private static Color ThemeToGroundColor(string? theme) => theme?.ToLowerInvariant() switch
    {
        "mountain" => new Color(0.22f, 0.28f, 0.22f),   // gray-green grass
        "desert" => new Color(0.40f, 0.35f, 0.20f),     // tan sand
        "coastal" => new Color(0.30f, 0.28f, 0.20f),    // light brown
        "forest" => new Color(0.12f, 0.28f, 0.12f),     // deep green
        "countryside" => new Color(0.25f, 0.38f, 0.18f), // bright green
        "urban" => new Color(0.22f, 0.22f, 0.22f),      // brown-gray
        _ => new Color(0.18f, 0.32f, 0.16f),             // default grass
    };
}
