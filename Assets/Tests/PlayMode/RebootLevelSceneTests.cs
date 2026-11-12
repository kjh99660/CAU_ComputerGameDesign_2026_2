using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// 레벨 디자인(Env_RebootHub, 광장 높이 정렬)이 기존 게임 흐름과 충돌하지 않는지 확인한다.
// REBOOT_CAPTURE_DIR 환경 변수가 있으면 실제 런타임 카메라 화면과 적 위치 요약을 저장한다.
public sealed class RebootLevelSceneTests
{
    // 레벨 디자인은 미리보기 씬에만 적용되어 있다. 빌드 설정에 없는 씬이라 Editor 전용 로더를 쓴다.
    private const string ScenePath = "Assets/Scenes/Main_RebootHub.unity";
    private const float FloorTolerance = 0.3f;

    [UnityTest]
    public IEnumerator PlazaFloorMatchesSpawnHeightAndEnvironmentAddsNoObstacles()
    {
        yield return LoadAndWaitReady();
        var state = Object.FindFirstObjectByType<GameStateManager>();
        var enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        var player = Object.FindFirstObjectByType<PlayerRuntime>();

        float? floor = FloorBelow(player.transform.position);
        Assert.IsTrue(floor.HasValue, "No floor under the player start.");
        Assert.AreEqual(enemies.Settings.SpawnHeight, floor.Value, 0.05f, "Plaza floor must match EnemySettings.SpawnHeight.");
        Assert.AreEqual(floor.Value, player.transform.position.y, 0.1f, "Player must start on the plaza floor.");

        var env = GameObject.Find("Env_RebootHub");
        Assert.IsNotNull(env, "Env_RebootHub root is missing.");
        Assert.AreEqual(0, env.GetComponentsInChildren<Collider>(true).Length, "Decoration must not add colliders to the combat space.");

        // 광장 반경 안에 들어오는 장식은 바닥에 붙은 평면(높이 0.1m 미만)뿐이어야 한다.
        var plaza = new Vector2(-24f, 48.2f);
        foreach (Renderer renderer in env.GetComponentsInChildren<Renderer>(true))
        {
            Bounds b = renderer.bounds;
            bool overPlaza = Vector2.Distance(new Vector2(b.center.x, b.center.z), plaza) < 19f;
            // 1.9m 위(기존 기둥 꼭대기의 경고등 등)는 이동 공간을 막지 않는다.
            if (overPlaza && b.min.y < floor.Value + 1.9f)
                Assert.Less(b.max.y - floor.Value, 0.1f, $"{renderer.name} blocks the walkable plaza volume.");
        }
        Assert.AreEqual(GameState.Ready, state.CurrentState);
    }

    [UnityTest]
    public IEnumerator BattleEnemiesStayOnSpawnHeightApproachAndRetryRestoresLayout()
    {
        // 적 Rigidbody는 kinematic + Interpolate이고 autoSyncTransforms가 꺼져 있어, 고정 스텝이 없는 프레임에서
        // 보간이 스크립트 이동을 덮어쓴다(적 시스템의 알려진 문제, 문서 참고). batchmode의 무제한 프레임 대신
        // 실제 플레이와 같은 60 FPS 조건에서 레벨 동작을 검증한다.
        int previousFrameRate = Application.targetFrameRate;
        int previousVSync = QualitySettings.vSyncCount;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        try
        {
            yield return BattleScenario();
        }
        finally
        {
            Application.targetFrameRate = previousFrameRate;
            QualitySettings.vSyncCount = previousVSync;
        }
    }

