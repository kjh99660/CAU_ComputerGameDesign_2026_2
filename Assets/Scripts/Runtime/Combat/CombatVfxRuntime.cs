using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// World-space effects remain independent of enemies returning to their pool.
public sealed class CombatVfxRuntime : MonoBehaviour, IRoundResettable
{
    private const float SpecialReferenceRadius = 6f;   // the special burst sizes in the settings are written for this radius

    private sealed class Effect
    {
        public GameObject Root;
        public ParticleSystem[] Particles;
        public float Age;
        public bool Active;
    }

    private readonly List<Effect> _slashes = new();
    private readonly List<Effect> _impacts = new();
    private readonly List<Effect> _specialBursts = new();
    private readonly List<Effect> _specialRadials = new();
    private List<Effect>[] _pools;
    // Built on first use, so the component also works where Awake has not run (edit-mode tests).
    private List<Effect>[] Pools => _pools ??= new[] { _slashes, _impacts, _specialBursts, _specialRadials };
    private CombatVfxSettings _settings;
    private EnemyRuntime _enemies;
    private CombatRoundRuntime _combat;
    private GameState _state;
    private int _tick;
    private bool _stopped;

    // Raised when a swing effect is really shown (same tick, combat running). Used for the camera pulse.
    public event Action<int> SlashPlayed;

    // Charge scale of the latest swing, for SlashPlayed listeners (a charged swing has a heavier sound).
    public float LastSlashScale { get; private set; } = 1f;

    // Effects currently alive in each pool. Read by tests.
    public int ActiveSpecialEffects => CountActive(_specialBursts) + CountActive(_specialRadials);
    // Instances created so far for the special, alive or waiting in the pool.
    public int PooledSpecialEffects => _specialBursts.Count + _specialRadials.Count;

    public void Configure(EnemyRuntime enemies, CombatVfxSettings settings, CombatRoundRuntime combat = null)
    {
        _settings = settings;
        _enemies = enemies;
        _combat = combat;
        if (_enemies != null) _enemies.HitPresented += PlayImpact;
        if (_combat != null) _combat.SpecialActivated += PlaySpecial;
        enabled = false;
    }

