using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DefaultExecutionOrder(-1100)]
[DisallowMultipleComponent]
public sealed class OptimizationManager : MonoBehaviour
{
    [SerializeField] private bool spatialPartitioning = true;
    [SerializeField] private bool enemyPooling = true;
    [SerializeField] private bool enemyPrewarming = true;
    [SerializeField] private bool combatVfxPooling = true;
    [SerializeField] private bool nonAllocHitQueries = true;
    [SerializeField] private bool srpBatcher = true;

    public static OptimizationManager Instance { get; private set; }
    public static bool SpatialPartitioning => Instance == null || Instance.spatialPartitioning;
    public static bool EnemyPooling => Instance == null || Instance.enemyPooling;
    public static bool EnemyPrewarming => Instance == null || Instance.enemyPrewarming;
    public static bool CombatVfxPooling => Instance == null || Instance.combatVfxPooling;
    public static bool NonAllocHitQueries => Instance == null || Instance.nonAllocHitQueries;

    private UniversalRenderPipelineAsset _overriddenPipeline;
    private bool _originalBatcher;

    private void OnEnable()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Only one OptimizationManager can be active.", this);
            enabled = false;
            return;
        }
        Instance = this;
        Update();
    }

    private void Update()
    {
        if (srpBatcher)
        {
            RestoreBatcher();
            return;
        }

        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (_overriddenPipeline == pipeline) return;
        RestoreBatcher();
        if (pipeline == null) return;
        _originalBatcher = pipeline.useSRPBatcher;
        _overriddenPipeline = pipeline;
        pipeline.useSRPBatcher = false;
        GraphicsSettings.useScriptableRenderPipelineBatching = false;
    }

    private void OnDisable()
    {
        if (Instance != this) return;
        RestoreBatcher();
        Instance = null;
    }

    private void RestoreBatcher()
    {
        if (_overriddenPipeline != null)
        {
            _overriddenPipeline.useSRPBatcher = _originalBatcher;
            if (GraphicsSettings.currentRenderPipeline == _overriddenPipeline)
                GraphicsSettings.useScriptableRenderPipelineBatching = _originalBatcher;
        }
        _overriddenPipeline = null;
    }
}
