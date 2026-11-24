using System.IO;
using UnityEditor;
using UnityEngine;

// Makes the neon floor-marking material (REBOOT/Ground Indicator) and points CombatVfxSettings at it, so the charge
// attack's reach and the special's shock ring use it. Referencing it from the settings asset also keeps the shader in
// builds. Fills only an empty field, so a material chosen in the inspector is kept.
//   Unity -batchmode -executeMethod GroundIndicatorBuilder.BuildBatch -quit
public static class GroundIndicatorBuilder
{
    public const string MaterialPath = "Assets/Prefabs/Combat/GroundIndicator.mat";
    private const string SettingsPath = "Assets/Resources/CombatVfxSettings.asset";
    private const string ShaderName = "REBOOT/Ground Indicator";

    [MenuItem("Tools/REBOOT Feel/Build Ground Indicator")]
    public static void Build()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null) throw new IOException("Shader " + ShaderName + " not found");
        Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "GroundIndicator" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = shader;
        EditorUtility.SetDirty(material);
        var settings = AssetDatabase.LoadAssetAtPath<CombatVfxSettings>(SettingsPath);
        if (settings == null) throw new IOException("Missing " + SettingsPath);
        if (settings.groundIndicatorMaterial == null)
        {
            settings.groundIndicatorMaterial = material;
            EditorUtility.SetDirty(settings);
        }
        AssetDatabase.SaveAssets();
    }

    public static void BuildBatch()
    {
        Build();
        Debug.Log("REBOOT ground indicator material at " + MaterialPath);
    }
}
