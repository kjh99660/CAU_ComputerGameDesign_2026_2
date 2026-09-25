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
    [SerializeField] private float spawnHeight = 1f;

    [Header("AI - provisional")]
    [SerializeField, Min(0f)] private float moveSpeed = 3.5f;
    [SerializeField, Min(0.1f)] private float attackRange = 2f;
    [SerializeField, Min(0.05f)] private float attackInterval = 1.5f;
    [SerializeField, Min(0.01f)] private float attackDuration = 0.35f;
    [SerializeField, Min(0.1f)] private float separationDistance = 2f;
    [SerializeField, Min(0f)] private float separationWeight = 1.5f;
    [SerializeField, Min(0.5f)] private float spatialCellSize = 4f;

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
    public float NormalKnockbackForce => normalKnockbackForce;
    public float KnockbackUpwardForce => knockbackUpwardForce;
    public float KnockbackFallbackDuration => knockbackFallbackDuration;
    public float DeathDuration => deathDuration;
}
