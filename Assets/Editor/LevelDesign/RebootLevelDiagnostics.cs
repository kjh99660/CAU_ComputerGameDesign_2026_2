using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// 레벨 디자인 검증용 Editor 도구.
// Scene을 저장하지 않고 계층 요약, 적 스폰 위치/접근 경로 분석, 고정 시점 캡처를 만든다.
// 배치 실행: Unity -batchmode -projectPath <path> -executeMethod RebootLevelDiagnostics.RunAll -rebootOut <dir> -rebootTag before
public static class RebootLevelDiagnostics
{
    // 기본은 미리보기 씬. -rebootScene main 으로 팀 공용 Main.unity를 진단할 수 있다(저장하지 않음).
    private static string ScenePath => GetArg("-rebootScene") == "main"
        ? RebootHubEnvironmentBuilder.MainScenePath
        : RebootHubEnvironmentBuilder.PreviewScenePath;
    private const string PlayerPrefabPath = "Assets/Prefabs/Character/Player.prefab";

    // EnemyRuntime.SpawnOne과 같은 분포(플레이어 기준 반경 15~25m, 절대 높이 SpawnHeight)를 재현한다.
    private const int SpawnSamplesPerPosition = 64;
    private const float FloorTolerance = 0.35f;
    private const float EnemyRadius = 0.45f;
    private const float EnemyHeight = 1.8f;

