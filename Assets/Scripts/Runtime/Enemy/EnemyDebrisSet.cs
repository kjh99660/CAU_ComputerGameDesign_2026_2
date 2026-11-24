using System;
using UnityEngine;

// The parts a robot enemy breaks into (head, chest, pelvis, arms, legs). Made from the enemy's skinned mesh by
// Tools/REBOOT Feel/Build Enemy Remains: every triangle goes to the part of the bone that moves it most, and each
// part is stored in the space of its anchor bone (in metres), so placing it on that bone's current position and
// rotation puts it where it was on the body.
[CreateAssetMenu(fileName = "EnemyDebrisSet", menuName = "REBOOT/Enemy Debris Set")]
public sealed class EnemyDebrisSet : ScriptableObject
{
    [Serializable]
    public sealed class Part
    {
        public string name;
        [Tooltip("Name of the bone the part is placed on, as it appears in the enemy prefab.")]
        public string bone;
        public Mesh mesh;
        [Tooltip("The anchor bone's world scale when the part was built; a bigger enemy gets a bigger part.")]
        public float bakedScale = 1f;
        [Tooltip("Rough radius in metres, for landing on the floor.")]
        public float radius = 0.15f;
    }

    public Part[] parts = Array.Empty<Part>();
}
