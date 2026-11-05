using UnityEditor;
using UnityEngine;

public static class CombatSettingsAssetCreator
{
    [MenuItem("Tools/Game Demo Combat/Create Default Settings")]
    public static void CreateDefaultSettings()
    {
        const string path = "Assets/Resources/CombatSettings.asset";
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (AssetDatabase.LoadAssetAtPath<CombatSettings>(path) == null)
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<CombatSettings>(), path);
        CopySprite("Assets/Sprites/UI/Battle/Battle_SpecialGaugeFrame.png", "Assets/Resources/CombatGaugeFrame.png");
        CopySprite("Assets/Sprites/UI/Battle/Battle_SpecialGaugeFill.png", "Assets/Resources/CombatGaugeFill.png");
        AssetDatabase.SaveAssets();
    }

    private static void CopySprite(string source, string destination)
    {
        if (AssetDatabase.LoadAssetAtPath<Sprite>(destination) != null) return;
        if (!AssetDatabase.CopyAsset(source, destination))
            Debug.LogError("Could not copy gauge sprite: " + source);
    }
}
