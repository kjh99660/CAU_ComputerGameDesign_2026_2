using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// The enemies' attack sounds and the ambience bed, checked on the real clips and mix: enemy sounds stay quieter and
// higher than a landed blow even when three attackers line up, the ambience loops without a click and follows the
// game states.
public sealed class EnemySoundTests
{
    [Test]
    public void ThreeAttackersAtOnceStayBelowOneLandedBlowAndSitHigherThanIt()
    {
        var settings = Resources.Load<SfxSettings>("SfxSettings");
        SfxCueSettings windup = settings.Find(SfxCue.EnemyWindup);
        SfxCueSettings strike = settings.Find(SfxCue.EnemyStrike);
        SfxCueSettings hit = settings.Find(SfxCue.Hit);
        var enemies = AssetDatabase.LoadAssetAtPath<EnemySettings>("Assets/Prefabs/Enemy/EnemySettings.asset");
        Assert.Greater(enemies.MaximumAttackers, 0);

        // Worst case: every voice the limits allow (no more than the attackers allowed at once) at its loudest clip,
        // all peaking together. A landed blow is at least its Hit layer at its quietest clip.
        int attackers = enemies.MaximumAttackers;
        float enemyPeak = settings.masterVolume * (
            Mathf.Min(attackers, windup.maxVoices) * windup.volume * LoudestPeak(windup) +
            Mathf.Min(attackers, strike.maxVoices) * strike.volume * LoudestPeak(strike));
        float blowPeak = settings.masterVolume * hit.volume * QuietestPeak(hit);
        Assert.Less(enemyPeak, blowPeak, $"Lined-up enemy attack sounds peak at {Db(enemyPeak):0.0} dBFS, a blow at {Db(blowPeak):0.0} dBFS.");
        Assert.Greater(windup.minInterval, 0.05f, "A crowd must not machine-gun the wind-ups.");
        Assert.Greater(strike.minInterval, 0.05f);
        Assert.Greater(windup.spatialBlend, 0.5f, "Heard where the enemy stands.");
        Assert.Greater(strike.spatialBlend, 0.5f);

        // Higher: zero crossings per second (a rough pitch measure) at the pitch each cue plays at.
        float blowRate = float.MaxValue;
        foreach (AudioClip clip in hit.clips) blowRate = Mathf.Min(blowRate, CrossingsPerSecond(clip) * hit.pitch);
        foreach (AudioClip clip in windup.clips)
            Assert.Greater(CrossingsPerSecond(clip) * windup.pitch, blowRate * 5f, clip.name + " must sit well above the blow.");
        foreach (AudioClip clip in strike.clips)
            Assert.Greater(CrossingsPerSecond(clip) * strike.pitch, blowRate * 5f, clip.name + " must sit well above the blow.");
        foreach (AudioClip clip in windup.clips)
            Assert.LessOrEqual(clip.length / windup.pitch, enemies.AttackWindup, clip.name + " must end before the strike.");
    }

    [Test]
    public void TheAmbienceIsAnUncompressedLoopWithoutAClick()
    {
        var settings = Resources.Load<SfxSettings>("SfxSettings");
        AudioClip clip = settings.ambienceClip;
        Assert.IsNotNull(clip, "SfxSettings has an ambience clip.");
        string path = AssetDatabase.GetAssetPath(clip);
        StringAssert.StartsWith("Assets/Sound/Ambience/", path);
        Assert.IsTrue(System.IO.File.Exists("Assets/Sound/Ambience/README.md"), "Its source is written down.");
        var importer = (AudioImporter)AssetImporter.GetAtPath(path);
        Assert.AreEqual(AudioCompressionFormat.PCM, importer.defaultSampleSettings.compressionFormat, "Sample exact loop point.");
        Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, importer.defaultSampleSettings.loadType);
        Assert.AreEqual(1, clip.channels);
        Assert.Greater(clip.length, 3f, "Long enough not to drone in a short cycle.");

        float[] samples = Samples(clip);
        int n = samples.Length;
        var steps = new float[n - 1];
        var bends = new float[n - 2];
        for (int i = 0; i < n - 1; i++) steps[i] = Mathf.Abs(samples[i + 1] - samples[i]);
        for (int i = 0; i < n - 2; i++) bends[i] = Mathf.Abs(samples[i + 2] - 2f * samples[i + 1] + samples[i]);
        float seamStep = Mathf.Abs(samples[0] - samples[n - 1]);
        float seamBend = Mathf.Max(Mathf.Abs(samples[0] - 2f * samples[n - 1] + samples[n - 2]),
            Mathf.Abs(samples[1] - 2f * samples[0] + samples[n - 1]));
        Array.Sort(steps);
        Array.Sort(bends);
        // A click is a step far outside the clip's own: the loop point must look like an ordinary moment of it.
        Assert.LessOrEqual(seamStep, steps[(int)(steps.Length * 0.99f)], "The loop point jumps.");
        Assert.LessOrEqual(seamBend, bends[(int)(bends.Length * 0.99f)], "The loop point bends sharply.");

