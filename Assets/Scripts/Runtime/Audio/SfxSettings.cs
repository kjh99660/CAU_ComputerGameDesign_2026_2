using System;
using UnityEngine;

// Clips and mix for every SfxCue. Tools/REBOOT Audio/Generate Sound Effects fills it: recorded CC0 sounds from
// Assets/ThirdParty (sources in each folder's README) and two generated layers in Assets/Sound/SFX. Any clip can be
// swapped here. Every number is a provisional starting point to be tuned in play tests.
[CreateAssetMenu(fileName = "SfxSettings", menuName = "REBOOT/Sfx Settings")]
public sealed class SfxSettings : ScriptableObject
{
    [Range(0f, 1f)] public float masterVolume = 0.7f;
    [Tooltip("AudioSources made once at start-up. The project allows 32 real voices and the music uses one.")]
    [Range(4, 24)] public int voiceCount = 20;
    [Tooltip("Pitch of gameplay sounds that start during the Finishing slow motion.")]
    [Range(0.5f, 1f)] public float finishingPitch = 0.85f;
    [Tooltip("A swing whose charge scale reaches this uses the heavy swing sound.")]
    [Min(1f)] public float heavySwingScale = 1.05f;
    [Tooltip("Enemy sounds lean towards where the enemy is (see each cue's spatial blend) and fade with distance.")]
    [Min(0.1f)] public float spatialMinDistance = 8f;
    [Min(1f)] public float spatialMaxDistance = 40f;
    public SfxCueSettings[] cues = Array.Empty<SfxCueSettings>();

    public SfxCueSettings Find(SfxCue cue)
    {
        if (cues == null) return null;
        foreach (SfxCueSettings settings in cues)
            if (settings != null && settings.cue == cue)
                return settings;
        return null;
    }
}

[Serializable]
public sealed class SfxCueSettings
{
    public SfxCue cue;
    [Tooltip("One is picked at random each time, never the same one twice in a row.")]
    public AudioClip[] clips = Array.Empty<AudioClip>();
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Base pitch; below 1 makes a sound deeper and duller.")]
    [Range(0.5f, 2f)] public float pitch = 1f;
    [Range(0f, 0.3f)] public float pitchJitter = 0.03f;
    [Tooltip("At this many voices the oldest one of this cue starts over.")]
    [Range(1, 8)] public int maxVoices = 2;
    [Tooltip("Seconds before the cue may start again; keeps a crowd of hits from stacking into one loud burst.")]
    [Min(0f)] public float minInterval = 0.03f;
    [Range(0f, 1f)] public float spatialBlend;
}
