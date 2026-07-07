using Godot;
using System.Collections.Generic;

namespace AiInfiniteJourney;

/// <summary>
/// Spawns and despawns terrain tiles (RoadChunk instances) in a 2D grid
/// around the car. Each tile is a 50x50 heightmapped terrain section.
/// The sliding window moves with the car, loading new tiles and unloading
/// old ones.
/// </summary>
[GlobalClass]
public partial class TerrainManager : Node3D
{
    [Export] public NodePath? CarPath { get; set; }
    [Export] public int LookAhead = 4;   // tiles ahead
    [Export] public int LookBehind = 2;  // tiles behind
    [Export] public int SideSpan = 1;    // tiles to each side (0 = single row under road)
    [Export] public float TileSize = 50.0f;

    private readonly Dictionary<(int z, int x), RoadChunk> _tiles = new();
    private RigidBody3D? _car;

    public override void _Ready()
    {
        if (CarPath is not null)
            _car = GetNodeOrNull<RigidBody3D>(CarPath);
        if (_car is null)
            _car = GetNodeOrNull<RigidBody3D>("../Car");

        // Spawn initial set centred on the car.
        RefreshTiles(0, 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_car is null) return;

        var carPos = _car.GlobalPosition;
        var cz = (int)(carPos.Z / TileSize);
        var cx = (int)(carPos.X / TileSize);

        RefreshTiles(cz, cx);
    }

    private void RefreshTiles(int centerZ, int centerX)
    {
        var wanted = new HashSet<(int, int)>();
        for (var dz = -LookBehind; dz <= LookAhead; dz++)
        for (var dx = -SideSpan; dx <= SideSpan; dx++)
            wanted.Add((centerZ + dz, centerX + dx));

        // Remove tiles outside the window.
        var toRemove = new List<(int, int)>();
        foreach (var kv in _tiles)
        {
            if (!wanted.Contains(kv.Key))
                toRemove.Add(kv.Key);
        }
        foreach (var key in toRemove)
        {
            _tiles[key].QueueFree();
            _tiles.Remove(key);
        }

        // Add missing tiles.
        foreach (var (kz, kx) in wanted)
        {
            if (_tiles.ContainsKey((kz, kx))) continue;
            var tile = new RoadChunk
            {
                Position = new Vector3(kx * TileSize, 0, kz * TileSize),
            };
            AddChild(tile);
            _tiles[(kz, kx)] = tile;
        }
    }
}