        // No swell or dip where the end was blended into the start: the last and first half seconds are as loud as the rest.
        int half = clip.frequency / 2;
        float whole = Rms(samples, 0, n), first = Rms(samples, 0, half), last = Rms(samples, n - half, n);
        Assert.AreEqual(0f, Db(first) - Db(whole), 3f);
        Assert.AreEqual(0f, Db(last) - Db(whole), 3f);
    }

    [Test]
    public void TheAmbienceFollowsTheRoundAndHoldsInPause()
    {
        const float battle = 0.08f, share = 0.5f;
        Assert.AreEqual(battle, SfxAmbienceRules.TargetVolume(GameState.Battle, battle, share));
        Assert.AreEqual(battle, SfxAmbienceRules.TargetVolume(GameState.Finishing, battle, share));
        foreach (GameState around in new[] { GameState.Ready, GameState.IntroDialogue, GameState.Countdown, GameState.OutroDialogue, GameState.Result })
            Assert.AreEqual(battle * share, SfxAmbienceRules.TargetVolume(around, battle, share), around.ToString());
        Assert.AreEqual(0f, SfxAmbienceRules.TargetVolume(GameState.Initializing, battle, share));
        Assert.AreEqual(0f, SfxAmbienceRules.TargetVolume(GameState.Ready, battle, 0f), "A share of 0 is silent around the battle.");
        foreach (GameState state in (GameState[])Enum.GetValues(typeof(GameState)))
            Assert.AreEqual(state == GameState.Paused, SfxAmbienceRules.Holds(state), state.ToString());

        // Silence to the battle volume takes the fade time, at a steady rate, either way.
        float volume = 0f;
        for (int frame = 0; frame < 30; frame++) volume = SfxAmbienceRules.Step(volume, battle, battle, 1f, 1f / 60f);
        Assert.AreEqual(battle * 0.5f, volume, 0.0001f);
        for (int frame = 0; frame < 30; frame++) volume = SfxAmbienceRules.Step(volume, battle, battle, 1f, 1f / 60f);
        Assert.AreEqual(battle, volume, 0.0001f);
        Assert.AreEqual(battle, SfxAmbienceRules.Step(volume, battle, battle, 1f, 1f), "It stops at its target.");
        Assert.AreEqual(battle * 0.75f, SfxAmbienceRules.Step(battle, 0f, battle, 1f, 0.25f), 0.0001f);
    }

    private static float LoudestPeak(SfxCueSettings cue)
    {
        float peak = 0f;
        foreach (AudioClip clip in cue.clips) peak = Mathf.Max(peak, Peak(Samples(clip)));
        return peak;
    }

    private static float QuietestPeak(SfxCueSettings cue)
    {
        float peak = float.MaxValue;
        foreach (AudioClip clip in cue.clips) peak = Mathf.Min(peak, Peak(Samples(clip)));
        return peak;
    }

    private static float[] Samples(AudioClip clip)
    {
        Assert.IsTrue(clip.LoadAudioData(), clip.name);
        var samples = new float[clip.samples * clip.channels];
        Assert.IsTrue(clip.GetData(samples, 0), clip.name + " could not be read.");
        return samples;
    }

    private static float Peak(float[] samples)
    {
        float peak = 0f;
        foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
        return peak;
    }

    private static float Rms(float[] samples, int from, int to)
    {
        double sum = 0.0;
        for (int i = from; i < to; i++) sum += samples[i] * (double)samples[i];
        return (float)Math.Sqrt(sum / Math.Max(1, to - from));
    }

    private static float CrossingsPerSecond(AudioClip clip)
    {
        float[] samples = Samples(clip);
        float peak = Peak(samples);
        int crossings = 0, loud = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if (Mathf.Abs(samples[i]) < peak * 0.05f && Mathf.Abs(samples[i - 1]) < peak * 0.05f) continue;   // skip silence
            loud++;
            if ((samples[i] >= 0f) != (samples[i - 1] >= 0f)) crossings++;
        }
        return crossings / Mathf.Max(1f / clip.frequency, loud / (float)clip.frequency);
    }

    private static float Db(float value) => 20f * Mathf.Log10(Mathf.Max(1e-9f, value));
}
