using UnityEngine;

[CreateAssetMenu(menuName = "Game Demo/Combat VFX Settings")]
public sealed class CombatVfxSettings : ScriptableObject
{
    public GameObject slashPrefab;
    public GameObject impactPrefab;
    [Header("Provisional placement and size (player local space)")]
    public Vector3 slashOffset = new Vector3(0f, 1f, 1.25f);
    public Vector3 slashEulerOffset;
    [Min(0.01f)] public float slashScale = 1f;
    public Vector3 impactOffset = new Vector3(0f, 1f, 0f);
    public Vector3 impactEulerOffset;
    [Min(0.01f)] public float impactScale = 1f;
    [Header("Pool limits / scaled-time safety timeout")]
    [Min(1)] public int maximumInstancesPerEffect = 128;
    [Min(0.1f)] public float maximumLifetime = 3f;

    // Particle burst of the gauge special. One burst at the centre and a ring of copies around it, all pooled.
    // Sizes are written for a 6 m special radius and follow the real radius.
    [Header("Special attack burst - provisional")]
    public GameObject specialBurstPrefab;
    public GameObject specialRadialPrefab;
    public Vector3 specialOffset = new Vector3(0f, 1f, 0f);
    [Min(0.01f)] public float specialBurstScale = 0.8f;
    [Range(0, 24)] public int specialRadialCount = 10;
    [Tooltip("Where the ring of copies sits, as a share of the special radius.")]
    [Range(0f, 1f)] public float specialRadialDistance = 0.5f;
    public Vector3 specialRadialEulerOffset;
    [Min(0.01f)] public float specialRadialScale = 1.7f;

    // Field-of-view pulse (degrees taken off, then released). 0 turns a pulse off.
    [Header("Camera zoom pulse - provisional")]
    [Range(0f, 10f)] public float swingZoomDegrees = 0.8f;
    [Range(0f, 10f)] public float hitZoomDegrees = 2.6f;
    [Range(0f, 20f)] public float specialZoomDegrees = 6f;
    [Min(0.01f)] public float zoomInSeconds = 0.05f;
    [Min(0.01f)] public float zoomOutSeconds = 0.24f;
    [Tooltip("How far the view swings back out past its resting value, as a share of the pulse.")]
    [Range(0f, 1f)] public float zoomOvershoot = 0.2f;
}
