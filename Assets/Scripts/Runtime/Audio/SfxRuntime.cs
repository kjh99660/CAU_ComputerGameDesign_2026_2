using System;
using System.Collections;
using UnityEngine;

// Plays the game's sound effects. It listens to the same events as the rest of the combat feedback (swings, hits,
// the special, accepted player hits, the gauge, the enemies' wind-ups and strikes), to the buttons of the UI views and
// to state changes.
// Gameplay sounds belong to the round: they start only in Battle and Finishing, Pause holds them (their AudioSources
// are paused; the time scale is not involved) and Retry or a dialogue cuts them. Interface sounds play in every
// state. The voices are a fixed pool of AudioSources made once at start-up. Under it all runs the ambience bed
// (SfxAmbience), which follows the same states with its own AudioSource.
[DisallowMultipleComponent]
public sealed class SfxRuntime : MonoBehaviour, IRoundResettable
{
    // Player animator states that have their own sound. They are watched, so the player scripts stay untouched.
    private static readonly int DodgeForward = Animator.StringToHash("Dodge_F");
    private static readonly int DodgeBackward = Animator.StringToHash("Dodge_B");
    private static readonly int SkillDashState = Animator.StringToHash("Skill1_Dash");
    private static readonly int SkillMainState = Animator.StringToHash("Skill1_Main");

    private readonly SfxCueSettings[] _cues = new SfxCueSettings[SfxCues.Count];
    private readonly int[] _lastClip = new int[SfxCues.Count];
    private readonly int[] _plays = new int[SfxCues.Count];
    private SfxSettings _settings;
    private AudioSource[] _sources = Array.Empty<AudioSource>();
    private SfxVoiceTable _voices = new SfxVoiceTable(0, SfxCues.Count);
    private SfxRandom _random = new SfxRandom(0x2545F491u);
    private EnemyRuntime _enemies;
    private CombatVfxRuntime _vfx;
    private CombatRoundRuntime _combat;
    private RoundCombatStats _stats;
    private SpecialHitWindow _special;
    private UIRoot _ui;
    private Animator _player;
    private SfxAmbience _ambience;
    private int _tick;
    private GameState _state = GameState.Initializing;
    private bool _gameplay;
    private int _hitCount;
    private bool _gaugeFull;
    private int _playerState;

    public SfxSettings Settings => _settings;
    public int VoiceCount => _sources.Length;
    public int PausedVoices => _voices.PausedCount;
    // The ambience bed; null without an ambience clip.
    public SfxAmbience Ambience => _ambience;

    // The voice that started most recently, for tests that check its position and pitch.
    public AudioSource LastSource { get; private set; }

    // How many times a cue has started since the scene loaded. Read by tests and tuning tools.
    public int PlayCount(SfxCue cue) => _plays[(int)cue];

    public int ActiveVoices(SfxCue cue) => _voices.ActiveCount((int)cue, Now);

    private static double Now => Time.unscaledTimeAsDouble;

    public void Configure(SfxSettings settings, EnemyRuntime enemies, CombatVfxRuntime vfx, CombatRoundRuntime combat,
        UIRoot ui, Animator player)
    {
        _settings = settings;
        for (int i = 0; i < _lastClip.Length; i++) _lastClip[i] = -1;
        if (settings != null && settings.cues != null)
            foreach (SfxCueSettings cue in settings.cues)
                if (cue != null && (int)cue.cue >= 0 && (int)cue.cue < _cues.Length && _cues[(int)cue.cue] == null)
                    _cues[(int)cue.cue] = cue;
        CreateVoices(settings != null ? Mathf.Clamp(settings.voiceCount, 4, 24) : 0);
        if (settings != null && settings.ambienceClip != null)
        {
            var bed = new GameObject("Sfx Ambience");
            bed.transform.SetParent(transform, false);
            _ambience = bed.AddComponent<SfxAmbience>();
            _ambience.Configure(settings.ambienceClip, settings.masterVolume * settings.ambienceVolume,
                settings.ambienceOutsideBattle, settings.ambienceFadeSeconds);
        }
        _enemies = enemies;
        _vfx = vfx;
        _combat = combat;
        _stats = combat != null ? combat.Stats : null;
        _special = new SpecialHitWindow(_stats);
        _ui = ui;
        _player = player;
        Subscribe(true);
        enabled = false;
    }

