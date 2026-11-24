using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Fills the SfxSettings asset: which clips each cue plays and the provisional mix. The clips are recorded CC0 sounds in
// Assets/ThirdParty (each folder's README names the source and licence) plus the two generated layers that
// RebootSfxSynth writes to Assets/Sound/SFX. Also sets the import settings of every clip it uses.
// A cue that already has clips keeps them and its mix values, so tuning done in the inspector survives running the
// builder again; clear a cue's clips to take the listed ones again.
//   Unity -batchmode -executeMethod RebootSfxBuilder.BuildBatch -quit
public static class RebootSfxBuilder
{
    public const string Folder = "Assets/Sound/SFX";
    public const string SettingsPath = "Assets/Resources/SfxSettings.asset";
    private const string Impact = "Assets/ThirdParty/Kenney_ImpactSounds/";
    private const string SciFi = "Assets/ThirdParty/Kenney_SciFiSounds/";
    private const string Interface = "Assets/ThirdParty/Kenney_InterfaceSounds/";
    private const string Swishes = "Assets/ThirdParty/artisticdude_Swishes/";

    // Recorded sounds per cue (generated layers are added from RebootSfxSynth by their cue name).
    private static readonly Dictionary<SfxCue, string[]> Recorded = new Dictionary<SfxCue, string[]>
    {
        { SfxCue.Swing, new[] { Swishes + "swish-2.wav", Swishes + "swish-4.wav", Swishes + "swish-5.wav", Swishes + "swish-6.wav", Swishes + "swish-8.wav" } },
        { SfxCue.SwingHeavy, new[] { Swishes + "swish-3.wav", Swishes + "swish-7.wav" } },
        { SfxCue.Hit, new[] { Impact + "impactPunch_heavy_000.ogg", Impact + "impactPunch_heavy_001.ogg", Impact + "impactPunch_heavy_002.ogg", Impact + "impactPunch_heavy_003.ogg", Impact + "impactPunch_heavy_004.ogg" } },
        { SfxCue.HitMetal, new[] { SciFi + "impactMetal_000.ogg", SciFi + "impactMetal_003.ogg", SciFi + "impactMetal_004.ogg" } },
        { SfxCue.EnemyDown, new[] { SciFi + "explosionCrunch_000.ogg", SciFi + "explosionCrunch_001.ogg", SciFi + "explosionCrunch_003.ogg" } },
        { SfxCue.PlayerHurt, new[] { Impact + "impactSoft_heavy_001.ogg", Impact + "impactSoft_heavy_002.ogg", Impact + "impactSoft_heavy_004.ogg" } },
        { SfxCue.Dodge, new[] { Swishes + "swish-10.wav", Swishes + "swish-11.wav", Swishes + "swish-12.wav", Swishes + "swish-13.wav" } },
        { SfxCue.SkillDash, new[] { Swishes + "swish-1.wav", Swishes + "swish-9.wav" } },
        { SfxCue.Special, new[] { SciFi + "lowFrequency_explosion_000.ogg", SciFi + "lowFrequency_explosion_001.ogg" } },
        { SfxCue.GaugeFull, new[] { Interface + "maximize_008.ogg" } },
        { SfxCue.CountdownTick, new[] { Interface + "tick_004.ogg" } },
        { SfxCue.CountdownGo, new[] { Interface + "confirmation_001.ogg" } },
        { SfxCue.TimeUp, new[] { Impact + "impactBell_heavy_001.ogg" } },
        { SfxCue.Result, new[] { Interface + "confirmation_004.ogg" } },
        { SfxCue.UiClick, new[] { Interface + "back_003.ogg" } },
        { SfxCue.UiConfirm, new[] { Interface + "select_004.ogg" } },
        { SfxCue.UiBack, new[] { Interface + "back_002.ogg", Interface + "back_004.ogg" } },
        { SfxCue.DialogueNext, new[] { Interface + "tick_002.ogg" } },
    };

    [MenuItem("Tools/REBOOT Audio/Generate Sound Effects")]
    public static void Generate()
    {
        SfxSettings settings = Build();
        Selection.activeObject = settings;
    }

    public static void BuildBatch()
    {
        Build();
        Debug.Log("REBOOT sound effects set up in " + SettingsPath);
    }

