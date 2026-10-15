// Assets/Editor/FindMissingScripts.cs 에 넣으세요 (Editor 폴더 필수)
// 삭제는 하지 않고, Missing Script 정보를 Console에 표시만 합니다.
// Console 로그를 클릭하면 해당 오브젝트가 Hierarchy에서 하이라이트됩니다.
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FindMissingScripts
{
    [MenuItem("Tools/Find Missing Scripts/In Selection (with children)")]
    static void InSelection()
    {
        int found = 0, scanned = 0;
        foreach (var root in Selection.gameObjects)
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                scanned++;
                found += Report(t.gameObject);
            }
        Debug.Log($"[MissingScripts] 선택 영역 검사 완료: 오브젝트 {scanned}개 검사, Missing {found}개 발견");
    }

    [MenuItem("Tools/Find Missing Scripts/In Active Scene")]
    static void InScene()
    {
        int found = 0, scanned = 0;
        Scene scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                scanned++;
                found += Report(t.gameObject);
            }
        Debug.Log($"[MissingScripts] 씬 '{scene.name}' 검사 완료: 오브젝트 {scanned}개 검사, Missing {found}개 발견");
    }

    // 오브젝트 하나를 검사하고 Missing 개수를 반환
    static int Report(GameObject go)
    {
        int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
        if (count == 0) return 0;

        var sb = new StringBuilder();
        sb.AppendLine($"[MissingScripts] '{GetPath(go)}' 에 Missing Script {count}개");

        // 컴포넌트 목록에서 null 인 칸이 Missing Script 자리
        var comps = go.GetComponents<Component>();
        for (int i = 0; i < comps.Length; i++)
            if (comps[i] == null)
                sb.AppendLine($"  - 컴포넌트 순서 #{i} (Inspector 위에서 {i + 1}번째)");

        // 프리팹 관련 정보
        if (PrefabUtility.IsPartOfPrefabInstance(go))
        {
            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            sb.AppendLine($"  - 프리팹 인스턴스 소속, 원본 에셋: {assetPath}");

            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source != null)
            {
                int srcCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(source);
                sb.AppendLine(srcCount > 0
                    ? $"  - 원본 프리팹/FBX 안에도 Missing {srcCount}개 있음 (원본에서 해결해야 사라짐)"
                    : "  - 원본에는 없음 -> 이 씬 인스턴스에서 추가된 컴포넌트(Override)");
            }
        }
        else if (PrefabUtility.IsPartOfPrefabAsset(go))
        {
            sb.AppendLine("  - 프리팹 에셋 자체에 있는 오브젝트");
        }
        else
        {
            sb.AppendLine("  - 일반 씬 오브젝트 (프리팹 아님)");
        }

        // 두 번째 인자로 go를 넘기면 로그 클릭 시 해당 오브젝트가 하이라이트됨
        Debug.LogWarning(sb.ToString(), go);
        return count;
    }

    static string GetPath(GameObject go)
    {
        string path = go.name;
        var t = go.transform.parent;
        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        return path;
    }
}