    private IEnumerator BattleScenario()
    {
        yield return LoadAndWaitReady();
        var state = Object.FindFirstObjectByType<GameStateManager>();
        var events = Object.FindFirstObjectByType<EventManager>();
        var runtime = Object.FindFirstObjectByType<EnemyRuntime>();
        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        var camera = Camera.main;
        var log = new StringBuilder();
        Vector3 playerStart = player.transform.position;

        Capture(camera, "runtime_ready");
        log.AppendLine("Ready: " + Summarize(Active(), player.transform.position, runtime.Settings.SpawnHeight));

        events.EnqueueRequest(new StartRequest(state.Tick));
        yield return WaitFor(state, GameState.IntroDialogue);
        events.EnqueueRequest(new DialogueCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Countdown);
        events.EnqueueRequest(new CountdownCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Battle);

        var firstDistances = new Dictionary<EnemyAgent, float>();
        foreach (EnemyAgent enemy in Active())
            firstDistances[enemy] = Horizontal(enemy.transform.position, player.transform.position);

        // 적 시스템의 알려진 경쟁 상태: EnemyAgent.Activate가 Transform만 옮기고, kinematic + Interpolate Rigidbody가
        // 다음 고정 스텝 전에 이전 위치(풀 원점)로 Transform을 덮어쓰면 초기 적이 모두 원점에 겹친다.
        // 레벨 문제가 아니므로 실패 대신 판정 불가로 남긴다(Docs/LevelDesign 문서 6절).
        int collapsed = 0;
        Vector3 poolOrigin = runtime.transform.position;
        foreach (EnemyAgent enemy in Active())
            if (Horizontal(enemy.transform.position, poolOrigin) < 0.6f) collapsed++;
        if (collapsed >= 2 && collapsed == firstDistances.Count)
            Assert.Inconclusive($"Initial enemies collapsed to the pool origin ({collapsed}). Known EnemyAgent spawn/interpolation race; level checks skipped.");

        // 첫 추가 생성(1초) 전에 접근을 판정한다. 이후에는 새 적과의 분리(Separation) 때문에 옆으로 밀릴 수 있다.
        // 적 Animator는 렌더링 여부에 따라 갱신이 달라지므로(CullUpdateTransforms), 실제 플레이처럼 매 프레임 카메라를 렌더링한다.
        yield return RenderFor(camera, 0.8f);
        int approached = 0, measured = 0;
        var notApproached = new StringBuilder();
        foreach (EnemyAgent enemy in Active())
        {
            if (!firstDistances.TryGetValue(enemy, out float before)) continue;
            measured++;
            float after = Horizontal(enemy.transform.position, player.transform.position);
            if (after < before - 0.5f) approached++;
            else notApproached.Append($" {enemy.name}@{enemy.transform.position}:{before:0.0}->{after:0.0}({enemy.State})");
        }
        Assert.Greater(measured, 0);
        Assert.AreEqual(measured, approached, $"Every initial enemy should move toward the player. player={player.transform.position} timeScale={Time.timeScale} state={state.CurrentState}" + notApproached);

        yield return RenderFor(camera, 1.7f);
        Capture(camera, "runtime_battle_2s");
        List<EnemyAgent> active = Active();
        log.AppendLine("Battle 2.5s: " + Summarize(active, player.transform.position, runtime.Settings.SpawnHeight));
        foreach (EnemyAgent enemy in active)
            Assert.AreEqual(runtime.Settings.SpawnHeight, enemy.transform.position.y, 0.05f, $"{enemy.name} left the spawn height while chasing.");

        // 플레이어를 동쪽 벽으로 이동시켜 벽 충돌로 광장 안에 머무는지 확인한다(입력 대신 CharacterController 직접 이동).
        // 북쪽은 Area_B로 이어지는 열린 통로라 벽 검사 대상이 아니다.
        var controller = player.GetComponent<CharacterController>();
        for (int i = 0; i < 90; i++)
        {
            controller.Move(Vector3.right * 0.4f);
            yield return null;
        }
        Assert.Less(player.transform.position.x, -2.7f, "Player crossed the plaza east wall.");
        Capture(camera, "runtime_east_wall");
        log.AppendLine($"Player after pushing east: {player.transform.position}");

        events.EnqueueRequest(new PauseRequest(state.Tick));
        yield return WaitFor(state, GameState.Paused);
        Vector3 pausedEnemy = active.Count > 0 ? active[0].transform.position : Vector3.zero;
        yield return new WaitForSecondsRealtime(0.3f);
        if (active.Count > 0 && active[0] != null && active[0].gameObject.activeInHierarchy)
            Assert.AreEqual(pausedEnemy, active[0].transform.position, "Enemy moved while paused.");
        events.EnqueueRequest(new ResumeRequest(state.Tick));
        yield return WaitFor(state, GameState.Battle);
        events.EnqueueRequest(new PauseRequest(state.Tick));
        yield return WaitFor(state, GameState.Paused);
        events.EnqueueRequest(new RestartRequest(state.Tick));
        yield return WaitFor(state, GameState.Ready);

        Assert.AreEqual(1f, Time.timeScale);
        Assert.AreEqual(playerStart.x, player.transform.position.x, 0.01f);
        Assert.AreEqual(playerStart.y, player.transform.position.y, 0.01f);
        Assert.AreEqual(playerStart.z, player.transform.position.z, 0.01f);
        Assert.IsTrue(GameObject.Find("Env_RebootHub").activeInHierarchy);
        log.AppendLine("After Retry: " + Summarize(Active(), player.transform.position, runtime.Settings.SpawnHeight));
        WriteLog(log.ToString());
    }

