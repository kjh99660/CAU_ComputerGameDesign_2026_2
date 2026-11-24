using System;
using UnityEditor;
using UnityEngine;

// Writes the assets the combat feedback needs, through the editor so references and file IDs stay valid:
//   - the enemy's extra attack clips and controller states (GameDemoHumanoidEnemyBuilder.RebuildClips),
//   - the enemy's glow mask and material (EnemyGlowBuilder),
//   - the references in Resources/CombatVfxSettings (special burst prefabs, player outline shader),
//   - the new hit-blur values in the Battle prefab, so every computer tunes the same numbers.
// Every step can be run again and gives the same result.
//   -executeMethod RebootCombatFeedbackSetup.ApplyBatch
public static class RebootCombatFeedbackSetup
{
    private const string SettingsPath = "Assets/Resources/CombatVfxSettings.asset";
    private const string BattlePrefabPath = "Assets/Prefabs/UI/Battle.prefab";
    // Effects already in the project (Free Slash VFX pack). Provisional picks.
    private const string SpecialBurstPrefab = "Assets/Free Slash VFX/Prefabs/Multiple Slashes.prefab";
    private const string SpecialRadialPrefab = "Assets/Free Slash VFX/Prefabs/Slash Water VFX.prefab";
    private const string OutlineShader = "Hidden/Reboot/CharacterOutline";

    [MenuItem("Tools/REBOOT Feel/Apply Combat Feedback Assets")]
    public static void Apply()
    {
        GameDemoHumanoidEnemyBuilder.RebuildClips();
        EnemyGlowBuilder.Apply();
        WireSettings();
        SaveBattleViewSettings();
        AssetDatabase.SaveAssets();
        Debug.Log("[RebootFeel] Combat feedback assets applied.");
    }

    public static void ApplyBatch()
    {
        try { Apply(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // Fills only what is still empty, so picks made later in the Inspector are kept.
    private static void WireSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<CombatVfxSettings>(SettingsPath)
                       ?? throw new InvalidOperationException("Missing " + SettingsPath);
        if (settings.specialBurstPrefab == null)
            settings.specialBurstPrefab = Load<GameObject>(SpecialBurstPrefab);
        if (settings.specialRadialPrefab == null)
            settings.specialRadialPrefab = Load<GameObject>(SpecialRadialPrefab);
        if (settings.playerOutlineShader == null)
            settings.playerOutlineShader = Shader.Find(OutlineShader) ?? throw new InvalidOperationException("Shader missing: " + OutlineShader);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
    }

    // Same step as RebootUiThemeBuilder.SaveViewSettingsBatch, for the Battle prefab only.
    private static void SaveBattleViewSettings()
    {
        AssetDatabase.ImportAsset(BattlePrefabPath, ImportAssetOptions.ForceUpdate);
        GameObject root = PrefabUtility.LoadPrefabContents(BattlePrefabPath);
        try
        {
            root.GetComponent<UIScreenView>()?.AutoBind();
            PrefabUtility.SaveAsPrefabAsset(root, BattlePrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static T Load<T>(string path) where T : UnityEngine.Object =>
        AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing " + path);
}
