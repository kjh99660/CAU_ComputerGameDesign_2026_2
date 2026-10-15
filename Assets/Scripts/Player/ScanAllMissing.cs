using UnityEngine;
using UnityEditor;

public class ScanAllMissing : MonoBehaviour
{
    void Start()
    {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (n > 0)
                Debug.LogWarning($"Missing {n}개: '{go.name}' scene='{go.scene.name}' " +
                                 $"hideFlags={go.hideFlags} prefabAsset={EditorUtility.IsPersistent(go)}", go);
        }
        Debug.Log("[ScanAllMissing] 완료");
    }
}