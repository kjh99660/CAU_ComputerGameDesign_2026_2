using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// 적 수를 바꿔 가며 "실제 한 판" A/B 측정을 차례로 돌린다(예: 100, 200, 300명).
// 일반 플레이의 상한(EnemySettings, 100명)을 넘는 수는 성능 실험용 상한으로만 채운다. 설정 에셋과 게임 규칙은 바꾸지 않는다.
// 적 수가 하나면 결과를 지정한 폴더에 그대로 쓰고, 여럿이면 enemies-<수> 폴더로 나눠 쓴 뒤 scaling_summary.csv로 묶는다.
public static class EnemyAbSweep
{
    public const string ScalingFileName = "scaling_summary.csv";
    public const string ScalingHeader = "target_enemies,enemy_cap_in_settings,experiment_enemy_cap," + EnemyAbRoundReport.SummaryHeader;

    // "100,200,300" 같은 목록을 읽는다. 숫자가 아니거나 1보다 작은 항목은 버리고, 실험 허용치를 넘는 값은 허용치로 낮추며,
    // 같은 수는 한 번만 남긴다. 쓸 수 있는 항목이 없으면 fallback 하나를 돌려준다.
    public static int[] ParseCounts(string text, int fallback = 100)
    {
        var counts = new List<int>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (string part in text.Split(','))
            {
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 1)
                    continue;
                value = Math.Min(value, EnemyPerfProbe.ExperimentEnemyLimit);
                if (!counts.Contains(value))
                    counts.Add(value);
            }
        }
        if (counts.Count == 0)
            counts.Add(Mathf.Clamp(fallback, 1, EnemyPerfProbe.ExperimentEnemyLimit));
        return counts.ToArray();
    }

    public static string FolderFor(int count) => "enemies-" + count.ToString(CultureInfo.InvariantCulture);

    // 적 수 하나에 대한 설정. 다른 값은 template과 같다.
    public static EnemyAbRoundConfig ConfigFor(EnemyAbRoundConfig template, int count, bool separateFolder)
    {
        var config = JsonUtility.FromJson<EnemyAbRoundConfig>(JsonUtility.ToJson(template));
        config.TargetEnemies = count;
        if (separateFolder && !string.IsNullOrEmpty(template.OutputDirectory))
            config.OutputDirectory = Path.Combine(template.OutputDirectory, FolderFor(count));
        return config;
    }

    // counts의 적 수마다 한 번씩 측정한다. 하나가 실패하면 거기서 멈춘다. 끝나면 지금까지의 결과와 성공 여부를 알린다.
    public static void Run(EnemyAbRoundConfig template, IReadOnlyList<int> counts,
        Action<IReadOnlyList<EnemyAbRoundReport>, bool> completed = null)
    {
        if (template == null) throw new ArgumentNullException(nameof(template));
        if (counts == null || counts.Count == 0)
            counts = new[] { template.TargetEnemies };
        RunNext(template, counts, 0, new List<EnemyAbRoundReport>(), completed);
    }

    private static void RunNext(EnemyAbRoundConfig template, IReadOnlyList<int> counts, int index,
        List<EnemyAbRoundReport> reports, Action<IReadOnlyList<EnemyAbRoundReport>, bool> completed)
    {
        bool separate = counts.Count > 1;
        EnemyAbRoundRunner runner = null;
        runner = EnemyAbRoundRunner.Run(ConfigFor(template, counts[index], separate), report =>
        {
            reports.Add(report);
            // 끝난 실행기는 치운다. 다음 실행기가 시간·화면 설정을 다시 저장하고 건다.
            if (runner != null)
                UnityEngine.Object.Destroy(runner.gameObject);
            if (report.Succeeded && index + 1 < counts.Count)
            {
                RunNext(template, counts, index + 1, reports, completed);
                return;
            }

            bool succeeded = report.Succeeded;
            if (separate && !string.IsNullOrEmpty(template.OutputDirectory))
            {
                try
                {
                    Directory.CreateDirectory(template.OutputDirectory);
                    File.WriteAllText(Path.Combine(template.OutputDirectory, ScalingFileName), ScalingCsv(reports), new UTF8Encoding(false));
                }
                catch (Exception exception)
                {
                    Debug.LogError("[EnemyAb] Could not write " + ScalingFileName + ": " + exception.Message);
                    succeeded = false;
                }
            }
            completed?.Invoke(reports, succeeded);
        });
    }

    // 적 수별 요약 행을 한 파일로 묶는다. 각 행 앞에 목표 적 수와 상한 두 가지를 붙인다.
    public static string ScalingCsv(IReadOnlyList<EnemyAbRoundReport> reports)
    {
        var text = new StringBuilder(ScalingHeader).Append('\n');
        if (reports == null)
            return text.ToString();
        foreach (EnemyAbRoundReport report in reports)
        {
            string prefix = PerfCsv.Number(report.Config.TargetEnemies) + "," + PerfCsv.Number(report.EnemyCapInSettings) + "," +
                            PerfCsv.Number(report.ExperimentEnemyCap) + ",";
            foreach (string line in report.SummaryLines())
                text.Append(prefix).Append(line).Append('\n');
        }
        return text.ToString();
    }
}
