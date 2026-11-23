using System;
using System.Globalization;
using System.IO;
using UnityEngine;

// 플레이어 빌드를 "-rebootEnemyAb" 인자로 실행하면 A/B 한 판 측정을 자동으로 돌리고 결과를 쓴 뒤 끝낸다.
// 인자가 없으면 아무 일도 하지 않는다. 에디터에서는 Tools/REBOOT Perf 메뉴나 배치 명령이 같은 실행기를 쓴다.
//   -rebootOut <폴더>          결과 CSV를 쓸 곳(기본: persistentDataPath/EnemyAb)
//   -rebootLabel <이름>        결과에 적을 측정 이름(예: demo-pc-player)
//   -rebootEnemies <수>        측정을 시작할 활성 적 수(기본 100). "100,200,300"처럼 여럿을 주면 차례로 돌리고
//                              결과를 enemies-<수> 폴더로 나눈 뒤 scaling_summary.csv로 묶는다. 100명을 넘는 수는
//                              성능 실험용 상한(최대 300명)으로 채우며 게임 설정은 바꾸지 않는다
//   -rebootWindowFrames <수>   측정 구간 길이(기본 900프레임 = 게임 시간 15초)
//   -rebootRepeats <수>        방식마다 시간을 재는 판 수(기본 2)
//   -rebootEnemyAbStay         끝난 뒤 프로그램을 닫지 않는다
public static class EnemyAbBootstrap
{
    public const string RunArgument = "-rebootEnemyAb";
    public const string StayArgument = "-rebootEnemyAbStay";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoStart()
    {
        if (Application.isEditor)
            return;
        string[] args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, RunArgument) < 0)
            return;

        EnemyAbRoundConfig config = ConfigFromArguments(args, Path.Combine(Application.persistentDataPath, "EnemyAb"));
        int[] counts = CountsFromArguments(args, config.TargetEnemies);
        bool stay = Array.IndexOf(args, StayArgument) >= 0;
        Debug.Log($"[EnemyAb] Starting the A/B round benchmark (enemies: {string.Join(", ", counts)}). Results: {config.OutputDirectory}");
        EnemyAbSweep.Run(config, counts, (reports, succeeded) =>
        {
            Debug.Log(succeeded
                ? "[EnemyAb] Finished. Results: " + config.OutputDirectory
                : "[EnemyAb] Failed: " + (reports.Count > 0 ? reports[reports.Count - 1].Error : "no result"));
            if (!stay)
                Application.Quit(succeeded ? 0 : 1);
        });
    }

    // "-rebootEnemies"에 적힌 적 수 목록. 인자가 없으면 fallback 하나다.
    public static int[] CountsFromArguments(string[] args, int fallback) =>
        EnemyAbSweep.ParseCounts(Argument(args, "-rebootEnemies"), fallback);

    public static EnemyAbRoundConfig ConfigFromArguments(string[] args, string defaultOutput)
    {
        var config = new EnemyAbRoundConfig { OutputDirectory = defaultOutput };
        string value;
        if ((value = Argument(args, "-rebootOut")) != null) config.OutputDirectory = value;
        if ((value = Argument(args, "-rebootLabel")) != null) config.Label = value;
        // 적 수를 여럿 적었으면 첫 번째가 이 설정의 값이다(나머지는 CountsFromArguments로 읽는다).
        config.TargetEnemies = CountsFromArguments(args, config.TargetEnemies)[0];
        if (TryInt(args, "-rebootRepeats", out int repeats)) config.TimingRepeats = Mathf.Max(0, repeats);
        if (TryInt(args, "-rebootWindowFrames", out int frames))
        {
            // 구간 길이를 바꾸면 궤적 길이와 공격 시점도 같은 비율로 맞춘다(기본 900프레임에서 300, 500, 700).
            config.WindowFrames = Mathf.Max(3, frames);
            config.TraceFrames = config.WindowFrames / 3;
            config.SpecialFrames = new[] { config.WindowFrames / 3, config.WindowFrames * 5 / 9, config.WindowFrames * 7 / 9 };
        }
        return config;
    }

    public static string Argument(string[] args, string name)
    {
        if (args == null) return null;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    private static bool TryInt(string[] args, string name, out int value)
    {
        value = 0;
        string text = Argument(args, name);
        return text != null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
