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
}
