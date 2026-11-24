using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// CC0 외부 소품(RG Poly Sci-Fi Arena, Assets/ThirdParty/RGPoly_SciFiArena)을 Env_RebootHub/ArenaProps_CC0 아래에 놓는다.
// - 장식이므로 충돌체를 두지 않는다. 모델 가져오기 설정의 Generate Colliders를 끈다(레벨 테스트: Env_RebootHub 충돌체 0개).
// - 광장 반경 19.5m 안에는 놓지 않는다. 전투 공간은 그대로 두고 계단 위 데크, 옥상, 가장자리 턱, 벽면만 꾸민다.
// - 자리는 맵을 재서 고른다. 기대 높이의 평평한 면이 발판 네 모서리를 모두 받치고, 기존 충돌체·렌더러·다른 소품과
//   겹치지 않아야 한다. 요청 위치가 맞지 않으면 주변에서 가장 가까운 자리를 찾고, 없으면 건너뛰고 경고를 남긴다.
// - 소품 폴더가 없으면(외부 에셋을 받지 않은 환경) 아무것도 놓지 않는다.
public static partial class RebootHubEnvironmentBuilder
{
    private const string ArenaPropsRoot = "Assets/ThirdParty/RGPoly_SciFiArena";
    private const string ArenaPropsGroupName = "ArenaProps_CC0";
    private const float PropPlazaClearance = 19.5f;

    private readonly struct FloorProp
    {
        public readonly string Prefab;
        public readonly float X, Z, Up, Yaw, Search;

        public FloorProp(string prefab, float x, float z, float up, float yaw, float search = 1.5f)
        {
            Prefab = prefab; X = x; Z = z; Up = up; Yaw = yaw; Search = search;
        }
    }

    private readonly struct WallProp
    {
        public readonly string Prefab;
        public readonly Vector3 From, Direction, Slide;
        public readonly float MaxDistance, SlideRange;

        // 첫 자리가 맞지 않으면 시작점을 slide 방향으로 0.5m씩 앞뒤로 옮겨(최대 slideRange) 다시 찾는다.
        public WallProp(string prefab, Vector3 from, Vector3 direction, float maxDistance, Vector3 slide = default, float slideRange = 0f)
        {
            Prefab = prefab; From = from; Direction = direction.normalized; MaxDistance = maxDistance;
            Slide = slide.normalized; SlideRange = slideRange;
        }
    }

