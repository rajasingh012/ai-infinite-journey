using Godot;

namespace AiInfiniteJourney;

/// <summary>
/// When a road chunk is marked as dead_end, spawn a solid wall across the
/// road at the end of the segment. The car crashes into it — dead stops.
/// </summary>
public partial class DeadEndBlocker : Node3D
{
    /// <summary>
    /// Build a road-blocking wall at the end of a chunk.
    /// Called by ChunkedRoadManager when the chunk's DeadEnd flag is true.
    /// </summary>
    public static StaticBody3D Build(Vector3 start, Vector3 end, float width)
    {
        var direction = (end - start).Normalized();
        var perpendicular = new Vector3(-direction.Z, 0, direction.X);

        var wallCenter = end; // at the road's termination point

        var body = new StaticBody3D();
        var wallSize = new Vector3(width * 1.2f, 10f, 1.5f);

        var shape = new BoxShape3D { Size = wallSize };
        var collision = new CollisionShape3D { Shape = shape };
        body.AddChild(collision);

        // Red-orange hazard wall — visually unambiguous
        var mesh = new BoxMesh
        {
            Size = wallSize,
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.8f, 0.15f, 0.1f),
                Metallic = 0.3f,
                Roughness = 0.6f,
                EmissionEnabled = true,
                Emission = new Color(0.4f, 0.05f, 0.02f),
            },
        };
        var meshInstance = new MeshInstance3D { Mesh = mesh };
        body.AddChild(meshInstance);

        // Position the wall across the road, centered on end, rotated to face the road.
        body.Position = new Vector3(end.X, 0f, end.Z);
        body.Rotation = new Vector3(0, Mathf.Atan2(direction.X, -direction.Z), 0);
        body.Name = "dead_end_wall";

        return body;
    }
}