    public void ApplyState(GameStateChangedEvent notification)
    {
        if (notification.Tick < _tick) return;
        _tick = notification.Tick;
        GameState previous = _state;
        _state = notification.Current;
        _gameplay = _state == GameState.Battle || _state == GameState.Finishing;
        if (_state == GameState.Paused) PauseGameplay();
        else if (_gameplay) ResumeGameplay();
        else StopGameplay();
        _ambience?.ApplyState(_state);
        if (_state == GameState.Finishing && previous == GameState.Battle) Play(SfxCue.TimeUp, default);
        if (_state == GameState.Result && previous != GameState.Result) Play(SfxCue.Result, default);
        // Dodges and dashes are noticed by watching the player's animator, only while the round runs.
        enabled = _gameplay && _player != null;
        if (enabled) _playerState = PlayerState();
    }

    public void StopProducing() => StopGameplay();

    public IEnumerator ResetRound(int tick)
    {
        _tick = tick;
        StopGameplay();
        // The new round's bed starts again from its beginning when the next state arrives.
        if (_ambience != null) _ambience.Restart();
        _hitCount = _stats != null ? _stats.HitCount : 0;
        _gaugeFull = false;
        _playerState = 0;
        yield break;
    }

    private void LateUpdate()
    {
        if (!_gameplay || _player == null) return;
        int state = PlayerState();
        if (state == _playerState) return;
        _playerState = state;
        if (state == DodgeForward || state == DodgeBackward) Play(SfxCue.Dodge, default);
        else if (state == SkillDashState) Play(SfxCue.SkillDash, default);
        // The skill's blow at the end of its dash: the hitbox plays no slash effect for it, so no swing was heard.
        else if (state == SkillMainState) Play(SfxCue.SwingHeavy, default);
    }

    private int PlayerState()
    {
        if (_player == null || !_player.isActiveAndEnabled || !_player.isInitialized) return 0;
        AnimatorStateInfo state = _player.IsInTransition(0) ? _player.GetNextAnimatorStateInfo(0) : _player.GetCurrentAnimatorStateInfo(0);
        return state.shortNameHash;
    }

    private bool Accepts(int tick) => _gameplay && tick == _tick;

    private void OnSwing(int tick)
    {
        if (!Accepts(tick)) return;
        float scale = _vfx != null ? _vfx.LastSlashScale : 1f;
        bool heavy = _settings != null && scale >= _settings.heavySwingScale;
        Play(heavy ? SfxCue.SwingHeavy : SfxCue.Swing, default);
    }

    // A landed blow is layered: the dull thud, the armour, the robot breaking, and a deep thump under heavy blows.
    private void OnHit(Vector3 position, Vector3 direction, int tick)
    {
        if (!Accepts(tick)) return;
        Play(SfxCue.Hit, position);
        Play(SfxCue.HitMetal, position);
        if (!_special.IsSpecialHit && CombatHitWeights.FromPlayer(_player) == CombatHitWeight.Heavy)
            Play(SfxCue.HitSub, position);
    }

    // The enemy going down is heard only for the hit that destroys it (a tough enemy survives its first hits).
    private void OnStruck(EnemyAgent enemy, Vector3 direction, int tick)
    {
        if (enemy != null && enemy.Defeated && Accepts(tick))
            Play(SfxCue.EnemyDown, enemy.transform.position);
    }

    private void OnSpecial(Vector3 center, float radius, int tick)
    {
        if (!Accepts(tick)) return;
        Play(SfxCue.Special, center);
        Play(SfxCue.HitSub, center);
    }

    // An enemy's attack, heard where the enemy stands. A crowd attacking at once is kept in check by the cues' voice
    // limit and minimum interval (EnemySettings.MaximumAttackers already limits how many attack at once).
    private void OnEnemyWindup(Vector3 position, int tick)
    {
        if (Accepts(tick)) Play(SfxCue.EnemyWindup, position);
    }

