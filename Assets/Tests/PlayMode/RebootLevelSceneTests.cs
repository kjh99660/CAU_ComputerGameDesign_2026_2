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

// 레벨 디자인(Env_RebootHub)이 기존 게임 흐름과 충돌하지 않는지 확인한다.
// 적 생성 높이는 JH_Work의 수정 이후 "라운드 시작 시 플레이어 높이 + EnemySettings.SpawnHeight"다.
// REBOOT_CAPTURE_DIR 환경 변수가 있으면 실제 런타임 카메라 화면과 적 위치 요약을 저장한다.
public sealed class RebootLevelSceneTests
{
    // 레벨 디자인은 미리보기 씬에만 적용되어 있다. 빌드 설정에 없는 씬이라 Editor 전용 로더를 쓴다.
    private const string ScenePath = "Assets/Scenes/Main_RebootHub.unity";
    private const float FloorTolerance = 0.3f;

    [UnityTest]
    public IEnumerator PlayerStartsOnPlazaFloorAndEnvironmentAddsNoObstacles()
    {
        yield return LoadAndWaitReady();
        var state = Object.FindFirstObjectByType<GameStateManager>();
        var enemies = Object.FindFirstObjectByType<EnemyRuntime>();
        var player = Object.FindFirstObjectByType<PlayerRuntime>();

        float? floor = FloorBelow(player.transform.position);
        Assert.IsTrue(floor.HasValue, "No floor under the player start.");
        Assert.AreEqual(floor.Value, player.transform.position.y, 0.1f, "Player must start on the plaza floor.");
        float spawnPlane = player.transform.position.y + enemies.Settings.SpawnHeight;
        foreach (EnemyAgent enemy in Active())
            Assert.AreEqual(spawnPlane, enemy.transform.position.y, 0.05f, $"{enemy.name} is not on the round spawn plane.");

        var env = GameObject.Find("Env_RebootHub");
        Assert.IsNotNull(env, "Env_RebootHub root is missing.");
        Assert.AreEqual(0, env.GetComponentsInChildren<Collider>(true).Length, "Decoration must not add colliders to the combat space.");

        // 광장 반경 안에 들어오는 장식은 바닥에 붙은 평면(높이 0.1m 미만)뿐이어야 한다.
        // 벽걸이 표지판은 아래에서 벽에 붙어 있는지 따로 확인한다.
        var plaza = new Vector2(-24f, 48.2f);
        Transform signage = env.transform.Find("Signage");
        foreach (Renderer renderer in env.GetComponentsInChildren<Renderer>(true))
        {
            if (signage != null && renderer.transform.IsChildOf(signage)) continue;
            Bounds b = renderer.bounds;
            bool overPlaza = Vector2.Distance(new Vector2(b.center.x, b.center.z), plaza) < 19f;
            // 1.9m 위(기존 기둥 꼭대기의 경고등 등)는 이동 공간을 막지 않는다.
            if (overPlaza && b.min.y < floor.Value + 1.9f)
                Assert.Less(b.max.y - floor.Value, 0.1f, $"{renderer.name} blocks the walkable plaza volume.");
        }

        // 보안 표지판 뒷면(로컬 +z, 판 두께 0.1m) 바로 뒤에 맵 벽이 있어야 한다. 벽 속에 묻히거나 공중에 뜨면 실패한다.
        Assert.IsNotNull(signage, "Signage group is missing.");
        int signs = 0;
        foreach (Transform sign in signage)
        {
            if (!sign.name.StartsWith("P_Reboot_SecuritySign", StringComparison.Ordinal)) continue;
            signs++;
            Vector3 face = sign.position + Vector3.up * 0.75f - sign.forward * 0.05f;
            Assert.IsTrue(Physics.Raycast(face, sign.forward, out RaycastHit wall, 0.3f, ~0, QueryTriggerInteraction.Ignore),
                $"{sign.name} at {sign.position} has no wall right behind it.");
            Assert.IsFalse(Physics.CheckSphere(face, 0.04f, ~0, QueryTriggerInteraction.Ignore),
                $"{sign.name} at {sign.position} is buried in geometry (wall at {wall.point}).");
        }
        Assert.AreEqual(2, signs);
        Assert.AreEqual(GameState.Ready, state.CurrentState);
    }