    public static SfxSettings Build()
    {
        Directory.CreateDirectory(Folder);
        List<RebootSfxSynth.Sound> sounds = RebootSfxSynth.BuildAll();
        foreach (RebootSfxSynth.Sound sound in sounds)
        {
            string path = Folder + "/" + sound.Name + ".wav";
            byte[] bytes = RebootSfxSynth.ToWav(sound.Samples);
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes)) File.WriteAllBytes(path, bytes);
        }
        AssetDatabase.Refresh();

        var clipsByCue = new Dictionary<SfxCue, List<string>>();
        foreach (SfxCue cue in Enum.GetValues(typeof(SfxCue))) clipsByCue[cue] = new List<string>();
        foreach (KeyValuePair<SfxCue, string[]> entry in Recorded) clipsByCue[entry.Key].AddRange(entry.Value);
        foreach (RebootSfxSynth.Sound sound in sounds)
            clipsByCue[(SfxCue)Enum.Parse(typeof(SfxCue), sound.Cue)].Add(Folder + "/" + sound.Name + ".wav");
        foreach (List<string> paths in clipsByCue.Values)
            foreach (string path in paths)
                ConfigureImporter(path);

        SfxSettings settings = AssetDatabase.LoadAssetAtPath<SfxSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<SfxSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }
        var cues = new List<SfxCueSettings>(settings.cues ?? Array.Empty<SfxCueSettings>());
        cues.RemoveAll(cue => cue == null);
        foreach (SfxCue cue in Enum.GetValues(typeof(SfxCue)))
        {
            SfxCueSettings entry = cues.Find(existing => existing.cue == cue);
            if (entry == null)
            {
                entry = Defaults(cue);
                cues.Add(entry);
            }
            if (entry.clips == null || entry.clips.Length == 0 || entry.clips.All(clip => clip == null))
                entry.clips = clipsByCue[cue].Select(path => AssetDatabase.LoadAssetAtPath<AudioClip>(path))
                    .Where(clip => clip != null).ToArray();
            if (entry.clips.Length == 0) Debug.LogWarning("No clip found for sound cue " + cue);
        }
        settings.cues = cues.OrderBy(cue => (int)cue.cue).ToArray();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return settings;
    }

    // Short sounds load decompressed (no decoding while playing) and are stored as ADPCM in builds.
    private static void ConfigureImporter(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null)
        {
            Debug.LogWarning("Missing sound " + path);
            return;
        }
        AudioImporterSampleSettings sample = importer.defaultSampleSettings;
        if (importer.forceToMono && sample.loadType == AudioClipLoadType.DecompressOnLoad &&
            sample.compressionFormat == AudioCompressionFormat.ADPCM && sample.preloadAudioData)
            return;
        importer.forceToMono = true;
        sample.loadType = AudioClipLoadType.DecompressOnLoad;
        sample.compressionFormat = AudioCompressionFormat.ADPCM;
        sample.preloadAudioData = true;
        importer.defaultSampleSettings = sample;
        importer.SaveAndReimport();
    }

    // Provisional mix. The blow itself (thud, armour, breaking robot) is loudest, layered under it a deep thump for
    // heavy blows; interface sounds stay under the combat. Cues that come in crowds get a voice limit and a short gap so
    // a group kill does not become one blast. Pitch below 1 makes a sound deeper and duller.
    private static SfxCueSettings Defaults(SfxCue cue)
    {
        switch (cue)
        {
            case SfxCue.Swing: return Entry(cue, 0.6f, 0.92f, 0.06f, 2, 0.05f, 0f);
            case SfxCue.SwingHeavy: return Entry(cue, 0.75f, 0.82f, 0.03f, 1, 0.1f, 0f);
            case SfxCue.Hit: return Entry(cue, 0.85f, 0.9f, 0.05f, 4, 0.03f, 0.3f);
            case SfxCue.HitMetal: return Entry(cue, 0.4f, 0.85f, 0.06f, 3, 0.04f, 0.3f);
            case SfxCue.HitSub: return Entry(cue, 0.75f, 1f, 0.03f, 2, 0.08f, 0f);
            case SfxCue.EnemyDown: return Entry(cue, 0.45f, 0.95f, 0.06f, 3, 0.06f, 0.35f);
            case SfxCue.PlayerHurt: return Entry(cue, 0.9f, 0.85f, 0.03f, 1, 0.2f, 0f);
            case SfxCue.PlayerGlitch: return Entry(cue, 0.3f, 1f, 0.03f, 1, 0.2f, 0f);
            case SfxCue.Dodge: return Entry(cue, 0.45f, 0.9f, 0.05f, 1, 0.1f, 0f);
            case SfxCue.SkillDash: return Entry(cue, 0.7f, 0.8f, 0.03f, 1, 0.15f, 0f);
            case SfxCue.Special: return Entry(cue, 1f, 0.95f, 0f, 1, 0.3f, 0f);
            case SfxCue.GaugeFull: return Entry(cue, 0.55f, 1f, 0f, 1, 0.5f, 0f);
            case SfxCue.CountdownTick: return Entry(cue, 0.6f, 1f, 0f, 2, 0.1f, 0f);
            case SfxCue.CountdownGo: return Entry(cue, 0.7f, 1f, 0f, 1, 0.2f, 0f);
            case SfxCue.TimeUp: return Entry(cue, 0.85f, 0.9f, 0f, 1, 0.5f, 0f);
            case SfxCue.Result: return Entry(cue, 0.65f, 1f, 0f, 1, 0.5f, 0f);
            case SfxCue.UiClick: return Entry(cue, 0.5f, 1f, 0.03f, 2, 0.03f, 0f);
            case SfxCue.UiConfirm: return Entry(cue, 0.55f, 1f, 0f, 2, 0.05f, 0f);
            case SfxCue.UiBack: return Entry(cue, 0.5f, 1f, 0f, 2, 0.05f, 0f);
            default: return Entry(cue, 0.35f, 1f, 0.04f, 2, 0.03f, 0f);
        }
    }

    private static SfxCueSettings Entry(SfxCue cue, float volume, float pitch, float pitchJitter, int maxVoices,
        float minInterval, float spatialBlend)
    {
        return new SfxCueSettings
        {
            cue = cue,
            volume = volume,
            pitch = pitch,
            pitchJitter = pitchJitter,
            maxVoices = maxVoices,
            minInterval = minInterval,
            spatialBlend = spatialBlend
        };
    }
}
