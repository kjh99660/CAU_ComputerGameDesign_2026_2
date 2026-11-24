using System;
using System.Collections.Generic;
using UnityEngine;

// One stretch of a round's spawn schedule: from StartSecond of battle time on, waves of CountPerWave enemies every
// SpawnInterval seconds until ActiveCap enemies are on the field (enemies announced by a warning ring count too).
// EnemySettings holds the list; an empty list keeps one flat schedule for the whole round.
// A phase can also open with an event (EnemyRuntime.SpawnPhaseStarted): a mass spawn of BurstOnStart enemies at once,
// and Noa's warning line on the HUD. Events fire when battle time passes the start second, so a phase that starts at
// 0 never fires one.
// ToughShare is the share of this phase's spawns (waves and mass spawn) that are the tough kind (EnemySettings:
// Tough Hits, Tough Colour); the rest are the weak kind that one hit destroys. 0 = all weak, 1 = all tough.
[Serializable]
public struct EnemySpawnPhase
{
    [Tooltip("Seconds of battle time (the round timer's elapsed time) when this phase begins.")]
    [Min(0f)] public float startSecond;
    [Tooltip("Most enemies on the field during this phase, counting the ones a warning ring has announced. " +
             "Never above Maximum Enemy Count.")]
    [Min(1)] public int activeCap;
    [Tooltip("Seconds between two waves.")]
    [Min(0.05f)] public float spawnInterval;
    [Tooltip("Enemies per wave.")]
    [Min(1)] public int countPerWave;
    [Tooltip("Mass spawn: enemies announced all at once when this phase begins, on top of its waves. They may pass the " +
             "phase cap but never Maximum Enemy Count. 0 = none.")]
    [Min(0)] public int burstOnStart;
    [Tooltip("Noa's warning when this phase begins: line N of DialogueSequenceData.spawnWarnings (1 = the first). 0 = none.")]
    [Min(0)] public int warningNumber;
    [Tooltip("Share of this phase's spawns that are the tough kind (several hits, the tough skin). 0 = all weak, 1 = all tough.")]
    [Range(0f, 1f)] public float toughShare;

    public EnemySpawnPhase(float startSecond, int activeCap, float spawnInterval, int countPerWave)
        : this(startSecond, activeCap, spawnInterval, countPerWave, 0, 0, 0f)
    {
    }

    public EnemySpawnPhase(float startSecond, int activeCap, float spawnInterval, int countPerWave, int burstOnStart,
        int warningNumber)
        : this(startSecond, activeCap, spawnInterval, countPerWave, burstOnStart, warningNumber, 0f)
    {
    }

    public EnemySpawnPhase(float startSecond, int activeCap, float spawnInterval, int countPerWave, int burstOnStart,
        int warningNumber, float toughShare)
    {
        this.startSecond = startSecond;
        this.activeCap = activeCap;
        this.spawnInterval = spawnInterval;
        this.countPerWave = countPerWave;
        this.burstOnStart = burstOnStart;
        this.warningNumber = warningNumber;
        this.toughShare = toughShare;
    }

    // The values the spawner works with, kept in their valid ranges even when a list was typed in carelessly.
    public int Cap => Mathf.Max(1, activeCap);
    public float Interval => Mathf.Max(0.05f, spawnInterval);
    public int PerWave => Mathf.Max(1, countPerWave);
    public int Burst => Mathf.Max(0, burstOnStart);
    // Index into DialogueSequenceData.spawnWarnings, -1 for no warning.
    public int WarningIndex => warningNumber > 0 ? warningNumber - 1 : -1;
    public bool HasEvent => Burst > 0 || WarningIndex >= 0;
    public float Tough => Mathf.Clamp01(toughShare);

    // Whether the phase begins inside (from, to] of battle time: once, as the clock passes its start second.
    public bool StartsBetween(float from, float to) => startSecond > from && startSecond <= to;
}

public static class EnemySpawnPhases
{
    // Index of the phase in effect at battleSeconds: the one with the latest start at or before that moment (a phase
    // starts exactly on its start second). Before the earliest start the earliest phase applies, so the list need not
    // begin at 0 or be sorted. Of two phases with the same start the first one listed wins. -1 for an empty list.
    public static int IndexAt(IReadOnlyList<EnemySpawnPhase> phases, float battleSeconds)
    {
        if (phases == null || phases.Count == 0)
            return -1;
        int current = -1, earliest = 0;
        for (int i = 0; i < phases.Count; i++)
        {
            float start = phases[i].startSecond;
            if (start < phases[earliest].startSecond)
                earliest = i;
            if (start <= battleSeconds && (current < 0 || start > phases[current].startSecond))
                current = i;
        }
        return current >= 0 ? current : earliest;
    }
}
