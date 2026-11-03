using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// One reusable world-space ring; it follows the round lifecycle without changing timeScale.
[DisallowMultipleComponent]
public sealed class SpecialFeedback : MonoBehaviour, IRoundResettable
{
    private CombatRoundRuntime _combat;
    private LineRenderer _ring;
    private Material _material;
    private float _elapsed;
    private float _radius;
    private int _tick;
    private bool _advancing;

    public void Configure(CombatRoundRuntime combat)
    {
        _combat = combat != null ? combat : throw new ArgumentNullException(nameof(combat));
        var ringObject = new GameObject("SpecialShockwave", typeof(LineRenderer));
        ringObject.transform.SetParent(transform, false);
        _ring = ringObject.GetComponent<LineRenderer>();
        _ring.useWorldSpace = false;
        _ring.loop = true;
        _ring.positionCount = 64;
        for (int i = 0; i < _ring.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / _ring.positionCount;
            _ring.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
        }
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
        {
            _material = new Material(shader);
            _material.SetColor("_BaseColor", _combat.Settings.specialEffectColor);
            _material.SetFloat("_Surface", 1f);
            _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_ZWrite", 0);
            _material.SetOverrideTag("RenderType", "Transparent");
            _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _material.renderQueue = (int)RenderQueue.Transparent;
            _ring.sharedMaterial = _material;
        }
        _ring.enabled = false;
        _combat.SpecialActivated += Play;
        enabled = false;
    }

    public void ApplyState(GameStateChangedEvent notification)
    {
        if (notification.Tick < _tick) return;
        _tick = notification.Tick;
        _advancing = notification.Current == GameState.Battle || notification.Current == GameState.Finishing;
        if (!_advancing && notification.Current != GameState.Paused) Hide();
    }

    private void Play(Vector3 center, float radius, int tick)
    {
        if (tick != _tick || !_advancing || _material == null) return;
        _elapsed = 0f;
        _radius = Mathf.Max(0f, radius);
        _ring.transform.position = center + Vector3.up * 0.12f;
        _ring.transform.localScale = Vector3.zero;
        _ring.widthMultiplier = Mathf.Max(0.01f, _combat.Settings.specialEffectWidth);
        _ring.enabled = true;
        enabled = true;
    }

    private void Update()
    {
        if (!_advancing) return;
        float duration = Mathf.Max(0.05f, _combat.Settings.specialEffectDuration);
        _elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(_elapsed / duration);
        float eased = 1f - (1f - progress) * (1f - progress);
        _ring.transform.localScale = Vector3.one * (_radius * eased);
        Color color = _combat.Settings.specialEffectColor;
        color.a *= 1f - progress;
        _material.SetColor("_BaseColor", color);
        if (progress >= 1f) Hide();
    }

    public void StopProducing() => Hide();
    public IEnumerator ResetRound(int tick) { _tick = tick; Hide(); yield break; }

    private void Hide()
    {
        if (_ring != null) _ring.enabled = false;
        enabled = false;
    }

    private void OnDestroy()
    {
        if (_combat != null) _combat.SpecialActivated -= Play;
        if (_material != null) Destroy(_material);
    }
}