    private void OnEnemyStrike(Vector3 position, int tick)
    {
        if (Accepts(tick)) Play(SfxCue.EnemyStrike, position);
    }

    // The round's numbers changed: a player hit the round accepted, or the gauge has just become full.
    private void OnStatsChanged()
    {
        if (_stats == null) return;
        bool hurt = _stats.HitCount > _hitCount;
        _hitCount = _stats.HitCount;
        bool full = _combat != null && _stats.Gauge >= Mathf.Max(0.01f, _combat.Settings.maximumGauge) - 0.0001f;
        bool filled = full && !_gaugeFull;
        _gaugeFull = full;
        if (!Accepts(_stats.Tick)) return;
        if (hurt)
        {
            Play(SfxCue.PlayerHurt, default);
            Play(SfxCue.PlayerGlitch, default);
        }
        if (filled) Play(SfxCue.GaugeFull, default);
    }

    private void Click() => Play(SfxCue.UiClick, default);
    // An option step in the pause menu. It plays after the value has changed, so a volume step is heard at the new volume.
    private void StepClick(int direction) => Play(SfxCue.UiClick, default);
    private void Confirm() => Play(SfxCue.UiConfirm, default);
    private void Back() => Play(SfxCue.UiBack, default);
    private void NextLine() => Play(SfxCue.DialogueNext, default);
    // The 3, 2, 1 before the battle, and the same tick on the battle's last three seconds.
    private void CountdownNumber(int number) => Play(SfxCue.CountdownTick, default);
    private void CountdownStart() => Play(SfxCue.CountdownGo, default);

    private void Play(SfxCue cue, Vector3 position)
    {
        SfxCueSettings settings = _cues[(int)cue];
        if (settings == null || settings.clips == null || settings.clips.Length == 0 || _sources.Length == 0) return;
        int pick = _random.Pick(settings.clips.Length, _lastClip[(int)cue]);
        AudioClip clip = settings.clips[pick];
        if (clip == null) return;
        bool gameplay = SfxCues.IsGameplay(cue);
        float pitch = settings.pitch * (1f + (_random.Value() * 2f - 1f) * settings.pitchJitter);
        if (gameplay && _state == GameState.Finishing) pitch *= _settings.finishingPitch;
        pitch = Mathf.Max(0.1f, pitch);
        int voice = _voices.Claim((int)cue, gameplay, Now, clip.length / pitch, settings.maxVoices, settings.minInterval);
        if (voice < 0) return;
        _lastClip[(int)cue] = pick;
        AudioSource source = _sources[voice];
        source.Stop();
        source.clip = clip;
        source.pitch = pitch;
        source.volume = _settings.masterVolume * settings.volume;
        source.spatialBlend = settings.spatialBlend;
        source.priority = gameplay ? 128 : 64;
        if (settings.spatialBlend > 0f) source.transform.position = position;
        source.Play();
        _plays[(int)cue]++;
        LastSource = source;
    }

    private void PauseGameplay()
    {
        double now = Now;
        for (int v = 0; v < _sources.Length; v++)
        {
            if (!_voices.IsGameplay(v) || _voices.IsPaused(v) || !_voices.IsBusy(v, now)) continue;
            _voices.Pause(v, now);
            _sources[v].Pause();
        }
    }

    private void ResumeGameplay()
    {
        double now = Now;
        for (int v = 0; v < _sources.Length; v++)
        {
            if (!_voices.IsPaused(v)) continue;
            _voices.Resume(v, now);
            _sources[v].UnPause();
        }
    }

    private void StopGameplay()
    {
        double now = Now;
        for (int v = 0; v < _sources.Length; v++)
        {
            if (!_voices.IsGameplay(v) || !_voices.IsBusy(v, now)) continue;
            _voices.Release(v);
            _sources[v].Stop();
        }
    }

