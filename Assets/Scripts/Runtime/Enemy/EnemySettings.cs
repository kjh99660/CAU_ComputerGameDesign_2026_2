using UnityEngine;

[CreateAssetMenu(fileName = "EnemySettings", menuName = "Game Demo/Enemy Settings")]
public sealed class EnemySettings : ScriptableObject
{
    [Header("Spawn - provisional")]
    [SerializeField, Min(0)] private int initialEnemyCount = 5;
    [SerializeField, Min(0.05f)] private float spawnInterval = 1f;
    [SerializeField, Min(1)] private int spawnCountPerWave = 5;
    [SerializeField, Min(0f)] private float minimumSpawnDistance = 15f;
    [SerializeField, Min(0f)] private float maximumSpawnDistance = 25f;
    [SerializeField, Min(1)] private int maximumEnemyCount = 100;
    [Tooltip("Height offset from the player's restored round-start position. Does not follow later player elevation.")]
    [SerializeField] private float spawnHeight = 0f;

    [Header("AI - provisional")]
    [SerializeField, Min(0f)] private float moveSpeed = 3.5f;
    [SerializeField, Min(0.1f)] private float attackRange = 2f;
    [SerializeField, Min(0.05f)] private float attackInterval = 1.5f;
    [SerializeField, Min(0.01f)] private float attackDuration = 0.35f;
    [SerializeField, Min(0.1f)] private float separationDistance = 2f;
    [SerializeField, Min(0f)] private float separationWeight = 1.5f;
    [SerializeField, Min(0.5f)] private float spatialCellSize = 4f;

    // Presentation of how the crowd moves. None of these changes range, cooldown, damage or score rules.
    [Header("Locomotion feel - provisional")]
    [Tooltip("How fast an enemy reaches its move speed (m/s^2). Stops enemies from starting and stopping in one frame.")]
    [SerializeField, Min(0f)] private float acceleration = 14f;
    [Tooltip("Turn rate in degrees per second. Stops enemies from snapping to a new facing in one frame.")]
    [SerializeField, Min(0f)] private float turnSpeed = 420f;
    [Tooltip("Per-enemy move speed spread (0.12 = 88%..112%), so a wave does not march in step.")]
    [SerializeField, Range(0f, 0.5f)] private float speedVariance = 0.12f;
    [Tooltip("Per-enemy visual size spread. The collider is not scaled.")]
    [SerializeField, Range(0f, 0.2f)] private float scaleVariance = 0.04f;
    [Tooltip("Ground speed (m/s) the Chase (run) clip is authored for. Playback follows the real speed so feet do not slide. Rebuild the clips after changing it.")]
    [SerializeField, Min(0.1f)] private float runClipSpeed = 3.5f;
    [Tooltip("Ground speed (m/s) the Walk clip is authored for. Rebuild the clips after changing it.")]
    [SerializeField, Min(0.1f)] private float walkClipSpeed = 1.1f;
    [Tooltip("Above this speed (m/s) the run clip is shown, below it the walk clip.")]
    [SerializeField, Min(0.1f)] private float walkToRunSpeed = 2f;
    [Tooltip("An enemy does not walk into another enemy closer than this (m). Late arrivals wait behind a full ring instead of standing inside each other. 0 turns it off.")]
    [SerializeField, Min(0f)] private float bodySpacing = 1.1f;
    [Tooltip("Enemies closer to the player than this step back, because the player is no longer body-blocked by the crowd. 0 turns it off.")]
    [SerializeField, Min(0f)] private float playerPersonalRadius = 0.9f;
    [SerializeField, Min(0f)] private float playerPushSpeed = 12f;
    [Tooltip("How many times a spawn position is re-drawn. Flat level: while level geometry stands between it and the player. Level with floors: while it is not a walkable spot out of the player's sight.")]
    [SerializeField, Range(0, 16)] private int spawnVisibilityAttempts = 8;

    // Only read where the scene has an EnemyNavVolume. A flat arena ignores all three.
    [Header("Levels with floors above each other - provisional")]
    [Tooltip("Enemies and the player only reach each other within this height difference (m), so an enemy under a deck cannot hit the player on it. Keep it below the height of one storey.")]
    [SerializeField, Min(0.1f)] private float verticalReach = 2.2f;
    [Tooltip("How often the shared path to the player is refreshed (s).")]
    [SerializeField, Min(0f)] private float pathRefreshSeconds = 0.1f;
    [Tooltip("A spawn point is skipped when the walk from it to the player is longer than the maximum spawn distance times this.")]
    [SerializeField, Min(1f)] private float spawnDetourLimit = 2f;

    [Header("Hit - provisional")]
    [SerializeField, Min(0f)] private float normalKnockbackForce = 8f;
    [SerializeField, Min(0f)] private float knockbackUpwardForce = 3f;
    [SerializeField, Min(0.05f)] private float knockbackFallbackDuration = 1.25f;
    [SerializeField, Min(0f)] private float deathDuration = 0.25f;

    public int InitialEnemyCount => initialEnemyCount;
    public float SpawnInterval => spawnInterval;
    public int SpawnCountPerWave => spawnCountPerWave;
    public float MinimumSpawnDistance => minimumSpawnDistance;
    public float MaximumSpawnDistance => Mathf.Max(minimumSpawnDistance, maximumSpawnDistance);
    public int MaximumEnemyCount => maximumEnemyCount;
    public float SpawnHeight => spawnHeight;
    public float MoveSpeed => moveSpeed;
    public float AttackRange => attackRange;
    public float AttackInterval => attackInterval;
    public float AttackDuration => attackDuration;
    public float SeparationDistance => separationDistance;
    public float SeparationWeight => separationWeight;
    public float SpatialCellSize => spatialCellSize;
    public float Acceleration => acceleration;
    public float TurnSpeed => turnSpeed;
    public float SpeedVariance => speedVariance;
    public float ScaleVariance => scaleVariance;
    public float RunClipSpeed => runClipSpeed;
    public float WalkClipSpeed => walkClipSpeed;
    public float WalkToRunSpeed => walkToRunSpeed;
    public float BodySpacing => bodySpacing;
    public float PlayerPersonalRadius => playerPersonalRadius;
    public float PlayerPushSpeed => playerPushSpeed;
    public int SpawnVisibilityAttempts => spawnVisibilityAttempts;
    public float VerticalReach => verticalReach;
    public float PathRefreshSeconds => pathRefreshSeconds;
    public float SpawnDetourLimit => spawnDetourLimit;
    public float NormalKnockbackForce => normalKnockbackForce;
    public float KnockbackUpwardForce => knockbackUpwardForce;
    public float KnockbackFallbackDuration => knockbackFallbackDuration;
    public float DeathDuration => deathDuration;
}
