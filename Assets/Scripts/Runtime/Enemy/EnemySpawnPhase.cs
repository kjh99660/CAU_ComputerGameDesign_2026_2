using System;
using System.Collections.Generic;
using UnityEngine;

// One stretch of a round's spawn schedule: from StartSecond of battle time on, waves of CountPerWave enemies every
// SpawnInterval seconds until ActiveCap enemies are on the field (enemies announced by a warning ring count too).
// EnemySettings holds the list; an empty list keeps one flat schedule for the whole round.
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

    public EnemySpawnPhase(float startSecond, int activeCap, float spawnInterval, int countPerWave)
    {
        this.startSecond = startSecond;
        this.activeCap = activeCap;
        this.spawnInterval = spawnInterval;
        this.countPerWave = countPerWave;
    }

    // The values the spawner works with, kept in their valid ranges even when a list was typed in carelessly.
    public int Cap => Mathf.Max(1, activeCap);
    public float Interval => Mathf.Max(0.05f, spawnInterval);
    public int PerWave => Mathf.Max(1, countPerWave);
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