    private void CreateVoices(int count)
    {
        _sources = new AudioSource[count];
        _voices = new SfxVoiceTable(count, SfxCues.Count);
        for (int i = 0; i < count; i++)
        {
            var voice = new GameObject("Sfx Voice " + i);
            voice.transform.SetParent(transform, false);
            AudioSource source = voice.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = _settings.spatialMinDistance;
            source.maxDistance = Mathf.Max(_settings.spatialMinDistance + 1f, _settings.spatialMaxDistance);
            _sources[i] = source;
        }
    }

    private void Subscribe(bool on)
    {
        if (_enemies != null)
        {
            if (on)
            {
                _enemies.HitPresented += OnHit;
                _enemies.EnemyStruck += OnStruck;
                _enemies.AttackWindupStarted += OnEnemyWindup;
                _enemies.AttackSwung += OnEnemyStrike;
            }
            else
            {
                _enemies.HitPresented -= OnHit;
                _enemies.EnemyStruck -= OnStruck;
                _enemies.AttackWindupStarted -= OnEnemyWindup;
                _enemies.AttackSwung -= OnEnemyStrike;
            }
        }
        if (_vfx != null)
        {
            if (on) _vfx.SlashPlayed += OnSwing;
            else _vfx.SlashPlayed -= OnSwing;
        }
        if (_combat != null)
        {
            if (on) _combat.SpecialActivated += OnSpecial;
            else _combat.SpecialActivated -= OnSpecial;
        }
        if (_stats != null)
        {
            if (on) _stats.Changed += OnStatsChanged;
            else _stats.Changed -= OnStatsChanged;
        }
        if (_ui == null) return;
        StartView start = _ui.StartScreen;
        if (start != null)
        {
            if (on) start.StartClicked += Confirm;
            else start.StartClicked -= Confirm;
        }
        DialogueView dialogue = _ui.DialogueScreen;
        if (dialogue != null)
        {
            if (on)
            {
                dialogue.SkipClicked += Click;
                dialogue.ContinueClicked += NextLine;
            }
            else
            {
                dialogue.SkipClicked -= Click;
                dialogue.ContinueClicked -= NextLine;
            }
        }
        CountdownView countdown = _ui.CountdownScreen;
        if (countdown != null)
        {
            if (on)
            {
                countdown.NumberShown += CountdownNumber;
                countdown.StartShown += CountdownStart;
            }
            else
            {
                countdown.NumberShown -= CountdownNumber;
                countdown.StartShown -= CountdownStart;
            }
        }
        BattleView battle = _ui.BattleScreen;
        if (battle != null)
        {
            if (on)
            {
                battle.PauseClicked += Click;
                battle.FinalSecondTicked += CountdownNumber;
            }
            else
            {
                battle.PauseClicked -= Click;
                battle.FinalSecondTicked -= CountdownNumber;
            }
        }
        PauseView pause = _ui.PauseScreen;
        if (pause != null)
        {
            if (on)
            {
                pause.ResumeClicked += Confirm;
                pause.RetryClicked += Click;
                pause.QuitClicked += Back;
                pause.LookStepClicked += StepClick;
                pause.VolumeStepClicked += StepClick;
            }
            else
            {
                pause.ResumeClicked -= Confirm;
                pause.RetryClicked -= Click;
                pause.QuitClicked -= Back;
                pause.LookStepClicked -= StepClick;
                pause.VolumeStepClicked -= StepClick;
            }
        }
        RetryConfirmationView confirmation = _ui.RetryConfirmationScreen;
        if (confirmation != null)
        {
            if (on)
            {
                confirmation.ConfirmClicked += Confirm;
                confirmation.CancelClicked += Back;
            }
            else
            {
                confirmation.ConfirmClicked -= Confirm;
                confirmation.CancelClicked -= Back;
            }
        }
        ResultView result = _ui.ResultScreen;
        if (result != null)
        {
            if (on) result.RetryClicked += Confirm;
            else result.RetryClicked -= Confirm;
        }
    }

    private void OnDestroy()
    {
        Subscribe(false);
        _special?.Dispose();
        foreach (AudioSource source in _sources)
            if (source != null) source.Stop();
    }
}
