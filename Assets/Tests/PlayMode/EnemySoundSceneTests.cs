using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// The enemies' attack sounds and the ambience bed in the real scene: a wind-up sounds where the enemy starts its attack
// and a swish where it strikes, never beyond the cues' voice limits and minimum intervals even when the crowd attacks
// at once; the ambience hums under the music through the battle, holds in Pause and starts over after a Retry.
public sealed class EnemySoundSceneTests
{
    private const int Fps = 60;
    private GameStateManager _state;
    private EventManager _events;
    private EnemyRuntime _enemies;
    private SfxRuntime _sfx;
    private PlayerRuntime _player;
    private int _oldCaptureFramerate;
    private readonly List<Object> _made = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        _oldCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = Fps;
    }

    [TearDown]
    public void TearDown()
    {
        Time.captureFramerate = _oldCaptureFramerate;
        Time.timeScale = 1f;
        foreach (Object made in _made)
            if (made != null) Object.Destroy(made);
        _made.Clear();
    }

    [UnityTest]
    public IEnumerator WindupsAndStrikesSoundWhereTheEnemyStandsWithinTheirLimits()
    {
        yield return Load();
        // A crowd that attacks at once: without liveliness every enemy in reach attacks the moment it can, so the
        // attackers allowed at once (EnemySettings.MaximumAttackers) start together in one frame.
        EnemySettings settings = Object.Instantiate(_enemies.Settings);
        _made.Add(settings);
        SetField(settings, "liveliness", 0f);
        SetField(_enemies, "settings", settings);
        Assert.Greater(settings.MaximumAttackers, 1);
        yield return EnterBattle();
        SfxCueSettings windup = _sfx.Settings.Find(SfxCue.EnemyWindup);
        SfxCueSettings strike = _sfx.Settings.Find(SfxCue.EnemyStrike);
        var windups = new Sounded(_sfx, SfxCue.EnemyWindup);
        var strikes = new Sounded(_sfx, SfxCue.EnemyStrike);
        _enemies.AttackWindupStarted += windups.OnEvent;
        _enemies.AttackSwung += strikes.OnEvent;
        try
        {
            // The first enemies stand around the player, in reach.
            Vector3 center = _player.transform.position;
            IReadOnlyList<EnemyAgent> active = _enemies.Perf.ActiveEnemies;
            for (int i = 0; i < active.Count; i++)
            {
                float angle = i * Mathf.PI * 2f / active.Count;
                active[i].transform.position = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.3f;
            }
            for (int frame = 0; frame < 25 * Fps && (windups.Sounds < 4 || strikes.Sounds < 4); frame++)
            {
                yield return null;
                Assert.LessOrEqual(_sfx.ActiveVoices(SfxCue.EnemyWindup), windup.maxVoices, "Wind-up voices over the limit.");
                Assert.LessOrEqual(_sfx.ActiveVoices(SfxCue.EnemyStrike), strike.maxVoices, "Strike voices over the limit.");
            }
        }
        finally
        {
            _enemies.AttackWindupStarted -= windups.OnEvent;
            _enemies.AttackSwung -= strikes.OnEvent;
        }

        Assert.GreaterOrEqual(windups.Sounds, 4, "Attacking enemies wind up audibly.");
        Assert.GreaterOrEqual(strikes.Sounds, 4, "Their strikes swish.");
        Assert.LessOrEqual(windups.Sounds, windups.Events);
        Assert.LessOrEqual(strikes.Sounds, strikes.Events);
        Assert.AreEqual(0, windups.Misplaced, "A wind-up sounds where its enemy stands.");
        Assert.AreEqual(0, strikes.Misplaced, "A strike sounds where its enemy stands.");
        Assert.GreaterOrEqual(windups.ShortestGap, windup.minInterval - 0.0001, "Wind-ups started closer together than their minimum interval.");
        Assert.GreaterOrEqual(strikes.ShortestGap, strike.minInterval - 0.0001, "Strikes started closer together than their minimum interval.");
        Assert.Greater(windups.SameFrameEvents, 0, "Several enemies started their attacks in the same frame...");
        Assert.LessOrEqual(windups.MostSoundsInAFrame, 1, "...and were heard as one wind-up, not a burst.");
        Assert.LessOrEqual(strikes.MostSoundsInAFrame, 1);
    }

    [UnityTest]
    public IEnumerator TheAmbienceLoopsInBattleHoldsInPauseAndStartsOverOnRetry()
    {
        yield return Load();
        SfxSettings settings = _sfx.Settings;
        SfxAmbience bed = _sfx.Ambience;
        Assert.IsNotNull(bed, "The game has an ambience bed.");
        AudioSource source = bed.Source;
        Assert.AreSame(settings.ambienceClip, source.clip);
        Assert.IsTrue(source.loop);
        Assert.AreEqual(0f, source.spatialBlend, "Everywhere, not at one place.");
        float battle = settings.masterVolume * settings.ambienceVolume;
        float around = battle * settings.ambienceOutsideBattle;
        int fadeFrames = Mathf.CeilToInt(settings.ambienceFadeSeconds * Fps) + 2;

        // Ready already hums, quieter, after fading in from silence.
        Assert.IsTrue(bed.IsRunning);
        Assert.AreEqual(around, bed.TargetVolume, 0.00001f);
        yield return Frames(fadeFrames);
        Assert.AreEqual(around, source.volume, 0.00001f);
        int starts = bed.Starts;

        yield return EnterBattle();
        Assert.AreEqual(battle, bed.TargetVolume, 0.00001f);
        yield return Frames(fadeFrames);
        Assert.AreEqual(battle, source.volume, 0.00001f, "Up to the battle level.");
        Assert.AreEqual(starts, bed.Starts, "Into the battle it carries on; it does not start over.");

        // It loops: let real time pass beyond the clip's length (the game at real speed meanwhile) and watch the play
        // position come round.
        Time.captureFramerate = 0;
        int wraps = 0, last = source.timeSamples;
        float until = Time.realtimeSinceStartup + source.clip.length + 0.6f;
        while (Time.realtimeSinceStartup < until)
        {
            yield return null;
            if (source.timeSamples < last) wraps++;
            last = source.timeSamples;
        }
        Time.captureFramerate = Fps;
        Assert.AreEqual(GameState.Battle, _state.CurrentState);
        Assert.IsTrue(source.isPlaying);
        Assert.GreaterOrEqual(wraps, 1, "The loop came round and kept playing.");

        // Pause holds it where it is.
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        Assert.IsTrue(bed.IsHeld);
        Assert.IsFalse(source.isPlaying);
        int held = source.timeSamples;
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.AreEqual(held, source.timeSamples, "Held in place while paused.");
        Assert.AreEqual(battle, source.volume, 0.00001f);
        _events.EnqueueRequest(new ResumeRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
        Assert.IsFalse(bed.IsHeld);
        Assert.IsTrue(source.isPlaying);
        yield return new WaitForSecondsRealtime(0.2f);
        int moved = (source.timeSamples - held + source.clip.samples) % source.clip.samples;
        Assert.That(moved, Is.InRange(1, source.clip.frequency), "It goes on from where it was held.");

        // Retry: the new round's bed starts again from the beginning of the loop, from silence.
        int restarts = bed.Restarts;
        starts = bed.Starts;
        _events.EnqueueRequest(new PauseRequest(_state.Tick));
        yield return WaitFor(GameState.Paused);
        _events.EnqueueRequest(new RestartRequest(_state.Tick));
        yield return WaitFor(GameState.Ready);
        Assert.AreEqual(restarts + 1, bed.Restarts);
        Assert.AreEqual(starts + 1, bed.Starts);
        Assert.IsTrue(bed.IsRunning);
        Assert.IsFalse(bed.IsHeld);
        Assert.IsTrue(source.isPlaying);
        Assert.Less(bed.Volume, around * 0.5f, "It fades in again from silence.");
        Assert.Less(source.time, 0.25f, "...from the beginning of the loop.");
        yield return Frames(fadeFrames);
        Assert.AreEqual(around, source.volume, 0.00001f);

        // Well under the music: the scene's music source plays its clip at its own volume.
        AudioSource music = null;
        foreach (AudioSource candidate in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            if (candidate.loop && candidate.playOnAwake && candidate.clip != null && candidate != source) music = candidate;
        Assert.IsNotNull(music, "The scene plays its music.");
        float musicLevel = Rms(music.clip) * music.volume;
        float bedLevel = Rms(source.clip) * battle;
        float below = 20f * Mathf.Log10(musicLevel / bedLevel);
        Assert.That(below, Is.InRange(12f, 26f), $"In battle the ambience is {below:0.0} dB below the music ({music.clip.name}).");
    }

    // Counts an enemy event and whether the sound runtime (subscribed before the test) played its cue for it, where.
    private sealed class Sounded
    {
        private readonly SfxRuntime _sfx;
        private readonly SfxCue _cue;
        private int _plays;
        private double _lastStart = double.NegativeInfinity;
        private int _lastFrame = -1;
        private int _soundsThisFrame;

        public int Events, Sounds, Misplaced, SameFrameEvents, MostSoundsInAFrame;
        public double ShortestGap = double.PositiveInfinity;

        public Sounded(SfxRuntime sfx, SfxCue cue)
        {
            _sfx = sfx;
            _cue = cue;
            _plays = sfx.PlayCount(cue);
        }

        public void OnEvent(Vector3 position, int tick)
        {
            Events++;
            if (Time.frameCount == _lastFrame) SameFrameEvents++;
            else _soundsThisFrame = 0;
            _lastFrame = Time.frameCount;
            int plays = _sfx.PlayCount(_cue);
            if (plays == _plays) return;
            _plays = plays;
            Sounds++;
            MostSoundsInAFrame = Mathf.Max(MostSoundsInAFrame, ++_soundsThisFrame);
            AudioSource source = _sfx.LastSource;
            if (Vector3.Distance(source.transform.position, position) > 0.01f || source.spatialBlend < 0.5f) Misplaced++;
            double now = Time.unscaledTimeAsDouble;
            ShortestGap = System.Math.Min(ShortestGap, now - _lastStart);
            _lastStart = now;
        }
    }

    // ---------------------------------------------------------------- helpers
    private static float Rms(AudioClip clip)
    {
        Assert.IsTrue(clip.LoadAudioData(), clip.name);
        var samples = new float[clip.samples * clip.channels];
        Assert.IsTrue(clip.GetData(samples, 0), clip.name + " could not be read.");
        double sum = 0.0;
        foreach (float sample in samples) sum += sample * (double)sample;
        return (float)System.Math.Sqrt(sum / samples.Length);
    }

    private static void SetField(object instance, string field, object value)
    {
        System.Reflection.FieldInfo info = instance.GetType().GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(info, "Field not found: " + field);
        info.SetValue(instance, value);
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++) yield return null;
    }

    private IEnumerator Load()
    {
        yield return TestScenes.Load(TestScenes.Arena);
        yield return null;
        _state = Object.FindFirstObjectByType<GameStateManager>();
        _events = Object.FindFirstObjectByType<EventManager>();
        _enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        _sfx = Object.FindFirstObjectByType<SfxRuntime>();
        _player = Object.FindFirstObjectByType<PlayerRuntime>();
        Assert.IsNotNull(_sfx);
        yield return WaitFor(GameState.Ready);
    }

    private IEnumerator EnterBattle()
    {
        _events.EnqueueRequest(new StartRequest(_state.Tick));
        yield return WaitFor(GameState.IntroDialogue);
        _events.EnqueueRequest(new DialogueCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Countdown);
        _events.EnqueueRequest(new CountdownCompleteRequest(_state.Tick));
        yield return WaitFor(GameState.Battle);
    }

    private IEnumerator WaitFor(GameState expected)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (_state.CurrentState != expected && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(expected, _state.CurrentState);
    }
}
