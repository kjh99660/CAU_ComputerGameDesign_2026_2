using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// 피격 글리치 렌더러 기능. ScreenGlitchState.Amount가 0보다 클 때만 패스를 추가하므로 평소 비용은 없다.
// 후처리 뒤(AfterRenderingPostProcessing)에 적용되어 3D 화면만 깨지고, Screen Space Overlay UI는 영향을 받지 않는다
// (HUD 흔들림은 BattleView가 따로 처리한다).
public sealed class RebootHitGlitchFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader shader;

    private static readonly int AmountId = Shader.PropertyToID("_Amount");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int AlertId = Shader.PropertyToID("_AlertColor");
    private static readonly int AccentId = Shader.PropertyToID("_AccentColor");

    private Material _material;
    private GlitchPass _pass;

    public override void Create()
    {
        if (shader == null)
            shader = Shader.Find("Hidden/Reboot/HitGlitch");
        if (shader != null && _material == null)
            _material = CoreUtils.CreateEngineMaterial(shader);
        _pass = new GlitchPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_material == null || ScreenGlitchState.Amount <= 0.001f)
            return;
        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;

        _material.SetFloat(AmountId, ScreenGlitchState.Amount);
        _material.SetFloat(SeedId, ScreenGlitchState.Seed);
        _material.SetColor(AlertId, UiPalette.Alert);
        _material.SetColor(AccentId, UiPalette.Accent);
        _pass.Setup(_material);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }

    private sealed class GlitchPass : ScriptableRenderPass
    {
        private Material _material;

        public void Setup(Material material)
        {
            _material = material;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || _material == null)
                return;

            TextureHandle source = resources.activeColorTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(source);
            desc.name = "RebootHitGlitch";
            desc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, _material, 0), "Reboot Hit Glitch");
            resources.cameraColor = destination;
        }
    }
}
