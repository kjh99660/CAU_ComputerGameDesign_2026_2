using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

// 플레이어 빌드 진입점. EditorBuildSettings에서 켜 둔 씬을 Release(개발 빌드 아님)로 <프로젝트>/Builds/<플랫폼>/에 빌드하고,
// 같은 폴더에 성능 측정 실행 파일(run_benchmark.command / run_benchmark.bat, PerfBenchmark 참고)을 쓴다.
//   메뉴  Tools > REBOOT Build > Build Mac (REBOOT.app), Build Windows (REBOOT.exe)
//   배치  Unity -batchmode -projectPath <프로젝트> -executeMethod RebootBuild.BuildMacBatch -quit
//         Unity -batchmode -projectPath <프로젝트> -executeMethod RebootBuild.BuildWindowsBatch -quit
//         실패하면 종료 코드 1로 끝난다.
// Windows 빌드에는 Unity Hub에서 이 Editor 버전에 "Windows Build Support (Mono)" 모듈을 설치해야 한다.
// 모듈이 없으면 빌드를 시작하지 않고 그 사실을 알린다.
public static class RebootBuild
{
    public const string PlayerName = "REBOOT";
    public const string BuildsFolder = "Builds";

    internal enum Platform
    {
        Mac,
        Windows
    }

    // 메뉴 경로의 '/'는 하위 메뉴를 만들므로 항목 이름에는 쓰지 않는다.
    [MenuItem("Tools/REBOOT Build/Build Mac (REBOOT.app)")]
    public static void BuildMac() => BuildFromMenu(Platform.Mac);

    [MenuItem("Tools/REBOOT Build/Build Windows (REBOOT.exe)")]
    public static void BuildWindows() => BuildFromMenu(Platform.Windows);

    public static void BuildMacBatch() => BuildFromBatch(Platform.Mac);

    public static void BuildWindowsBatch() => BuildFromBatch(Platform.Windows);

    private static void BuildFromMenu(Platform platform)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[RebootBuild] Stop Play Mode before building.");
            return;
        }
        if (Build(platform, out string output) && !Application.isBatchMode)
            EditorUtility.RevealInFinder(output);
    }

    private static void BuildFromBatch(Platform platform)
    {
        bool succeeded;
        try
        {
            succeeded = Build(platform, out _);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            succeeded = false;
        }
        EditorApplication.Exit(succeeded ? 0 : 1);
    }

    // isSupported는 "모듈 없음" 경로를 확인할 때만 넘긴다. 비우면 Editor에 설치된 모듈을 그대로 확인한다.
    internal static bool Build(Platform platform, out string output, Func<BuildTargetGroup, BuildTarget, bool> isSupported = null)
    {
        BuildTarget target = platform == Platform.Mac ? BuildTarget.StandaloneOSX : BuildTarget.StandaloneWindows64;
        string folder = Path.Combine(ProjectRoot, BuildsFolder, platform.ToString());
        output = Path.Combine(folder, platform == Platform.Mac ? PlayerName + ".app" : PlayerName + ".exe");

        isSupported ??= BuildPipeline.IsBuildTargetSupported;
        if (!isSupported(BuildTargetGroup.Standalone, target))
        {
            Debug.LogError($"[RebootBuild] {platform} build support is not installed for Unity {Application.unityVersion}. " +
                           $"Add the \"{(platform == Platform.Mac ? "Mac Build Support (Mono)" : "Windows Build Support (Mono)")}\" " +
                           "module to this Editor in Unity Hub (Installs > this version > Add modules), then build again. Nothing was built.");
            return false;
        }

        string[] scenes = EnabledScenes();
        if (scenes.Length == 0)
        {
            Debug.LogError("[RebootBuild] No scene is enabled in File > Build Profiles (EditorBuildSettings). Nothing was built.");
            return false;
        }

        Directory.CreateDirectory(folder);
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = target,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,   // Release: not a development build, no script debugging, no profiler connection
        };
        Debug.Log($"[RebootBuild] Building {platform} player (release) with {scenes.Length} scene(s): {string.Join(", ", scenes)} -> {output}");
        var clock = Stopwatch.StartNew();
        BuildReport report = BuildPipeline.BuildPlayer(options);
        clock.Stop();
        BuildSummary summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[RebootBuild] {platform} build {summary.result} after {clock.Elapsed.TotalSeconds:0} s " +
                           $"({summary.totalErrors} error(s)). See the messages above. Output: {output}");
            return false;
        }

        string script = WriteBenchmarkLauncher(platform, folder);
        // Mac은 .app 폴더 하나, Windows는 exe 옆의 _Data 폴더와 DLL까지 함께 옮겨야 하므로 폴더 전체를 잰다.
        long bytes = SizeOnDisk(platform == Platform.Mac ? output : folder);
        Debug.Log(string.Format(CultureInfo.InvariantCulture,
            "[RebootBuild] {0} build succeeded in {1:0} s: {2} ({3:0.0} MB on disk; build report total {4:0.0} MB). " +
            "Benchmark launcher: {5}",
            platform, clock.Elapsed.TotalSeconds, output, bytes / (1024d * 1024d), summary.totalSize / (1024d * 1024d), script));
        return true;
    }

    private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

    private static string[] EnabledScenes()
    {
        var scenes = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                scenes.Add(scene.path);
        return scenes.ToArray();
    }

    // 플레이어 옆에 벤치마크 실행 파일을 쓴다. 결과 폴더는 이 빌드의 회사 이름과 제품 이름으로 정해진다.
    private static string WriteBenchmarkLauncher(Platform platform, string folder)
    {
        string company = PlayerSettings.companyName;
        string product = PlayerSettings.productName;
        if (platform == Platform.Windows)
        {
            string bat = Path.Combine(folder, PerfBenchmark.WindowsScriptName);
            File.WriteAllText(bat, PerfBenchmark.WindowsLauncherScript(PlayerName + ".exe", company, product), new ASCIIEncoding());
            return bat;
        }

        string command = Path.Combine(folder, PerfBenchmark.MacScriptName);
        File.WriteAllText(command, PerfBenchmark.MacLauncherScript(PlayerName + ".app", company, product), new UTF8Encoding(false));
        MakeExecutable(command);
        return command;
    }

    // Finder에서 두 번 눌러 실행되도록 실행 권한을 준다(macOS·Linux Editor에서만 가능).
    private static void MakeExecutable(string path)
    {
        if (Application.platform != RuntimePlatform.OSXEditor && Application.platform != RuntimePlatform.LinuxEditor)
        {
            Debug.LogWarning("[RebootBuild] Run \"chmod +x " + Path.GetFileName(path) + "\" on the Mac before using the launcher.");
            return;
        }
        try
        {
            using Process chmod = Process.Start(new ProcessStartInfo("/bin/chmod", "755 \"" + path + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (chmod == null || !chmod.WaitForExit(10000) || chmod.ExitCode != 0)
                Debug.LogWarning("[RebootBuild] Could not make " + path + " executable; run chmod +x on it.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[RebootBuild] Could not make " + path + " executable (" + exception.Message + "); run chmod +x on it.");
        }
    }

    // .app은 폴더이므로 안의 파일 크기를 모두 더한다.
    private static long SizeOnDisk(string path)
    {
        if (File.Exists(path))
            return new FileInfo(path).Length;
        if (!Directory.Exists(path))
            return 0;
        long total = 0;
        foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            total += new FileInfo(file).Length;
        return total;
    }
}
