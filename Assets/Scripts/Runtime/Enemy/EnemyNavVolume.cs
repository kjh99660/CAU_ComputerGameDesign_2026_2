using System.Diagnostics;
using UnityEngine;

// Marks the part of a level enemies can walk in and measures it into an EnemyNavField.
// A level without this component keeps the flat, straight-line behaviour.
// Every value here describes the enemy body and the level, not a game rule. All are provisional.
[DisallowMultipleComponent]
public sealed class EnemyNavVolume : MonoBehaviour
{
    [Tooltip("Size of the measured box, centred on this object. Rotation is ignored.")]
    [SerializeField] private Vector3 size = new(60f, 14f, 60f);
    [Tooltip("Width of one walkable cell (m). Smaller follows the level more closely and costs more.")]
    [SerializeField, Min(0.25f)] private float cellSize = 0.5f;
    [Tooltip("Level geometry: floors, walls, props. Not the player and not enemies.")]
    [SerializeField] private LayerMask geometry = 1 << 0;
    [Tooltip("How far an enemy's centre stays from walls and props (m).")]
    [SerializeField, Min(0.1f)] private float bodyRadius = 0.45f;
    [Tooltip("Head room an enemy needs (m).")]
    [SerializeField, Min(0.5f)] private float bodyHeight = 1.9f;
    [Tooltip("Things lower than this do not block: stair treads, kerbs (m).")]
    [SerializeField, Min(0f)] private float stepHeight = 0.45f;
    [Tooltip("Steepest walk between two neighbouring cells. Stairs and ramps under this angle connect floors.")]
    [SerializeField, Range(5f, 60f)] private float maxClimbDegrees = 40f;
    [Tooltip("How many floors above each other are looked for in one column.")]
    [SerializeField, Range(1, 8)] private int maxFloors = 6;
    [Tooltip("An enemy following the player steps off an open edge when the floor below is at most this far down (m). 0: enemies only come down by stairs and ramps.")]
    [SerializeField, Min(0f)] private float maxDropHeight = 7f;

    public Vector3 Size => size;
    public float CellSize => cellSize;
    // Wall-clock cost of the last measurement, for diagnostics and tests.
    public double LastBakeMilliseconds { get; private set; }

    public void Configure(Vector3 boxSize, float cell, LayerMask levelGeometry)
    {
        size = boxSize;
        cellSize = Mathf.Max(0.25f, cell);
        geometry = levelGeometry;
    }

    // Measures the level once. Only the cells that can be walked to from the seed (where the player starts) are kept,
    // which drops roofs nobody can reach and the insides of hollow buildings. keepUnreachable is for level tools
    // that want to show those cells too.
    public EnemyNavField Bake(Vector3 seed, bool keepUnreachable = false)
    {
        long started = Stopwatch.GetTimestamp();
        Physics.SyncTransforms();

        Vector3 centre = transform.position;
        int width = Mathf.Max(1, Mathf.CeilToInt(size.x / cellSize));
        int depth = Mathf.Max(1, Mathf.CeilToInt(size.z / cellSize));
        var origin = new Vector3(centre.x - width * cellSize * 0.5f, 0f, centre.z - depth * cellSize * 0.5f);
        float top = centre.y + size.y * 0.5f, bottom = centre.y - size.y * 0.5f;
        float walkableNormal = Mathf.Cos(Mathf.Min(maxClimbDegrees + 10f, 85f) * Mathf.Deg2Rad);
        float feet = stepHeight + bodyRadius;
        float head = Mathf.Max(feet, bodyHeight - bodyRadius);
        var builder = new EnemyNavField.Builder(origin, cellSize, width, depth);

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                var from = new Vector3(origin.x + (x + 0.5f) * cellSize, top, origin.z + (z + 0.5f) * cellSize);
                float lastFloor = float.PositiveInfinity;
                for (int floor = 0; floor < maxFloors && from.y > bottom; floor++)
                {
                    if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, from.y - bottom, geometry, QueryTriggerInteraction.Ignore))
                        break;
                    Vector3 point = hit.point;
                    // A floor counts when it is flat enough, an enemy fits between it and whatever is above, and
                    // nothing taller than a step stands on it.
                    if (hit.normal.y >= walkableNormal && lastFloor - point.y >= bodyHeight &&
                        !Physics.CheckCapsule(point + Vector3.up * feet, point + Vector3.up * head, bodyRadius, geometry, QueryTriggerInteraction.Ignore))
                    {
                        builder.Add(x, z, point.y);
                        lastFloor = point.y;
                    }
                    from.y = point.y - 0.02f;
                }
            }
        }

        // A drop needs an open edge (nothing at chest height on the way out, so railings and parapets stop it)
        // and a free fall onto the floor below.
        float chest = (feet + head) * 0.5f;
        bool CanDrop(Vector3 from, Vector3 to)
        {
            var above = new Vector3(to.x, from.y, to.z);
            return !Physics.CheckCapsule(from + Vector3.up * chest, above + Vector3.up * chest, bodyRadius * 0.6f, geometry, QueryTriggerInteraction.Ignore) &&
                   !Physics.CheckCapsule(to + Vector3.up * feet, above + Vector3.up * head, bodyRadius, geometry, QueryTriggerInteraction.Ignore);
        }
        EnemyNavField field = builder.Build(maxClimbDegrees, keepUnreachable ? null : seed, maxDropHeight, CanDrop);
        LastBakeMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        return field;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireCube(transform.position, size);
    }
#endif
}
