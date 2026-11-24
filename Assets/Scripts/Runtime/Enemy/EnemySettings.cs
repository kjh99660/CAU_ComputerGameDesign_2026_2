using System;
using System.Collections.Generic;
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

    // A difficulty curve over the round. Without phases one flat schedule (Spawn Interval, Spawn Count Per Wave,
    // Maximum Enemy Count) runs the whole round, as before the phases existed.
    [Header("Spawn phases - provisional")]
    [Tooltip("Spawn schedule by battle time (the round timer's elapsed time; it holds in Pause and starts over on " +
             "Retry). From its start second on, a phase sets the cap, the interval and the wave size; Maximum Enemy " +
             "Count stays the upper limit. Empty: the flat schedule above for the whole round.")]
    [SerializeField] private EnemySpawnPhase[] spawnPhases = Array.Empty<EnemySpawnPhase>();

    [Header("Spawn warning - provisional")]
    [Tooltip("Seconds a neon ring marks the floor where an enemy will appear during Battle before it appears there. " +
             "0 spawns at once, as before. The enemies placed in Ready always appear at once.")]
    [SerializeField, Min(0f)] private float spawnWarningSeconds = 0f;
    [Tooltip("Radius of the warning ring (m).")]
    [SerializeField, Min(0.1f)] private float spawnWarningRadius = 1.3f;
    [Tooltip("Colour of a fresh warning ring (HDR, additive on the floor).")]
    [SerializeField, ColorUsage(true, true)] private Color spawnWarningColor = new Color(2.2f, 0.16f, 0.22f, 1f);
    [Tooltip("Colour the ring heats up to as the enemy is about to appear.")]
    [SerializeField, ColorUsage(true, true)] private Color spawnWarningHotColor = new Color(2.4f, 0.3f, 1.3f, 1f);

    [Header("Round layout")]
    [Tooltip("On: every round draws its spawn positions and enemy variety from the first round's seeds, so a Retry " +
             "starts with the same enemies in the same places. Off: every round (Tick) gets its own layout.")]
    [SerializeField] private bool sameLayoutEveryRound = false;

    // Two kinds of the same robot (2026-11-25 request). The weak kind is destroyed by one hit. The tough kind takes
    // Tough Hits; after a hit it survives it is shoved back, flinches and its lights flicker (no health bar). Which
    // kind a spawn is comes from its spawn phase (Tough Share). The defaults keep the first prototype: every enemy
    // is the weak kind in the original red. The special attack destroys either kind at once.
    [Header("Enemy kinds - provisional")]
    [Tooltip("Skin (tint and glow) of the weak kind: one hit destroys it.")]
    [SerializeField] private Color weakColor = new Color(0.9f, 0.22f, 0.18f, 1f);
    [Tooltip("Skin of the tough kind.")]
    [SerializeField] private Color toughColor = new Color(0.9f, 0.22f, 0.18f, 1f);
    [Tooltip("Hits that destroy a tough enemy.")]
    [SerializeField, Min(1)] private int toughHits = 2;
    [Tooltip("A damaged tough enemy's lights flicker between its skin and this. A colour with little saturation gives no glow.")]
    [SerializeField] private Color damagedColor = new Color(0.3f, 0.25f, 0.25f, 1f);
    [Tooltip("Flicker steps per second of a damaged enemy's lights.")]
    [SerializeField, Min(1f)] private float damagedFlickerHz = 14f;
    [Tooltip("A hit it survives shoves it back for this long (s); it decides nothing meanwhile.")]
    [SerializeField, Min(0f)] private float staggerSeconds = 0.3f;
    [Tooltip("Speed of that shove at its start (m/s); it fades out over the stagger.")]
    [SerializeField, Min(0f)] private float staggerPushSpeed = 5f;
    [Tooltip("After a hit it survives, further hits are ignored this long (s), so one swing cannot count twice.")]
    [SerializeField, Min(0f)] private float hitImmunitySeconds = 0.15f;
    [Tooltip("Warning ring colours of the weak kind's spawns (the tough kind uses Spawn Warning Colour).")]
    [SerializeField, ColorUsage(true, true)] private Color spawnWarningWeakColor = new Color(2.2f, 0.16f, 0.22f, 1f);
    [SerializeField, ColorUsage(true, true)] private Color spawnWarningWeakHotColor = new Color(2.4f, 0.3f, 1.3f, 1f);

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

    // How an attack reads and lands. The defaults keep the first prototype's attack (a strike halfway through
    // AttackDuration that reaches all around the enemy); the game's settings asset sets a readable wind-up instead.
    [Header("Attack telegraph and hit - provisional")]
    [Tooltip("Seconds from the start of an attack to its strike. The enemy glows the warning colour and raises its arm " +
             "slowly meanwhile. 0 keeps the strike halfway through Attack Duration.")]
    [SerializeField, Min(0f)] private float attackWindup = 0f;
    [Tooltip("Seconds the enemy stays in its attack after the swing, before it can move again.")]
    [SerializeField, Min(0f)] private float attackRecovery = 0f;
    [Tooltip("How far the strike reaches (m). 0 uses Attack Range.")]
    [SerializeField, Min(0f)] private float attackReach = 0f;
    [Tooltip("Width of the strike in front of the enemy (degrees). 360 reaches all around.")]
    [SerializeField, Range(10f, 360f)] private float attackArcDegrees = 360f;
    [Tooltip("Degrees per second the enemy can still turn toward the player while winding up, so a sidestep can beat it.")]
    [SerializeField, Min(0f)] private float windupTurnSpeed = 140f;
    [Tooltip("How many enemies may attack at the same time. 0 is no limit.")]
    [SerializeField, Min(0)] private int maximumAttackers = 0;

    // A living crowd: each enemy gets a temperament and comes in its own way, and near the player the ones that may
    // not attack yet circle, hold back and feint instead of standing still. 0 keeps the first prototype's straight
    // approach and hold.
    [Header("Liveliness - provisional")]
    [SerializeField, Range(0f, 1f)] private float liveliness = 0f;
    [Tooltip("Shares of the four temperaments: straight in / around the side / circling / stop and go.")]
    [SerializeField] private Vector4 temperamentShares = new Vector4(0.25f, 0.25f, 0.3f, 0.2f);
    [Tooltip("Waiting enemies hover between these distances from the player (m).")]
    [SerializeField, Min(0.5f)] private float hoverRingMinimum = 2.2f;
    [SerializeField, Min(0.5f)] private float hoverRingMaximum = 3.4f;
    [Tooltip("Closer than the ring plus this (m), enemies stop running and size the player up.")]
    [SerializeField, Min(0f)] private float sizeUpDistance = 4f;
    [Tooltip("Circling speed as a share of Move Speed.")]
    [SerializeField, Range(0f, 1f)] private float circleSpeedShare = 0.45f;
    [Tooltip("How far around (degrees) a flanker swings its approach.")]
    [SerializeField, Range(0f, 90f)] private float flankAngle = 55f;
    [Tooltip("Seconds a waiting enemy hovers before it steps in, for the most eager and the most careful temperament.")]
    [SerializeField] private Vector2 commitDelay = new Vector2(0.3f, 2.6f);
    [Tooltip("Spread of the time between one enemy's attacks, as a share of Attack Interval.")]
    [SerializeField, Range(0f, 1f)] private float attackIntervalSpread = 0.35f;

    public int InitialEnemyCount => initialEnemyCount;
    public float SpawnInterval => spawnInterval;
    public int SpawnCountPerWave => spawnCountPerWave;
    public float MinimumSpawnDistance => minimumSpawnDistance;
    public float MaximumSpawnDistance => Mathf.Max(minimumSpawnDistance, maximumSpawnDistance);
    public int MaximumEnemyCount => maximumEnemyCount;
    public float SpawnHeight => spawnHeight;
    public IReadOnlyList<EnemySpawnPhase> SpawnPhases => spawnPhases ?? Array.Empty<EnemySpawnPhase>();
    public bool HasSpawnPhases => spawnPhases != null && spawnPhases.Length > 0;
    // The flat schedule as one phase covering the whole round.
    public EnemySpawnPhase FlatSpawnPhase => new EnemySpawnPhase(0f, maximumEnemyCount, spawnInterval, spawnCountPerWave);
    public float SpawnWarningSeconds => spawnWarningSeconds;
    public float SpawnWarningRadius => spawnWarningRadius;
    public Color SpawnWarningColor => spawnWarningColor;
    public Color SpawnWarningHotColor => spawnWarningHotColor;
    public bool SameLayoutEveryRound => sameLayoutEveryRound;
    public Color WeakColor => weakColor;
    public Color ToughColor => toughColor;
    public int ToughHits => Mathf.Max(1, toughHits);
    public Color DamagedColor => damagedColor;
    public float DamagedFlickerHz => Mathf.Max(1f, damagedFlickerHz);
    public float StaggerSeconds => staggerSeconds;
    public float StaggerPushSpeed => staggerPushSpeed;
    public float HitImmunitySeconds => hitImmunitySeconds;
    public Color SpawnWarningWeakColor => spawnWarningWeakColor;
    public Color SpawnWarningWeakHotColor => spawnWarningWeakHotColor;

    // The schedule in effect at a moment of battle time: a phase, or the flat schedule when there are none.
    public EnemySpawnPhase SpawnPhaseAt(float battleSeconds)
    {
        int index = EnemySpawnPhases.IndexAt(spawnPhases, battleSeconds);
        return index >= 0 ? spawnPhases[index] : FlatSpawnPhase;
    }
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

    // Seconds from the start of an attack to its strike (halfway through the attack animation when not set).
    public float AttackWindup => attackWindup > 0f ? attackWindup : attackDuration * 0.5f;
    public bool TelegraphsAttacks => attackWindup > 0f;
    public float AttackRecovery => attackRecovery;
    // Whole attack: the wind-up, the rest of the swing animation and the recovery.
    public float AttackTotal => AttackWindup + attackDuration * 0.5f + attackRecovery;
    public float AttackReach => attackReach > 0f ? attackReach : attackRange;
    public float AttackArcDegrees => attackArcDegrees;
    public float WindupTurnSpeed => windupTurnSpeed;
    public int MaximumAttackers => maximumAttackers;
    public float Liveliness => liveliness;
    public Vector4 TemperamentShares => temperamentShares;
    public float HoverRingMinimum => Mathf.Min(hoverRingMinimum, hoverRingMaximum);
    public float HoverRingMaximum => Mathf.Max(hoverRingMinimum, hoverRingMaximum);
    public float SizeUpDistance => sizeUpDistance;
    public float CircleSpeedShare => circleSpeedShare;
    public float FlankAngle => flankAngle;
    public Vector2 CommitDelay => commitDelay;
    public float AttackIntervalSpread => attackIntervalSpread;
}