    [MenuItem("Tools/REBOOT Level/Run Diagnostics (no save)")]
    public static void RunFromMenu()
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Docs/LevelDesign/Diagnostics"));
        Run(dir, "editor");
    }

    public static void RunAll()
    {
        string dir = GetArg("-rebootOut") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../Docs/LevelDesign/Diagnostics"));
        string tag = GetArg("-rebootTag") ?? "run";
        try
        {
            Run(dir, tag);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void Run(string outputDirectory, string tag)
    {
        Directory.CreateDirectory(outputDirectory);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Physics.SyncTransforms();

        var report = new StringBuilder();
        report.AppendLine($"# REBOOT level diagnostics ({tag})");
        report.AppendLine($"Scene: {ScenePath}");
        report.AppendLine($"Unity: {Application.unityVersion}, Graphics: {SystemInfo.graphicsDeviceType}");
        report.AppendLine();

        DumpHierarchy(scene, report);
        DumpRendering(report);
        DumpEnvironmentStats(report);

        Transform spawn = FindSpawnPoint();
        var settings = FindEnemySettings();
        float minDistance = settings != null ? settings.MinimumSpawnDistance : 15f;
        float maxDistance = settings != null ? settings.MaximumSpawnDistance : 25f;
        float spawnHeight = settings != null ? settings.SpawnHeight : 0f;

        if (spawn != null)
        {
            report.AppendLine($"## Spawn analysis (enemy spawn {minDistance}-{maxDistance}m, absolute y={spawnHeight})");
            report.AppendLine($"PlayerSpawnPoint world: {Fmt(spawn.position)} yaw {spawn.eulerAngles.y:0.#}");
            float? floor = FloorHeightAt(spawn.position + Vector3.up * 0.5f, 3f);
            report.AppendLine($"Floor under player spawn: {(floor.HasValue ? floor.Value.ToString("0.00") : "none")}");
            List<Vector3> reachable = ReachableArea(spawn.position, 1.5f, 80f);
            Bounds reach = BoundsOf(reachable, spawn.position);
            report.AppendLine($"Player-reachable sample cells (1.5m, collision only, no gravity): {reachable.Count}, bounds {Fmt(reach.min)} .. {Fmt(reach.max)}");
            AnalyzeSpawns(report, "From PlayerSpawnPoint", new List<Vector3> { spawn.position }, minDistance, maxDistance, spawnHeight);
            AnalyzeSpawns(report, "From all reachable cells", reachable, minDistance, maxDistance, spawnHeight);
            report.AppendLine();
            report.AppendLine("### Candidate fix simulation (not applied to runtime code)");
            report.AppendLine("Rejection sampling: keep the same 15-25m ring, accept only points on the floor at spawn height and not inside a collider, retry up to N times.");
            foreach (int tries in new[] { 1, 4, 10 })
                SimulateRejection(report, reachable, minDistance, maxDistance, spawnHeight, tries);
            SimulateRejection(report, reachable, Mathf.Min(minDistance, 8f), maxDistance, spawnHeight, 10);
            report.AppendLine();
        }
        else
        {
            report.AppendLine("PlayerSpawnPoint not found.");
        }

        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            Capture(outputDirectory, tag, spawn, report);
        else
            report.AppendLine("Captures skipped: no graphics device (-nographics).");

        File.WriteAllText(Path.Combine(outputDirectory, $"report_{tag}.md"), report.ToString());
        Debug.Log($"[RebootLevelDiagnostics] wrote {outputDirectory}/report_{tag}.md");

        // 캡처용 임시 오브젝트가 Scene에 남지 않도록 저장 없이 다시 연다.
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    private static void DumpHierarchy(UnityEngine.SceneManagement.Scene scene, StringBuilder report)
    {
        report.AppendLine("## Root objects");
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Bounds? bounds = RendererBounds(root);
            report.AppendLine($"- {root.name} active={root.activeSelf} children={root.transform.childCount} renderers={root.GetComponentsInChildren<Renderer>(true).Length} colliders={root.GetComponentsInChildren<Collider>(true).Length} bounds={(bounds.HasValue ? Fmt(bounds.Value.min) + ".." + Fmt(bounds.Value.max) : "-")}");
            if (root.name == "Map" || root.name.StartsWith("Env_", StringComparison.Ordinal))
            {
                foreach (Transform child in root.transform)
                {
                    Bounds? cb = RendererBounds(child.gameObject);
                    report.AppendLine($"  - {child.name} active={child.gameObject.activeSelf} children={child.childCount} colliders={child.GetComponentsInChildren<Collider>(true).Length} bounds={(cb.HasValue ? Fmt(cb.Value.min) + ".." + Fmt(cb.Value.max) : "-")}");
                    foreach (Transform grand in child)
                    {
                        Bounds? gb = RendererBounds(grand.gameObject);
                        report.AppendLine($"    - {grand.name} children={grand.childCount} colliders={grand.GetComponentsInChildren<Collider>(true).Length} bounds={(gb.HasValue ? Fmt(gb.Value.min) + ".." + Fmt(gb.Value.max) : "-")}");
                    }
                }
            }
        }
        report.AppendLine();
    }

    // 새 환경 루트가 추가하는 렌더링 비용의 정적 수치(FPS가 아님).
    private static void DumpEnvironmentStats(StringBuilder report)
    {
        GameObject env = GameObject.Find("Env_RebootHub");
        if (env == null) { report.AppendLine("Env_RebootHub: not present"); report.AppendLine(); return; }
        int renderers = 0, triangles = 0, shadowCasters = 0;
        var materials = new HashSet<Material>();
        foreach (Renderer r in env.GetComponentsInChildren<Renderer>(true))
        {
            renderers++;
            if (r.shadowCastingMode != ShadowCastingMode.Off) shadowCasters++;
            foreach (Material m in r.sharedMaterials) if (m != null) materials.Add(m);
            var filter = r.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) triangles += filter.sharedMesh.triangles.Length / 3;
        }
        int lights = env.GetComponentsInChildren<Light>(true).Length;
        int colliders = env.GetComponentsInChildren<Collider>(true).Length;
        report.AppendLine($"## Env_RebootHub static cost: renderers {renderers} (shadow casters {shadowCasters}), mesh triangles {triangles} (TMP text excluded), unique materials {materials.Count}, realtime lights {lights} (no shadows), colliders {colliders}");
        report.AppendLine();
    }

    private static void DumpRendering(StringBuilder report)
    {
        report.AppendLine("## Rendering");
        report.AppendLine($"Skybox: {(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none")}, ambient mode {RenderSettings.ambientMode}, ambient {RenderSettings.ambientLight}, intensity {RenderSettings.ambientIntensity}");
        report.AppendLine($"Fog: {RenderSettings.fog} {RenderSettings.fogMode} color {RenderSettings.fogColor} density {RenderSettings.fogDensity} start {RenderSettings.fogStartDistance} end {RenderSettings.fogEndDistance}");
        report.AppendLine($"Pipeline: {(GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "builtin")}");
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.AppendLine($"- Light {light.name} {light.type} color {light.color} intensity {light.intensity} shadows {light.shadows} rot {Fmt(light.transform.eulerAngles)}");
        foreach (Volume volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            report.AppendLine($"- Volume {volume.name} global={volume.isGlobal} profile={(volume.sharedProfile != null ? AssetDatabase.GetAssetPath(volume.sharedProfile) : "none")}");
            if (volume.sharedProfile != null)
                foreach (var component in volume.sharedProfile.components)
                    report.AppendLine($"  - {component.GetType().Name} active={component.active}");
        }
        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.AppendLine($"- Camera {camera.name} fov {camera.fieldOfView} near {camera.nearClipPlane} far {camera.farClipPlane} clear {camera.clearFlags}");
        report.AppendLine();
    }

    private static void AnalyzeSpawns(StringBuilder report, string label, List<Vector3> origins, float minDistance, float maxDistance, float spawnHeight)
    {
        int total = 0, onFloor = 0, offMap = 0, otherLevel = 0, insideSolid = 0, pathBlocked = 0;
        var random = new System.Random(7919);
        int stride = Mathf.Max(1, origins.Count / 200);
        for (int o = 0; o < origins.Count; o += stride)
        {
            Vector3 origin = origins[o];
            for (int i = 0; i < SpawnSamplesPerPosition; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2d;
                float distance = Mathf.Lerp(minDistance, maxDistance, (float)random.NextDouble());
                var position = new Vector3(origin.x + Mathf.Cos((float)angle) * distance, spawnHeight, origin.z + Mathf.Sin((float)angle) * distance);
                total++;
                float? floor = FloorHeightAt(position + Vector3.up * 0.5f, 60f);
                bool floorOk = floor.HasValue && Mathf.Abs(floor.Value - spawnHeight) <= FloorTolerance;
                if (!floor.HasValue) offMap++;
                else if (!floorOk) otherLevel++;
                else onFloor++;
                if (IsInsideSolid(position)) insideSolid++;
                if (IsPathBlocked(position, origin)) pathBlocked++;
            }
        }
        report.AppendLine($"### {label}: samples {total}");
        report.AppendLine($"- on floor at spawn height: {Pct(onFloor, total)}");
        report.AppendLine($"- no floor (outside map): {Pct(offMap, total)}");
        report.AppendLine($"- floor at another height (other level / under geometry): {Pct(otherLevel, total)}");
        report.AppendLine($"- overlapping solid collider at spawn: {Pct(insideSolid, total)}");
        report.AppendLine($"- straight path to player crosses solid collider (enemy clips through, AI has no avoidance): {Pct(pathBlocked, total)}");
    }

    private static void SimulateRejection(StringBuilder report, List<Vector3> origins, float minDistance, float maxDistance, float spawnHeight, int tries)
    {
        var random = new System.Random(104729);
        int total = 0, success = 0;
        int stride = Mathf.Max(1, origins.Count / 200);
        for (int o = 0; o < origins.Count; o += stride)
        for (int k = 0; k < 16; k++)
        {
            total++;
            for (int t = 0; t < tries; t++)
            {
                double angle = random.NextDouble() * Math.PI * 2d;
                float distance = Mathf.Lerp(minDistance, maxDistance, (float)random.NextDouble());
                var p = new Vector3(origins[o].x + Mathf.Cos((float)angle) * distance, spawnHeight, origins[o].z + Mathf.Sin((float)angle) * distance);
                float? floor = FloorHeightAt(p + Vector3.up * 0.5f, 60f);
                if (floor.HasValue && Mathf.Abs(floor.Value - spawnHeight) <= FloorTolerance && !IsInsideSolid(p))
                {
                    success++;
                    break;
                }
            }
        }
        report.AppendLine($"- ring {minDistance:0}-{maxDistance:0}m, {tries} tries: valid spawn found {Pct(success, total)} (rest would need a fallback such as an entry gate)");
    }

    private static readonly Collider[] OverlapBuffer = new Collider[32];

    // 플레이어/적 자신의 캡슐은 장애물로 세지 않는다.
    private static bool IsActorCollider(Collider c) =>
        c.GetComponentInParent<CharacterController>() != null || c.GetComponentInParent<EnemyAgent>() != null;

    private static bool IsInsideSolid(Vector3 position)
    {
        Vector3 bottom = position + Vector3.up * (EnemyRadius + 0.25f);
        Vector3 top = position + Vector3.up * (EnemyHeight - EnemyRadius);
        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, EnemyRadius, OverlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (!IsActorCollider(OverlapBuffer[i])) return true;
        return false;
    }

    private static bool IsPathBlocked(Vector3 from, Vector3 to)
    {
        Vector3 a = new Vector3(from.x, from.y + 1f, from.z);
        Vector3 b = new Vector3(to.x, from.y + 1f, to.z);
        Vector3 d = b - a;
        foreach (RaycastHit hit in Physics.RaycastAll(a, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
            if (!IsActorCollider(hit.collider)) return true;
        return false;
    }

    // CharacterController는 중력 없이 수평으로만 이동하므로, 플레이어 높이에서 충돌만으로 막히는 영역을 근사한다.
    private static List<Vector3> ReachableArea(Vector3 start, float cell, float limit)
    {
        var result = new List<Vector3>();
        var visited = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(Vector2Int.zero);
        visited.Add(Vector2Int.zero);
        int maxCells = Mathf.CeilToInt(limit / cell);
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (queue.Count > 0 && result.Count < 20000)
        {
            Vector2Int c = queue.Dequeue();
            Vector3 p = start + new Vector3(c.x * cell, 0f, c.y * cell);
            result.Add(p);
            foreach (Vector2Int d in dirs)
            {
                Vector2Int n = c + d;
                if (Mathf.Abs(n.x) > maxCells || Mathf.Abs(n.y) > maxCells || visited.Contains(n))
                    continue;
                Vector3 np = start + new Vector3(n.x * cell, 0f, n.y * cell);
                Vector3 bottom = p + Vector3.up * 0.6f;
                Vector3 top = p + Vector3.up * 1.4f;
                bool blocked = false;
                foreach (RaycastHit hit in Physics.CapsuleCastAll(bottom, top, 0.4f, (np - p).normalized, cell, ~0, QueryTriggerInteraction.Ignore))
                    if (!IsActorCollider(hit.collider)) { blocked = true; break; }
                if (blocked)
                    continue;
                visited.Add(n);
                queue.Enqueue(n);
            }
        }
        return result;
    }

    private static float? FloorHeightAt(Vector3 probe, float maxDown)
    {
        Vector3 origin = new Vector3(probe.x, probe.y + 40f, probe.z);
        float? best = null;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, 40f + maxDown, ~0, QueryTriggerInteraction.Ignore))
            if (!IsActorCollider(hit.collider) && (!best.HasValue || hit.point.y > best.Value))
                best = hit.point.y;
        return best;
    }

    // 위에서 아래로 Raycast한 첫 표면 높이를 색으로 기록한다. 1px = 0.5m, 중심은 PlayerSpawnPoint.
    public static void HeightMap()
    {
        string dir = GetArg("-rebootOut") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../Docs/LevelDesign/Diagnostics"));
        string tag = GetArg("-rebootTag") ?? "run";
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Physics.SyncTransforms();
        Transform spawn = FindSpawnPoint();
        Vector3 c = spawn != null ? spawn.position : Vector3.zero;
        const int size = 200;
        const float step = 0.5f;
        var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
        var histogram = new SortedDictionary<float, int>();
        var text = new StringBuilder();
        text.AppendLine($"center {Fmt(c)}; rows north(+z) first; 2m per char; '.' none, digits = floor height bucket");
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector3 p = c + new Vector3((x - size / 2) * step, 0f, (y - size / 2) * step);
            float? h = FloorHeightAt(new Vector3(p.x, 60f, p.z), 120f);
            Color color = Color.black;
            if (h.HasValue)
            {
                float t = Mathf.InverseLerp(-6f, 6f, h.Value);
                color = Color.HSVToRGB(Mathf.Lerp(0.66f, 0f, t), 0.8f, 0.9f);
                float bucket = Mathf.Round(h.Value * 2f) / 2f;
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(c.x, c.z)) <= 50f)
                    histogram[bucket] = histogram.TryGetValue(bucket, out int n) ? n + 1 : 1;
            }
            texture.SetPixel(x, y, color);
        }
        texture.Apply();
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, $"{tag}_heightmap.png"), texture.EncodeToPNG());
        for (int gz = 24; gz >= -24; gz--)
        {
            var line = new StringBuilder();
            for (int gx = -24; gx <= 24; gx++)
            {
                float? h = FloorHeightAt(new Vector3(c.x + gx * 2f, 60f, c.z + gz * 2f), 120f);
                line.Append(!h.HasValue ? '.' : h.Value < -1.5f ? 'L' : h.Value < -0.25f ? 'l' : h.Value < 0.25f ? '0' : h.Value < 1.5f ? 'h' : 'H');
            }
            text.AppendLine(line.ToString());
        }
        text.AppendLine("Legend: L<-1.5, l -1.5..-0.25, 0 = -0.25..0.25, h 0.25..1.5, H>1.5");
        text.AppendLine("Height histogram within 50m (0.5m buckets):");
        foreach (var kv in histogram) text.AppendLine($"  {kv.Key:0.0}: {kv.Value}");
        File.WriteAllText(Path.Combine(dir, $"{tag}_heightmap.txt"), text.ToString());
        EditorApplication.Exit(0);
    }

    private static void Capture(string outputDirectory, string tag, Transform spawn, StringBuilder report)
    {
        // 플레이어와 적을 임시 배치해 실제 전투 시점의 명도 대비를 확인한다. Scene은 저장하지 않는다.
        GameObject player = null;
        var enemies = new List<GameObject>();
        Vector3 center = spawn != null ? spawn.position : Vector3.zero;
        float yaw = spawn != null ? spawn.eulerAngles.y : 0f;
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (Object.FindFirstObjectByType<PlayerRuntime>() == null && playerPrefab != null)
        {
            player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.transform.SetPositionAndRotation(center, Quaternion.Euler(0f, yaw, 0f));
        }
        var enemyRuntime = Object.FindFirstObjectByType<EnemyRuntime>();
        GameObject enemyPrefab = null;
        if (enemyRuntime != null)
        {
            var so = new SerializedObject(enemyRuntime);
            var prefabRef = so.FindProperty("enemyPrefab").objectReferenceValue as Component;
            if (prefabRef != null) enemyPrefab = prefabRef.gameObject;
        }
        if (enemyPrefab != null)
        {
            var random = new System.Random(11);
            for (int i = 0; i < 18; i++)
            {
                float a = (float)(random.NextDouble() * Math.PI * 2d);
                float d = 4f + (float)random.NextDouble() * 12f;
                var e = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab);
                Vector3 p = center + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                p.y = 0f;
                e.transform.SetPositionAndRotation(p, Quaternion.LookRotation(new Vector3(center.x - p.x, 0f, center.z - p.z)));
                enemies.Add(e);
            }
        }

        var cameraObject = new GameObject("DiagnosticsCamera");
        var camera = cameraObject.AddComponent<Camera>();
        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            camera.CopyFrom(mainCamera);
            var mainData = mainCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var data = cameraObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (mainData != null)
            {
                data.renderPostProcessing = mainData.renderPostProcessing;
                data.antialiasing = mainData.antialiasing;
                data.volumeLayerMask = mainData.volumeLayerMask;
            }
        }
        camera.targetDisplay = 0;

        // 플레이어 카메라: PlayerRuntime.UpdateCamera와 같은 거리 4m, 높이 1.5m, pitch 20.
        Vector3 target = center + Vector3.up * 1.5f;
        for (int i = 0; i < 4; i++)
        {
            Quaternion rotation = Quaternion.Euler(20f, yaw + i * 90f, 0f);
            camera.orthographic = false;
            camera.fieldOfView = mainCamera != null ? mainCamera.fieldOfView : 60f;
            camera.transform.position = target - rotation * Vector3.forward * 4f;
            camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
            Render(camera, Path.Combine(outputDirectory, $"{tag}_player_view_{i * 90:000}.png"), 1920, 1080);
        }

        // 플레이어가 마우스로 올려다볼 때(최소 pitch -20)의 랜드마크 구도.
        {
            Quaternion rotation = Quaternion.Euler(-14f, yaw, 0f);
            camera.transform.position = target - rotation * Vector3.forward * 4f;
            camera.transform.rotation = rotation;
            camera.fieldOfView = mainCamera != null ? mainCamera.fieldOfView : 60f;
            Render(camera, Path.Combine(outputDirectory, $"{tag}_landmark_view.png"), 1920, 1080);
        }

        // 서쪽 벽에 붙은 플레이어를 동쪽으로 바라볼 때: 현재 카메라는 충돌 처리가 없어 벽 밖에 위치한다.
        {
            Vector3 edge = new Vector3(-43f, center.y, 48f) + Vector3.up * 1.5f;
            Quaternion rotation = Quaternion.Euler(20f, 90f, 0f);
            camera.transform.position = edge - rotation * Vector3.forward * 4f;
            camera.transform.rotation = Quaternion.LookRotation(edge - camera.transform.position, Vector3.up);
            Render(camera, Path.Combine(outputDirectory, $"{tag}_wall_edge_view.png"), 1920, 1080);
        }

        // 높은 대각 시점: 전투 공간과 랜드마크의 관계.
        foreach (float angle in new[] { 0f, 180f })
        {
            Quaternion rotation = Quaternion.Euler(38f, yaw + angle, 0f);
            camera.transform.position = center + Vector3.up * 2f - rotation * Vector3.forward * 46f;
            camera.transform.rotation = rotation;
            camera.fieldOfView = 55f;
            Render(camera, Path.Combine(outputDirectory, $"{tag}_overview_{angle:000}.png"), 1920, 1080);
        }

        // 정사영 평면도: 스폰 링(15/25m)을 함께 확인한다.
        camera.orthographic = true;
        camera.orthographicSize = 45f;
        camera.transform.position = center + Vector3.up * 120f;
        camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        camera.farClipPlane = 400f;
        var rings = new List<GameObject> { MakeRing(center, 15f, Color.yellow), MakeRing(center, 25f, Color.red) };
        Render(camera, Path.Combine(outputDirectory, $"{tag}_plan.png"), 1600, 1600);
        foreach (GameObject ring in rings) Object.DestroyImmediate(ring);

        Object.DestroyImmediate(cameraObject);
        if (player != null) Object.DestroyImmediate(player);
        foreach (GameObject e in enemies) Object.DestroyImmediate(e);
        report.AppendLine($"Captures written with tag '{tag}'.");
    }

    private static GameObject MakeRing(Vector3 center, float radius, Color color)
    {
        var go = new GameObject($"DiagRing_{radius}");
        var line = go.AddComponent<LineRenderer>();
        line.loop = true;
        line.useWorldSpace = true;
        line.positionCount = 96;
        line.widthMultiplier = 0.35f;
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        line.sharedMaterial = new Material(shader) { color = color };
        for (int i = 0; i < 96; i++)
        {
            float a = i / 96f * Mathf.PI * 2f;
            line.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 30f, Mathf.Sin(a) * radius));
        }
        return go;
    }

    private static void Render(Camera camera, string path, int width, int height)
    {
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        camera.targetTexture = null;
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        rt.Release();
        Object.DestroyImmediate(rt);
    }

    private static Transform FindSpawnPoint()
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == "PlayerSpawnPoint")
                return t;
        return null;
    }

    private static EnemySettings FindEnemySettings()
    {
        var runtime = Object.FindFirstObjectByType<EnemyRuntime>();
        return runtime != null ? runtime.Settings : null;
    }

    private static Bounds? RendererBounds(GameObject root)
    {
        Bounds? result = null;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (result.HasValue) { Bounds b = result.Value; b.Encapsulate(r.bounds); result = b; }
            else result = r.bounds;
        }
        return result;
    }

    private static Bounds BoundsOf(List<Vector3> points, Vector3 fallback)
    {
        var b = new Bounds(fallback, Vector3.zero);
        foreach (Vector3 p in points) b.Encapsulate(p);
        return b;
    }

    private static string Pct(int n, int total) => total == 0 ? "0" : $"{n} ({100f * n / total:0.0}%)";
    private static string Fmt(Vector3 v) => $"({v.x:0.#}, {v.y:0.#}, {v.z:0.#})";

    private static string GetArg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        return null;
    }
}