    // 맵을 열기 전에 호출한다. 이미 꺼져 있으면 아무것도 하지 않는다.
    private static void PrepareArenaPropImports()
    {
        if (!AssetDatabase.IsValidFolder(ArenaPropsRoot)) return;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ArenaPropsRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is ModelImporter importer && importer.addCollider)
            {
                importer.addCollider = false;
                importer.SaveAndReimport();
                Debug.Log($"[RebootHub] Generate Colliders off: {path}");
            }
        }
    }

    private static int PlaceArenaProps(GameObject env, Vector3 c)
    {
        if (!AssetDatabase.IsValidFolder(ArenaPropsRoot))
        {
            Debug.Log($"[RebootHub] {ArenaPropsRoot} not found; CC0 props skipped.");
            return 0;
        }
        Transform group = Group(env, ArenaPropsGroupName);
        float floor = c.y;
        var obstacles = SceneRendererBounds(env.transform);
        var placed = new List<Bounds>();
        int count = 0;

        FloorProp[] floorProps =
        {
            // 북쪽 계단 위 데크(+3.64m, 큰 건물 지붕): 허브 입구 양옆에 방패 바리케이드. 계단 가운데 길은 비운다.
            new FloorProp("BlueBase/Prefabs/Bariers/BarierP_4_A", -33.2f, 68.5f, 3.64f, 90f),
            new FloorProp("BlueBase/Prefabs/Bariers/BarierP_4_A", -14.8f, 68.5f, 3.64f, 90f),
            // 북동쪽 옥상(+4m, 계단 오른쪽): 화물 컨테이너와 동력 탱크.
            new FloorProp("Cargo/Prefabs/Props/ContainerC_B", -5.6f, 65.3f, 4f, 90f),
            new FloorProp("BlueBase/Prefabs/Other/WallPP_1_1_A", -1.4f, 64.3f, 4f, 180f),
            new FloorProp("BlueBase/Prefabs/Other/WallPP_1_1_A", -1.4f, 66.1f, 4f, 180f),
            // 서쪽 옥상(+5m, 광장 서쪽 벽 바로 뒤): 컨테이너와 큰 탱크.
            new FloorProp("Cargo/Prefabs/Props/ContainerC_A", -48.0f, 46.6f, 5f, 0f),
            new FloorProp("BlueBase/Prefabs/Other/PoleP_1_A", -48.0f, 50.4f, 5f, 0f),
            // 남쪽 계단 위 통로(+2m, 적이 들어오는 길): 양쪽 벽을 따라 낮은 바리케이드와 조명 기둥.
            new FloorProp("BlueBase/Prefabs/Bariers/BarierP_5_A_001", -27.5f, 19.3f, 2f, 0f),
            new FloorProp("BlueBase/Prefabs/Bariers/BarierP_5_A_001", -20.5f, 19.3f, 2f, 0f),
            new FloorProp("BlueBase/Prefabs/Other/PilarP_1_A", -27.6f, 23.3f, 2f, 0f),
            new FloorProp("BlueBase/Prefabs/Other/PilarP_1_A", -20.4f, 23.3f, 2f, 0f),
            // 광장 동쪽·북동·북서 가장자리 턱(+2m): 조명 기둥.
            new FloorProp("BlueBase/Prefabs/Other/PilarP_1_A", -3.0f, 45.0f, 2f, 0f, 1f),
            new FloorProp("BlueBase/Prefabs/Other/PilarP_1_A", -3.0f, 51.5f, 2f, 0f, 1f),
            new FloorProp("BlueBase/Prefabs/Other/PilarP_1_A", -5.0f, 56.2f, 2f, 0f, 1f),
            new FloorProp("BlueBase/Prefabs/Other/PilarP_1_A", -43.0f, 56.2f, 2f, 0f, 1f),
        };
        foreach (FloorProp prop in floorProps)
            if (TryPlaceFloorProp(group, prop, c, obstacles, placed)) count++;

        WallProp[] wallProps =
        {
            // 북서쪽 건물의 동쪽 벽(+5.5m, 계단 쪽을 향함)의 세로 간판. 시작 화면 왼쪽 위에 보인다.
            new WallProp("BlueBase/Prefabs/Neons/Neon_3", new Vector3(c.x - 8f, floor + 5.5f, c.z + 8f), new Vector3(-1f, 0f, 1f), 25f),
            // 북동쪽 모서리 높은 건물의 서쪽 벽(+5.5m, 광장을 향함)의 세로 간판.
            new WallProp("BlueBase/Prefabs/Neons/Neon_3", new Vector3(c.x + 12f, floor + 5.5f, c.z + 9.2f), Vector3.right, 12f, Vector3.forward, 1.5f),
            // 광장 북서쪽 모서리 너머 건물의 남쪽 벽(+2.5m, 광장을 향함)의 가로 간판.
            new WallProp("BlueBase/Prefabs/Neons/Neon_1", new Vector3(c.x - 19f, floor + 2.5f, c.z + 10f), Vector3.forward, 8f, Vector3.right, 2f),
            // 남쪽 계단 위 통로의 양쪽 낮은 벽(통로 바닥 +2m에서 1m 위).
            new WallProp("BlueBase/Prefabs/Neons/Neon_2", new Vector3(c.x, floor + 3f, 21.6f), Vector3.left, 6f),
            new WallProp("BlueBase/Prefabs/Neons/Neon_2", new Vector3(c.x, floor + 3f, 21.6f), Vector3.right, 6f),
        };
        foreach (WallProp prop in wallProps)
            if (TryPlaceWallProp(group, prop, c, placed)) count++;

        Debug.Log($"[RebootHub] CC0 props placed: {count}/{floorProps.Length + wallProps.Length}");
        return count;
    }

    private static bool TryPlaceFloorProp(Transform group, FloorProp prop, Vector3 c, List<Bounds> obstacles, List<Bounds> placed)
    {
        GameObject prefab = LoadArenaProp(prop.Prefab);
        if (prefab == null) return false;
        Bounds local = LocalBounds(prefab);
        Quaternion rotation = Quaternion.Euler(0f, prop.Yaw, 0f);
        float expected = c.y + prop.Up;
        string reason = "no candidate";

        foreach (Vector2 offset in SearchOffsets(prop.Search, 0.25f))
        {
            var spot = new Vector2(prop.X + offset.x, prop.Z + offset.y);
            if (!FitsOnFloor(spot, local, rotation, expected, c, obstacles, placed, out float surface, out Bounds world, out reason))
                continue;
            Vector3 pivot = new Vector3(spot.x, surface - local.min.y, spot.y) - rotation * new Vector3(local.center.x, 0f, local.center.z);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            instance.transform.SetPositionAndRotation(pivot, rotation);
            placed.Add(world);
            Debug.Log($"[RebootHub] CC0 {prefab.name} at ({spot.x:0.##}, {surface:0.##}, {spot.y:0.##}) yaw {prop.Yaw} (asked {prop.X}, {prop.Z})");
            return true;
        }
        Debug.LogWarning($"[RebootHub] CC0 {prefab.name} skipped near ({prop.X}, {prop.Z}) +{prop.Up}m: {reason}");
        return false;
    }

    private static bool FitsOnFloor(Vector2 spot, Bounds local, Quaternion rotation, float expected, Vector3 c,
        List<Bounds> obstacles, List<Bounds> placed, out float surface, out Bounds world, out string reason)
    {
        surface = 0f;
        world = default;
        if (!FirstHit(new Vector3(spot.x, expected + 2.5f, spot.y), Vector3.down, 4f, out RaycastHit ground)
            || Mathf.Abs(ground.point.y - expected) > 0.35f || ground.normal.y < 0.95f)
        {
            reason = ground.collider == null
                ? "no surface below"
                : $"no flat surface at the expected height (hit {ground.collider.name} y={ground.point.y:0.##}, normal.y={ground.normal.y:0.##})";
            return false;
        }
        surface = ground.point.y;

        // 발판 네 모서리가 같은 면 위에 있어야 한다(턱 끝에 걸치거나 위가 막힌 곳 제외). 모든 모서리가 광장 밖이어야 한다.
        float hx = Mathf.Max(0.05f, local.extents.x - 0.05f), hz = Mathf.Max(0.05f, local.extents.z - 0.05f);
        foreach (Vector2 sign in new[] { new Vector2(-1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(1f, 1f) })
        {
            Vector3 corner = new Vector3(spot.x, 0f, spot.y) + rotation * new Vector3(sign.x * hx, 0f, sign.y * hz);
            if (new Vector2(corner.x - c.x, corner.z - c.z).magnitude < PropPlazaClearance)
            {
                reason = "inside the plaza clearance";
                return false;
            }
            if (!FirstHit(new Vector3(corner.x, surface + 2.5f, corner.z), Vector3.down, 3.5f, out RaycastHit hit)
                || Mathf.Abs(hit.point.y - surface) > 0.12f)
            {
                reason = "footprint not supported";
                return false;
            }
        }

        var size = new Vector3(local.size.x, local.size.y, local.size.z);
        var boxCenter = new Vector3(spot.x, surface + 0.06f + size.y * 0.5f, spot.y);
        var half = new Vector3(Mathf.Max(0.02f, size.x * 0.5f - 0.05f), Mathf.Max(0.02f, size.y * 0.5f - 0.03f), Mathf.Max(0.02f, size.z * 0.5f - 0.05f));
        if (Physics.CheckBox(boxCenter, half, rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            reason = "overlaps a collider";
            return false;
        }

        world = RotatedBounds(boxCenter, half, rotation);
        foreach (Bounds other in placed)
            if (other.Intersects(world)) { reason = "overlaps another CC0 prop"; return false; }
        foreach (Bounds other in obstacles)
            if (other.max.y > surface + 0.1f && other.Intersects(world)) { reason = "overlaps a renderer without a collider"; return false; }

        reason = null;
        return true;
    }

    private static bool TryPlaceWallProp(Transform group, WallProp prop, Vector3 c, List<Bounds> placed)
    {
        GameObject prefab = LoadArenaProp(prop.Prefab);
        if (prefab == null) return false;
        Bounds local = LocalBounds(prefab);
        string reason = "no candidate";
        int steps = Mathf.FloorToInt(prop.SlideRange / 0.5f + 1e-4f);
        for (int i = 0; i <= steps * 2; i++)
        {
            float shift = ((i + 1) / 2) * 0.5f * (i % 2 == 0 ? 1f : -1f);
            Vector3 from = prop.From + prop.Slide * shift;
            if (!FitsOnWall(from, prop, local, c, placed, out Vector3 center, out Quaternion rotation, out Bounds world, out reason))
                continue;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            instance.transform.SetPositionAndRotation(center - rotation * local.center, rotation);
            placed.Add(world);
            Vector3 normal = rotation * Vector3.right;
            Debug.Log($"[RebootHub] CC0 {prefab.name} on wall at ({center.x:0.##}, {center.y:0.##}, {center.z:0.##}) facing ({normal.x:0.##}, {normal.z:0.##})");
            return true;
        }
        Debug.LogWarning($"[RebootHub] CC0 {prefab.name} skipped from {prop.From} toward {prop.Direction}: {reason}");
        return false;
    }

    private static bool FitsOnWall(Vector3 from, WallProp prop, Bounds local, Vector3 c, List<Bounds> placed,
        out Vector3 center, out Quaternion rotation, out Bounds world, out string reason)
    {
        center = default;
        rotation = Quaternion.identity;
        world = default;
        if (!FirstHit(from, prop.Direction, prop.MaxDistance, out RaycastHit hit))
        {
            reason = "no wall";
            return false;
        }
        Vector3 normal = hit.normal;
        normal.y = 0f;
        if (normal.sqrMagnitude < 0.8f)
        {
            reason = $"surface at {hit.point} is not a vertical wall";
            return false;
        }
        normal.Normalize();

        // 간판 메시는 로컬 YZ 평면(두께 0)이다. 로컬 X를 벽의 법선에 맞춘다.
        rotation = Quaternion.LookRotation(Vector3.Cross(normal, Vector3.up), Vector3.up);
        Vector3 along = rotation * Vector3.forward;
        center = hit.point + normal * 0.06f;
        float halfW = local.extents.z, halfH = local.extents.y;

        foreach (Vector2 sign in new[] { new Vector2(-1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(1f, 1f) })
        {
            Vector3 corner = hit.point + along * (sign.x * halfW) + Vector3.up * (sign.y * halfH);
            if (!FirstHit(corner + normal * 0.5f, -normal, 0.8f, out RaycastHit wall) || Mathf.Abs(wall.distance - 0.5f) > 0.15f)
            {
                reason = $"wall at {hit.point} is not flat or wide enough";
                return false;
            }
        }
        if (Physics.CheckBox(center + normal * 0.1f, new Vector3(0.08f, halfH, halfW), rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            reason = $"something sticks out of the wall at {hit.point}";
            return false;
        }
        if (new Vector2(center.x - c.x, center.z - c.z).magnitude < PropPlazaClearance)
        {
            reason = $"wall at {hit.point} is inside the plaza clearance";
            return false;
        }
        world = RotatedBounds(center, new Vector3(0.05f, halfH, halfW), rotation);
        foreach (Bounds other in placed)
            if (other.Intersects(world))
            {
                reason = $"overlaps another CC0 prop at {hit.point}";
                return false;
            }
        reason = null;
        return true;
    }

    private static GameObject LoadArenaProp(string relativePath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ArenaPropsRoot}/{relativePath}.prefab");
        if (prefab == null)
            Debug.LogWarning($"[RebootHub] CC0 prefab missing: {relativePath}");
        return prefab;
    }

    // 회전 없이 원점에 놓았을 때의 렌더러 경계(프리팹 기준점 기준).
    private static Bounds LocalBounds(GameObject prefab)
    {
        var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Renderer[] renderers = probe.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        Object.DestroyImmediate(probe);
        return bounds;
    }

    // 충돌체가 없는 장식(허브 등)과 겹치지 않도록, 맵·환경 렌더러의 경계를 모은다. 캐릭터, 아주 큰 메시(바닥 등)는 뺀다.
    private static List<Bounds> SceneRendererBounds(Transform envRoot)
    {
        var list = new List<Bounds>();
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (r.GetComponentInParent<CharacterController>() != null || r.GetComponentInParent<EnemyAgent>() != null) continue;
            if (r.transform.IsChildOf(envRoot) && r.transform.parent != null && r.transform.parent.name == ArenaPropsGroupName) continue;
            Bounds b = r.bounds;
            if (b.size.x > 40f || b.size.z > 40f) continue;
            list.Add(b);
        }
        return list;
    }

    private static Bounds RotatedBounds(Vector3 center, Vector3 half, Quaternion rotation)
    {
        var bounds = new Bounds(center, Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
            bounds.Encapsulate(center + rotation * corner);
        }
        return bounds;
    }

    // 요청 위치에서 가까운 순서로 step 간격의 후보를 낸다(반경 radius 안).
    private static IEnumerable<Vector2> SearchOffsets(float radius, float step)
    {
        var offsets = new List<Vector2>();
        int n = Mathf.CeilToInt(radius / step);
        for (int ix = -n; ix <= n; ix++)
            for (int iz = -n; iz <= n; iz++)
            {
                var offset = new Vector2(ix * step, iz * step);
                if (offset.magnitude <= radius + 1e-4f) offsets.Add(offset);
            }
        offsets.Sort((a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));
        return offsets;
    }
}
