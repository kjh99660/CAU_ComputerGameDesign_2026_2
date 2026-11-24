using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// Sound-effect rules that need no audio: voice limits, the gap between repeats, Pause holding a voice's remaining
// time, clip choice, and the generated clips wired to every cue.
public sealed class SfxTests
{
    private const int Swing = (int)SfxCue.Swing;
    private const int Hit = (int)SfxCue.Hit;
    private const int Click = (int)SfxCue.UiClick;

    [Test]
    public void CueCountMatchesTheEnumAndOnlyRoundSoundsAreGameplay()
    {
        Assert.AreEqual(Enum.GetValues(typeof(SfxCue)).Length, SfxCues.Count);
        var gameplay = new HashSet<SfxCue>
        {
            SfxCue.Swing, SfxCue.SwingHeavy, SfxCue.Hit, SfxCue.HitMetal, SfxCue.HitSub, SfxCue.EnemyDown,
            SfxCue.PlayerHurt, SfxCue.PlayerGlitch, SfxCue.Dodge, SfxCue.SkillDash, SfxCue.Special, SfxCue.GaugeFull
        };
        foreach (SfxCue cue in Enum.GetValues(typeof(SfxCue)))
            Assert.AreEqual(gameplay.Contains(cue), SfxCues.IsGameplay(cue), cue.ToString());
    }

    [Test]
    public void ACueWaitsForItsMinimumIntervalBeforeStartingAgain()
    {
        var table = new SfxVoiceTable(4, SfxCues.Count);
        Assert.AreEqual(0, table.Claim(Hit, true, 1.0, 0.3, 4, 0.05));
        Assert.AreEqual(-1, table.Claim(Hit, true, 1.02, 0.3, 4, 0.05), "Too soon after the last one.");
        Assert.AreEqual(1, table.Claim(Hit, true, 1.06, 0.3, 4, 0.05));
        Assert.AreEqual(2, table.ActiveCount(Hit, 1.1));
        Assert.AreEqual(1, table.ActiveCount(Hit, 1.31), "The first one has ended.");
        Assert.AreEqual(0, table.ActiveCount(Hit, 1.37));
    }

    [Test]
    public void AtItsLimitACueStartsItsOldestVoiceOver()
    {
        var table = new SfxVoiceTable(6, SfxCues.Count);
        int first = table.Claim(Swing, true, 0.0, 1.0, 2, 0.0);
        int second = table.Claim(Swing, true, 0.1, 1.0, 2, 0.0);
        int third = table.Claim(Swing, true, 0.2, 1.0, 2, 0.0);
        Assert.AreNotEqual(first, second);
        Assert.AreEqual(first, third);
        Assert.AreEqual(2, table.ActiveCount(Swing, 0.25));
    }

    [Test]
    public void WhenEveryVoiceIsBusyAPlayingVoiceIsTakenBeforeAPausedOne()
    {
        var table = new SfxVoiceTable(2, SfxCues.Count);
        int swing = table.Claim(Swing, true, 0.0, 5.0, 2, 0.0);
        int hit = table.Claim(Hit, true, 0.1, 5.0, 2, 0.0);
        table.Pause(swing, 0.2);
        int click = table.Claim(Click, false, 0.3, 0.1, 2, 0.0);
        Assert.AreEqual(hit, click, "The paused swing keeps its voice; the playing hit gives way.");
        Assert.IsTrue(table.IsPaused(swing));
        Assert.AreEqual((int)SfxCue.UiClick, table.CueOf(click));
        Assert.IsFalse(table.IsGameplay(click));
    }

    [Test]
    public void APausedVoiceKeepsTheTimeItHadLeft()
    {
        var table = new SfxVoiceTable(2, SfxCues.Count);
        int voice = table.Claim(Hit, true, 10.0, 0.4, 2, 0.0);
        table.Pause(voice, 10.1);
        Assert.AreEqual(1, table.PausedCount);
        Assert.IsTrue(table.IsBusy(voice, 50.0), "Held for as long as the pause lasts.");
        table.Resume(voice, 50.0);
        Assert.AreEqual(0, table.PausedCount);
        Assert.AreEqual(1, table.ActiveCount(Hit, 50.29));
        Assert.AreEqual(0, table.ActiveCount(Hit, 50.31), "0.3 s were left when it was paused.");
    }

    [Test]
    public void ReleaseFreesAVoiceAtOnceEvenWhilePaused()
    {
        var table = new SfxVoiceTable(1, SfxCues.Count);
        int voice = table.Claim(Swing, true, 0.0, 2.0, 1, 0.0);
        table.Pause(voice, 0.5);
        table.Release(voice);
        Assert.IsFalse(table.IsBusy(voice, 0.6));
        Assert.AreEqual(0, table.PausedCount);
        Assert.AreEqual(voice, table.Claim(Hit, true, 0.6, 1.0, 1, 0.0));
    }

    [Test]
    public void ClipChoiceNeverRepeatsWhenThereIsAChoice()
    {
        var random = new SfxRandom(7u);
        var seen = new HashSet<int>();
        int last = -1;
        for (int i = 0; i < 300; i++)
        {
            int pick = random.Pick(3, last);
            Assert.AreNotEqual(last, pick);
            Assert.That(pick, Is.InRange(0, 2));
            seen.Add(pick);
            last = pick;
        }
        Assert.AreEqual(3, seen.Count);
        Assert.AreEqual(0, random.Pick(1, 0), "A single clip is always used.");
        for (int i = 0; i < 100; i++)
            Assert.That(random.Value(), Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
    }

    [Test]
    public void EveryCueHasShortMonoClipsWithAKnownSource()
    {
        var settings = Resources.Load<SfxSettings>("SfxSettings");
        Assert.IsNotNull(settings, "Resources/SfxSettings");
        Assert.AreEqual(SfxCues.Count, settings.cues.Length);
        var used = new HashSet<string>();
        foreach (SfxCue cue in Enum.GetValues(typeof(SfxCue)))
        {
            SfxCueSettings entry = settings.Find(cue);
            Assert.IsNotNull(entry, cue.ToString());
            Assert.Greater(entry.clips.Length, 0, cue.ToString());
            Assert.Greater(entry.volume, 0f, cue.ToString());
            foreach (AudioClip clip in entry.clips)
            {
                Assert.IsNotNull(clip, cue + " has an empty clip slot.");
                string path = AssetDatabase.GetAssetPath(clip);
                // Generated in Assets/Sound/SFX, or a recorded sound in Assets/ThirdParty next to a README naming its source.
                bool generated = path.StartsWith("Assets/Sound/SFX/");
                bool recorded = path.StartsWith("Assets/ThirdParty/") &&
                                System.IO.File.Exists(System.IO.Path.GetDirectoryName(path) + "/README.md");
                Assert.IsTrue(generated || recorded, path + " has no known source.");
                Assert.AreEqual(1, clip.channels, path);
                Assert.That(clip.length, Is.InRange(0.015f, 2.5f), path);
                used.Add(path);
            }
        }
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Sound/SFX" }))
            Assert.IsTrue(used.Contains(AssetDatabase.GUIDToAssetPath(guid)), "Unused clip " + AssetDatabase.GUIDToAssetPath(guid));
    }
}
