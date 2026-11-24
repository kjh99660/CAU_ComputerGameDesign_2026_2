using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Keeps the player readable inside a crowd and in the dark: a constant-width outline around the whole character.
// Two extra materials are added to every mesh renderer under the player at run time (the prefab is not changed):
// one marks the silhouette in the stencil buffer, the other draws a slightly inflated copy only outside that mark.
[DisallowMultipleComponent]
public sealed class PlayerOutline : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int PixelsId = Shader.PropertyToID("_OutlinePixels");
    private static readonly int StencilCompId = Shader.PropertyToID("_StencilComp");
    private static readonly int StencilPassId = Shader.PropertyToID("_StencilPass");
    private static readonly int ColorMaskId = Shader.PropertyToID("_ColorMask");
    private static readonly int ZTestId = Shader.PropertyToID("_ZTest");

    private readonly List<Renderer> _renderers = new();
    private readonly List<Material[]> _originals = new();
    private Material _mask;
    private Material _hull;
    private Color _restColor = Color.white;

    public bool IsShown => _hull != null;
    public int RendererCount => _renderers.Count;
    // The colour the outline normally has, and the one it shows now (a hit tints it for a moment).
    public Color RestColor => _restColor;
    public Color CurrentColor => _hull != null ? _hull.GetColor(ColorId) : _restColor;

    public void ShowColor(Color color)
    {
        if (_hull != null) _hull.SetColor(ColorId, color);
    }

    public void ShowRestColor() => ShowColor(_restColor);

    // Width 0 or a missing shader leaves the character as it is.
    public void Configure(Shader shader, Color color, float pixels, bool throughOccluders)
    {
        Remove();
        if (shader == null || pixels <= 0f)
            return;

        // Both draw after the opaque scene and before the transparent effects; all marks before any band.
        float depthTest = (float)(throughOccluders ? CompareFunction.Always : CompareFunction.LessEqual);
        _mask = new Material(shader) { name = "Player Outline Mask", renderQueue = (int)RenderQueue.Transparent - 60 };
        _mask.SetFloat(PixelsId, 0f);
        _mask.SetFloat(ColorMaskId, 0f);
        _mask.SetFloat(StencilCompId, (float)CompareFunction.Always);
        _mask.SetFloat(StencilPassId, (float)StencilOp.Replace);
        _mask.SetFloat(ZTestId, depthTest);

        _hull = new Material(shader) { name = "Player Outline", renderQueue = (int)RenderQueue.Transparent - 50 };
        _restColor = color;
        _hull.SetColor(ColorId, color);
        _hull.SetFloat(PixelsId, pixels);
        _hull.SetFloat(ColorMaskId, 15f);
        _hull.SetFloat(StencilCompId, (float)CompareFunction.NotEqual);
        _hull.SetFloat(StencilPassId, (float)StencilOp.Keep);
        _hull.SetFloat(ZTestId, depthTest);

        // Includes renderers that are switched off right now (the sheathed and the drawn sword swap at run time).
        foreach (Renderer candidate in GetComponentsInChildren<Renderer>(true))
        {
            if (!(candidate is SkinnedMeshRenderer) && !(candidate is MeshRenderer))
                continue;
            Material[] original = candidate.sharedMaterials;
            var extended = new Material[original.Length + 2];
            original.CopyTo(extended, 0);
            extended[original.Length] = _mask;
            extended[original.Length + 1] = _hull;
            candidate.sharedMaterials = extended;
            _renderers.Add(candidate);
            _originals.Add(original);
        }
    }

    public void Remove()
    {
        for (int i = 0; i < _renderers.Count; i++)
            if (_renderers[i] != null)
                _renderers[i].sharedMaterials = _originals[i];
        _renderers.Clear();
        _originals.Clear();
        if (_mask != null) Destroy(_mask);
        if (_hull != null) Destroy(_hull);
        _mask = null;
        _hull = null;
    }

    private void OnDestroy() => Remove();
}
