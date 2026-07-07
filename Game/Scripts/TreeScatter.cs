using Godot;
using System;

namespace AiInfiniteJourney;

/// <summary>
/// Procedurally scatters a few tree prefabs (loaded from FBX) along the sides of the road.
/// Phase 2 stub: random placement with a keep-out zone around the road surface.
/// Phase 3+ will drive placement from the AI World Director's landmark hints.
/// </summary>
[GlobalClass]
public partial class TreeScatter : Node3D
{
    [Export] public string[] TreeMeshPaths { get; set; } = new[]
    {
        "res://Assets/Models/Trees/tree_default.fbx",
        "res://Assets/Models/Trees/tree_fat.fbx",
        "res://Assets/Models/Trees/tree_cone.fbx",
    };
    [Export] public float RoadLength { get; set; } = 500.0f;
    [Export] public float RoadWidth { get; set; } = 8.0f;
    [Export] public float MinOffset { get; set; } = 6.0f;   // min distance from road centerline
    [Export] public float MaxOffset { get; set; } = 35.0f;  // max distance from road centerline
    [Export] public int TreeCount { get; set; } = 60;
    [Export] public ulong RngSeed { get; set; } = 42;

    public override void _Ready()
    {
        var rng = new RandomNumberGenerator { Seed = RngSeed };
        var materials = new[]
        {
            new StandardMaterial3D { AlbedoColor = new Color(0.20f, 0.45f, 0.20f), Roughness = 0.9f },
            new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.55f, 0.25f), Roughness = 0.9f },
            new StandardMaterial3D { AlbedoColor = new Color(0.40f, 0.60f, 0.30f), Roughness = 0.9f },
        };

        for (var i = 0; i < TreeCount; i++)
        {
            var path = TreeMeshPaths[rng.RandiRange(0, TreeMeshPaths.Length - 1)];
            var treeScene = GD.Load<PackedScene>(path);
            if (treeScene == null) continue;
            var tree = treeScene.Instantiate<Node3D>();

            // Random position: z runs along the road, x is offset to the side.
            var z = rng.RandfRange(-RoadLength * 0.5f, RoadLength * 0.5f);
            var sideSign = rng.RandiRange(0, 1) == 0 ? -1.0f : 1.0f;
            var x = sideSign * rng.RandfRange(MinOffset, MaxOffset);
            tree.Position = new Vector3(x, 0.0f, z);

            // Random yaw rotation and scale jitter for visual variety.
            tree.Rotation = new Vector3(0, rng.RandfRange(0, Mathf.Tau), 0);
            var s = rng.RandfRange(0.9f, 1.3f);
            tree.Scale = new Vector3(s, s * rng.RandfRange(0.95f, 1.15f), s);

            // Apply a simple green material to all meshes in the tree.
            ApplyRandomMaterial(tree, materials[rng.RandiRange(0, materials.Length - 1)]);

            AddChild(tree);
        }
    }

    private static void ApplyRandomMaterial(Node node, StandardMaterial3D mat)
    {
        if (node is MeshInstance3D mi)
            mi.MaterialOverride = mat;
        foreach (var child in node.GetChildren())
            ApplyRandomMaterial(child, mat);
    }
}
