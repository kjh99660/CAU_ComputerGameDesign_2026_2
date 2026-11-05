using UnityEditor;
using UnityEngine;

public static class DialogueSequenceAssetCreator
{
    [MenuItem("Tools/Game Demo Dialogue/Create Default Sequence")]
    public static void CreateDefaultSequence()
    {
        const string path = "Assets/Resources/DialogueSequence.asset";
        if (AssetDatabase.LoadAssetAtPath<DialogueSequenceData>(path) != null) return;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<DialogueSequenceData>(), path);
        AssetDatabase.SaveAssets();
    }
}
