using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// World-space effects remain independent of enemies returning to their pool.
public sealed class CombatVfxRuntime : MonoBehaviour, IRoundResettable
{
    private sealed class Effect
    {
        public GameObject Root;
        public ParticleSystem[] Particles;
        public float Age;
        public bool Active;
    }

    private readonly List<Effect> _slashes = new();
    private readonly List<Effect> _impacts = new();
    private CombatVfxSettings _settings;
    private EnemyRuntime _enemies;
    private GameState _state;
    private int _tick;
    private bool _stopped;

    public void Configure(EnemyRuntime enemies, CombatVfxSettings settings)
    {
        _settings = settings;
        _enemies = enemies;
        if (_enemies != null) _enemies.HitPresented += PlayImpact;
        enabled = false;
    }

    public void PlaySlash(Transform player, float chargeScale, int tick)
    {
        if (!CanPlay(tick) || player == null || _settings == null) return;
        Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up);
        Quaternion facing = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;
        Play(_slashes, _settings.slashPrefab, player.position + facing * _settings.slashOffset,
            facing * Quaternion.Euler(_settings.slashEulerOffset), _settings.slashScale * Mathf.Max(0.01f, chargeScale));
    }

    private void PlayImpact(Vector3 position, Vector3 direction, int tick)
    {
        if (!CanPlay(tick) || _settings == null) return;
        Quaternion facing = direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity;
        Play(_impacts, _settings.impactPrefab, position + _settings.impactOffset,
            facing * Quaternion.Euler(_settings.impactEulerOffset), _settings.impactScale);
    }

    private bool CanPlay(int tick) => !_stopped && tick == _tick &&
        (_state == GameState.Battle || _state == GameState.Finishing);

    private void Play(List<Effect> pool, GameObject prefab, Vector3 position, Quaternion rotation, float scale)
    {
        if (prefab == null) return;
        Effect effect = null;
        for (int i = 0; i < pool.Count; i++)
            if (!pool[i].Active) { effect = pool[i]; break; }
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
        SetPaused(_slashes, _state == GameState.Paused);
        SetPaused(_impacts, _state == GameState.Paused);
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
        Advance(_slashes, Time.deltaTime);
        Advance(_impacts, Time.deltaTime);
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
    }

    private bool HasActive()
    {
        foreach (Effect effect in _slashes) if (effect.Active) return true;
        foreach (Effect effect in _impacts) if (effect.Active) return true;
        return false;
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
        foreach (Effect effect in _slashes) if (effect.Active) Release(effect);
        foreach (Effect effect in _impacts) if (effect.Active) Release(effect);
        enabled = false;
    }

    public void StopProducing() { _stopped = true; Clear(); }
    public IEnumerator ResetRound(int tick) { Clear(); _tick = tick; yield break; }
    private void OnDestroy()
    {
        if (_enemies != null) _enemies.HitPresented -= PlayImpact;
    }
}
