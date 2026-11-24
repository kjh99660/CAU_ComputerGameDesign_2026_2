using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// One reusable world-space shock ring for the special; it follows the round lifecycle without changing timeScale.
// With the ground indicator material (CombatVfxSettings) the ring is a dashed neon line that starts hot magenta and
// cools to cyan as it spreads and fades; without it, the first prototype's plain coloured ring.
[DisallowMultipleComponent]
public sealed class SpecialFeedback : MonoBehaviour, IRoundResettable
{
    private static readonly int ChargeId = Shader.PropertyToID("_Charge");
    private static readonly int FadeId = Shader.PropertyToID("_Fade");
    private static readonly int ModeId = Shader.PropertyToID("_Mode");

    private CombatRoundRuntime _combat;
    private LineRenderer _ring;
    private Material _material;
    private bool _neon;
    private float _elapsed;
    private float _radius;
    private int _tick;
    private bool _advancing;

    // Read by tests: the ring is showing, and with the neon look.
    public bool IsShowing => _ring != null && _ring.enabled;
    public bool UsesNeon => _neon;

    public void Configure(CombatRoundRuntime combat, Material indicator = null)
    {
        _combat = combat != null ? combat : throw new ArgumentNullException(nameof(combat));
        var ringObject = new GameObject("SpecialShockwave", typeof(LineRenderer));
        ringObject.transform.SetParent(transform, false);
        _ring = ringObject.GetComponent<LineRenderer>();
        _ring.useWorldSpace = false;
        _ring.loop = true;
        _ring.positionCount = 64;
        _ring.textureMode = LineTextureMode.Stretch;
        _ring.shadowCastingMode = ShadowCastingMode.Off;
        _ring.receiveShadows = false;
        for (int i = 0; i < _ring.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / _ring.positionCount;
            _ring.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
        }
        if (indicator != null)
        {
            _material = new Material(indicator);
            _material.SetFloat(ModeId, 1f);
            _neon = _material.HasProperty(FadeId);
            _ring.sharedMaterial = _material;
        }
        else
        {
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
        _ring.widthMultiplier = Mathf.Max(0.01f, _combat.Settings.specialEffectWidth) * (_neon ? 2f : 1f);
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
        if (_neon)
        {
            _material.SetFloat(ChargeId, 1f - progress);   // hot at the burst, cooling as it spreads
            _material.SetFloat(FadeId, 1f - progress * progress);
        }
        else
        {
            Color color = _combat.Settings.specialEffectColor;
            color.a *= 1f - progress;
            _material.SetColor("_BaseColor", color);
        }
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
