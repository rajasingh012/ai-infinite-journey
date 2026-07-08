using Godot;
using System.Collections.Generic;

namespace AiInfiniteJourney;

/// <summary>
/// Spawns and despawns SplineRoad segments as the car drives, creating an
/// endless curved road that follows the terrain. Maintains a sliding window
/// of segments centered on the car's Z position. Applies theme-based colour
/// tinting from the current JourneyPlan.
/// </summary>
[GlobalClass]
public partial class RoadManager : Node3D
{
    [Export] public NodePath? CarPath { get; set; }
    [Export] public int LookAhead = 6;
    [Export] public int LookBehind = 4;
    [Export] public float StripLength = 50.0f;

    private readonly List<SplineRoad> _segments = new();
    private RigidBody3D? _car;
    private int _centerIndex;

    public override void _Ready()
    {
        if (CarPath is not null)
            _car = GetNodeOrNull<RigidBody3D>(CarPath);
        if (_car is null)
            _car = GetNodeOrNull<RigidBody3D>("../Car");

        _centerIndex = 0;
        RefreshWindow();

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
        var first = _centerIndex - LookBehind;
        var last = _centerIndex + LookAhead;

        var wanted = new HashSet<int>();
        for (var i = first; i <= last; i++)
            wanted.Add(i);

        // Remove segments outside the window.
        for (var i = _segments.Count - 1; i >= 0; i--)
        {
            var seg = _segments[i];
            var idx = Mathf.RoundToInt(seg.Position.Z / StripLength);
            if (!wanted.Contains(idx))
            {
                seg.QueueFree();
                _segments.RemoveAt(i);
            }
        }

        // Add missing segments.
        var existing = new HashSet<int>();
        foreach (var s in _segments)
            existing.Add(Mathf.RoundToInt(s.Position.Z / StripLength));

        for (var i = first; i <= last; i++)
        {
            if (existing.Contains(i)) continue;
            var seg = new SplineRoad
            {
                Position = new Vector3(0, 0, i * StripLength),
            };
            AddChild(seg);
            _segments.Add(seg);
        }

        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var roadColor = ThemeToRoadColor(JourneyState.Instance.Plan?.Theme);
        foreach (var seg in _segments)
            seg.SetColor(roadColor);
    }

    private static Color ThemeToRoadColor(string? theme) => theme?.ToLowerInvariant() switch
    {
        "mountain" => new Color(0.25f, 0.27f, 0.30f),
        "desert" => new Color(0.35f, 0.30f, 0.22f),
        "coastal" => new Color(0.28f, 0.28f, 0.30f),
        "forest" => new Color(0.22f, 0.26f, 0.22f),
        "countryside" => new Color(0.30f, 0.28f, 0.24f),
        "urban" => new Color(0.18f, 0.18f, 0.20f),
        _ => new Color(0.20f, 0.20f, 0.22f),
    };
}
