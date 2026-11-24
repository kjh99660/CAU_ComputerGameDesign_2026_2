using System;
using UnityEngine;

// Clips and mix for every SfxCue. Tools/REBOOT Audio/Generate Sound Effects fills it: recorded CC0 sounds from
// Assets/ThirdParty (sources in each folder's README) and two generated layers in Assets/Sound/SFX. Any clip can be
// swapped here. Every number is a provisional starting point to be tuned in play tests.
[CreateAssetMenu(fileName = "SfxSettings", menuName = "REBOOT/Sfx Settings")]
public sealed class SfxSettings : ScriptableObject
{
    [Range(0f, 1f)] public float masterVolume = 0.7f;
    [Tooltip("AudioSources made once at start-up. The project allows 32 real voices; the music and the ambience use one each.")]
    [Range(4, 24)] public int voiceCount = 20;
    [Tooltip("Pitch of gameplay sounds that start during the Finishing slow motion.")]
    [Range(0.5f, 1f)] public float finishingPitch = 0.85f;
    [Tooltip("A swing whose charge scale reaches this uses the heavy swing sound.")]
    [Min(1f)] public float heavySwingScale = 1.05f;
    [Tooltip("Enemy sounds lean towards where the enemy is (see each cue's spatial blend) and fade with distance.")]
    [Min(0.1f)] public float spatialMinDistance = 8f;
    [Min(1f)] public float spatialMaxDistance = 40f;
    public SfxCueSettings[] cues = Array.Empty<SfxCueSettings>();

    // A quiet machine hum of the city under the music. It runs through Battle and Finishing, holds in Pause and starts
    // over from its beginning, faded in, after a Retry. Leave the clip empty for none.
    [Header("Ambience - provisional")]
    [Tooltip("A seamless loop. The music (the scene's Sound object, volume 0.5) stays far above it.")]
    public AudioClip ambienceClip;
    [Tooltip("Volume in Battle and Finishing, times the master volume.")]
    [Range(0f, 1f)] public float ambienceVolume = 0.12f;
    [Tooltip("Share of that volume before and after the battle (Ready, dialogue, countdown, result). 0 is silent there.")]
    [Range(0f, 1f)] public float ambienceOutsideBattle = 0.5f;
    [Tooltip("Seconds to fade from silence to the battle volume, in real time.")]
    [Min(0.01f)] public float ambienceFadeSeconds = 0.8f;

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