    public void PlaySlash(Transform player, float chargeScale, int tick)
    {
        if (!CanPlay(tick) || player == null || _settings == null) return;
        Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up);
        Quaternion facing = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;
        Play(_slashes, _settings.slashPrefab, player.position + facing * _settings.slashOffset,
            facing * Quaternion.Euler(_settings.slashEulerOffset), _settings.slashScale * Mathf.Max(0.01f, chargeScale));
        LastSlashScale = chargeScale;
        SlashPlayed?.Invoke(tick);
    }

    private void PlayImpact(Vector3 position, Vector3 direction, int tick)
    {
        if (!CanPlay(tick) || _settings == null) return;
        Quaternion facing = direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity;
        Play(_impacts, _settings.impactPrefab, position + _settings.impactOffset,
            facing * Quaternion.Euler(_settings.impactEulerOffset), _settings.impactScale);
    }

    // Gauge special: one burst on the player and a ring of copies facing outward, sized to the real radius.
    private void PlaySpecial(Vector3 center, float radius, int tick)
    {
        if (!CanPlay(tick) || _settings == null) return;
        float size = Mathf.Max(0.1f, radius) / SpecialReferenceRadius;
        Vector3 origin = center + _settings.specialOffset;
        Play(_specialBursts, _settings.specialBurstPrefab, origin, Quaternion.identity, _settings.specialBurstScale * size);
        int count = _settings.specialRadialPrefab != null ? _settings.specialRadialCount : 0;
        Quaternion tilt = Quaternion.Euler(_settings.specialRadialEulerOffset);
        for (int i = 0; i < count; i++)
        {
            Quaternion outward = Quaternion.Euler(0f, i * 360f / count, 0f);
            Play(_specialRadials, _settings.specialRadialPrefab,
                origin + outward * Vector3.forward * (radius * _settings.specialRadialDistance),
                outward * tilt, _settings.specialRadialScale * size);
        }
    }

    private bool CanPlay(int tick) => !_stopped && tick == _tick &&
        (_state == GameState.Battle || _state == GameState.Finishing);

    private void Play(List<Effect> pool, GameObject prefab, Vector3 position, Quaternion rotation, float scale)
    {
        if (prefab == null) return;
        Effect effect = null;
        if (OptimizationManager.CombatVfxPooling)
        {
            for (int i = 0; i < pool.Count; i++)
                if (!pool[i].Active) { effect = pool[i]; break; }
        }
        else
        {
            DiscardInactive(pool);
        }
        if (effect == null)
        {
            if (pool.Count >= Mathf.Max(1, _settings.maximumInstancesPerEffect)) return;
            var root = Instantiate(prefab, transform);
            effect = new Effect { Root = root, Particles = root.GetComponentsInChildren<ParticleSystem>(true) };
            foreach (ParticleSystem particle in effect.Particles)
            {
                var main = particle.main;
                main.loop = false;
                main.playOnAwake = false;
                main.useUnscaledTime = false;
                main.stopAction = ParticleSystemStopAction.None;
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            pool.Add(effect);
        }
        effect.Root.transform.SetPositionAndRotation(position, rotation);
        effect.Root.transform.localScale = prefab.transform.localScale * Mathf.Max(0.01f, scale);
        effect.Root.SetActive(true);
        effect.Age = 0f;
        effect.Active = true;
        foreach (ParticleSystem particle in effect.Particles)
        {
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (particle.gameObject.activeInHierarchy) particle.Play(false);
        }
        enabled = true;
    }

    public void ApplyState(GameStateChangedEvent notification)
    {
        if (notification.Tick < _tick) return;
        if (notification.Tick != _tick) Clear();
        _tick = notification.Tick;
        _state = notification.Current;
        if (_state == GameState.Ready) _stopped = false;
        if (_state != GameState.Battle && _state != GameState.Finishing && _state != GameState.Paused)
        { Clear(); return; }
        foreach (List<Effect> pool in Pools)
            SetPaused(pool, _state == GameState.Paused);
        enabled = _state != GameState.Paused && HasActive();
    }

    private static void SetPaused(List<Effect> pool, bool paused)
    {
        foreach (Effect effect in pool)
            if (effect.Active)
                foreach (ParticleSystem particle in effect.Particles)
                {
                    if (paused && particle.isPlaying) particle.Pause(false);
                    else if (!paused && particle.isPaused) particle.Play(false);
                }
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        foreach (List<Effect> pool in Pools)
            Advance(pool, deltaTime);
        enabled = HasActive();
    }

    private void Advance(List<Effect> pool, float deltaTime)
    {
        foreach (Effect effect in pool)
        {
            if (!effect.Active) continue;
            effect.Age += deltaTime;
            bool alive = false;
            foreach (ParticleSystem particle in effect.Particles)
                if (particle.IsAlive(false)) { alive = true; break; }
            if (!alive || effect.Age >= _settings.maximumLifetime) Release(effect);
        }
        if (!OptimizationManager.CombatVfxPooling) DiscardInactive(pool);
    }

    private bool HasActive()
    {
        foreach (List<Effect> pool in Pools)
            foreach (Effect effect in pool)
                if (effect.Active) return true;
        return false;
    }

    private static int CountActive(List<Effect> pool)
    {
        int count = 0;
        foreach (Effect effect in pool)
            if (effect.Active) count++;
        return count;
    }

    private static void Release(Effect effect)
    {
        foreach (ParticleSystem particle in effect.Particles)
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        effect.Root.SetActive(false);
        effect.Active = false;
    }

    private void Clear()
    {
        foreach (List<Effect> pool in Pools)
        {
            foreach (Effect effect in pool)
                if (effect.Active) Release(effect);
            if (!OptimizationManager.CombatVfxPooling) DiscardInactive(pool);
        }
        enabled = false;
    }

    private void DiscardInactive(List<Effect> pool)
    {
        for (int i = pool.Count - 1; i >= 0; i--)
        {
            if (pool[i].Active) continue;
            Destroy(pool[i].Root);
            pool.RemoveAt(i);
        }
    }

    public void StopProducing() { _stopped = true; Clear(); }
    public IEnumerator ResetRound(int tick) { Clear(); _tick = tick; yield break; }
    private void OnDestroy()
    {
        if (_enemies != null) _enemies.HitPresented -= PlayImpact;
        if (_combat != null) _combat.SpecialActivated -= PlaySpecial;
    }
}