    [UnityTest]
    public IEnumerator BattleEnemiesStayOnSpawnPlaneApproachAndRetryRestoresLayout()
    {
        // 적 이동(EnemyAgent.RunAI)은 보간(Interpolate) Rigidbody의 Transform을 직접 옮긴다. 고정 물리 스텝이 없는 프레임의
        // 이동은 보간이 덮어쓰므로 batchmode의 수백 FPS에서는 이동 속도가 설정값의 일부로 떨어진다.
        // 레벨 배치를 검사하는 테스트라 실제 플레이에 가까운 60 FPS로 고정한다(적 시스템 문제는 Docs/UiTheme/README.md 참고).
        int previousVSync = QualitySettings.vSyncCount;
        int previousTargetFrameRate = Application.targetFrameRate;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        try
        {
            yield return BattleScenario();
        }
        finally
        {
            QualitySettings.vSyncCount = previousVSync;
            Application.targetFrameRate = previousTargetFrameRate;
        }
    }

    private static IEnumerator BattleScenario()
    {
        yield return LoadAndWaitReady();
        var state = Object.FindFirstObjectByType<GameStateManager>();
        var events = Object.FindFirstObjectByType<EventManager>();
        var runtime = Object.FindFirstObjectByType<EnemyRuntime>();
        var player = Object.FindFirstObjectByType<PlayerRuntime>();
        var camera = Camera.main;
        var log = new StringBuilder();
        Vector3 playerStart = player.transform.position;
        float spawnPlane = playerStart.y + runtime.Settings.SpawnHeight;

        Capture(camera, "runtime_ready");
        log.AppendLine("Ready: " + Summarize(Active(), player.transform.position, spawnPlane));

        events.EnqueueRequest(new StartRequest(state.Tick));
        yield return WaitFor(state, GameState.IntroDialogue);
        events.EnqueueRequest(new DialogueCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Countdown);
        events.EnqueueRequest(new CountdownCompleteRequest(state.Tick));
        yield return WaitFor(state, GameState.Battle);

        var firstDistances = new Dictionary<EnemyAgent, float>();
        foreach (EnemyAgent enemy in Active())
            firstDistances[enemy] = Horizontal(enemy.transform.position, player.transform.position);

        // 첫 추가 생성(1초) 전에 접근을 판정한다. 이후에는 새 적과의 분리(Separation) 때문에 옆으로 밀릴 수 있다.
        yield return new WaitForSeconds(0.8f);
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
        Assert.AreEqual(measured, approached, $"Every initial enemy should move toward the player. player={player.transform.position}" + notApproached);

        yield return new WaitForSeconds(1.7f);
        Capture(camera, "runtime_battle_2s");
        List<EnemyAgent> active = Active();
        log.AppendLine("Battle 2.5s: " + Summarize(active, player.transform.position, spawnPlane));
        foreach (EnemyAgent enemy in active)
            Assert.AreEqual(spawnPlane, enemy.transform.position.y, 0.05f, $"{enemy.name} left the spawn plane while chasing.");

        // 플레이어를 동쪽 벽으로 이동시켜 벽 충돌로 광장 안에 머무는지 확인한다(입력 대신 CharacterController 직접 이동).
        // 남·북은 다른 높이로 이어지는 계단이라 벽 검사 대상이 아니다.
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
        log.AppendLine("After Retry: " + Summarize(Active(), player.transform.position, spawnPlane));
        WriteLog(log.ToString());
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

    // 적 위치를 "광장 바닥 위"와 "바닥 밖(공중/다른 층)"으로 나눈다. 바닥 밖은 현재 스폰 코드의 알려진 한계다
    // (플레이어 기준 15~25m 원이 광장 반경 약 20m보다 넓다).
    private static string Summarize(List<EnemyAgent> enemies, Vector3 player, float spawnPlane)
    {
        int onFloor = 0, offFloor = 0;
        foreach (EnemyAgent enemy in enemies)
        {
            float? floor = FloorBelow(enemy.transform.position);
            if (floor.HasValue && Mathf.Abs(floor.Value - spawnPlane) <= FloorTolerance) onFloor++;
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