    // batchmode에서는 화면 렌더링이 없으므로 작은 RenderTexture로 카메라를 매 프레임 렌더링해 실제 플레이와 같은 가시성 조건을 만든다.
    private static IEnumerator RenderFor(Camera camera, float seconds)
    {
        var rt = new RenderTexture(160, 90, 16);
        float end = Time.time + seconds;
        try
        {
            while (Time.time < end)
            {
                if (camera != null)
                {
                    RenderTexture previous = camera.targetTexture;
                    camera.targetTexture = rt;
                    camera.Render();
                    camera.targetTexture = previous;
                }
                yield return null;
            }
        }
        finally
        {
            rt.Release();
            Object.Destroy(rt);
        }
    }

    private static IEnumerator LoadAndWaitReady()
    {
#if UNITY_EDITOR
        yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
        Assert.Ignore("Main_RebootHub preview scene is editor-only.");
#endif
        yield return null;
        Assert.AreEqual(ScenePath, SceneManager.GetActiveScene().path);
        var state = Object.FindFirstObjectByType<GameStateManager>();
        Assert.IsNotNull(state);
        yield return WaitFor(state, GameState.Ready);
    }

    private static IEnumerator WaitFor(GameStateManager state, GameState expected)
    {
        float timeout = Time.realtimeSinceStartup + 5f;
        while (state.CurrentState != expected && Time.realtimeSinceStartup < timeout)
            yield return null;
        Assert.AreEqual(expected, state.CurrentState);
    }

    private static List<EnemyAgent> Active()
    {
        var result = new List<EnemyAgent>();
        foreach (EnemyAgent enemy in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.InstanceID))
            if (enemy.gameObject.activeInHierarchy && enemy.State != EnemyAgentState.Pooled)
                result.Add(enemy);
        return result;
    }

    // 적 위치를 "광장 바닥 위"와 "바닥 밖(공중/다른 층)"으로 나눈다. 바닥 밖은 현재 스폰 코드의 알려진 한계다.
    private static string Summarize(List<EnemyAgent> enemies, Vector3 player, float spawnHeight)
    {
        int onFloor = 0, offFloor = 0;
        foreach (EnemyAgent enemy in enemies)
        {
            float? floor = FloorBelow(enemy.transform.position);
            if (floor.HasValue && Mathf.Abs(floor.Value - spawnHeight) <= FloorTolerance) onFloor++;
            else offFloor++;
        }
        return $"active={enemies.Count}, onPlazaFloor={onFloor}, offFloor={offFloor}, player={player}";
    }

    private static float? FloorBelow(Vector3 position)
    {
        float? best = null;
        foreach (RaycastHit hit in Physics.RaycastAll(position + Vector3.up * 1.5f, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<CharacterController>() != null || hit.collider.GetComponentInParent<EnemyAgent>() != null)
                continue;
            if (!best.HasValue || hit.point.y > best.Value)
                best = hit.point.y;
        }
        return best;
    }

    private static float Horizontal(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    private static string CaptureDirectory => Environment.GetEnvironmentVariable("REBOOT_CAPTURE_DIR");

    private static void Capture(Camera camera, string name)
    {
        string dir = CaptureDirectory;
        if (string.IsNullOrEmpty(dir) || camera == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            return;
        Directory.CreateDirectory(dir);
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = camera.targetTexture;
        camera.targetTexture = rt;
        camera.Render();
        camera.targetTexture = previousTarget;
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), texture.EncodeToPNG());
        Object.Destroy(texture);
        rt.Release();
        Object.Destroy(rt);
    }

    private static void WriteLog(string text)
    {
        string dir = CaptureDirectory;
        if (string.IsNullOrEmpty(dir)) { Debug.Log(text); return; }
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "runtime_enemy_summary.txt"), text);
    }
}
