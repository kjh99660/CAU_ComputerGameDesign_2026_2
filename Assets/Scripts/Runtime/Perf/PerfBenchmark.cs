using System;
using System.Globalization;
using System.Text;

// 플레이어를 "-rebootBenchmark" 인자로 실행하면 한 판을 자동으로 시작해 끝까지 돌리고, 라운드 성능 기록을 쓴 뒤
// 프로그램을 닫는다(실행은 PerfRuntime이 맡는다). 인자가 없으면 아무것도 바뀌지 않는다. Editor에서는 쓰지 않는다.
//   -rebootBenchmarkCap <수>       성능 실험용 적 상한(예: 200, 300). 게임 설정(100명)보다 클 때만 쓰인다
//   -rebootBenchmarkSearch brute   적 이웃 검색을 전수 방식으로 돌린다(기본: grid, 공간 분할)
// 빌드 스크립트(RebootBuild)가 플레이어 옆에 쓰는 실행 파일의 내용도 여기서 만든다(결과 폴더 위치를 한곳에서 정한다).
public static class PerfBenchmark
{
    public const string Argument = "-rebootBenchmark";
    public const string CapArgument = "-rebootBenchmarkCap";
    public const string SearchArgument = "-rebootBenchmarkSearch";
    public const string MacScriptName = "run_benchmark.command";
    public const string WindowsScriptName = "run_benchmark.bat";

    public static bool IsRequested(string[] args) => args != null && Array.IndexOf(args, Argument) >= 0;

    // 이 실행에서 A/B 측정기(EnemyAbBootstrap)가 도는가. 그 측정기와 같은 조건이다: 플레이어에서 "-rebootEnemyAb"가 있을 때.
    // 그때는 라운드 기록과 이 벤치마크를 쓰지 않는다.
    public static bool IsEnemyAbRun(string[] args, bool isEditor) =>
        !isEditor && args != null && Array.IndexOf(args, EnemyAbBootstrap.RunArgument) >= 0;

    // 실험용 적 상한. 인자가 없거나 숫자가 아니면 0(게임 설정 그대로)이고, 0~300으로 자른다.
    public static int CapFromArguments(string[] args)
    {
        string text = EnemyAbBootstrap.Argument(args, CapArgument);
        if (text == null || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cap))
            return 0;
        return Math.Max(0, Math.Min(EnemyPerfProbe.ExperimentEnemyLimit, cap));
    }

    public static EnemyCandidateMode SearchFromArguments(string[] args)
    {
        string text = EnemyAbBootstrap.Argument(args, SearchArgument);
        if (text == null)
            return EnemyCandidateMode.SpatialGrid;
        string mode = text.Trim().ToLowerInvariant();
        return mode == "brute" || mode == "bruteforce" || mode == "brute-force"
            ? EnemyCandidateMode.BruteForce
            : EnemyCandidateMode.SpatialGrid;
    }

    // Application.persistentDataPath/perf가 각 OS에서 놓이는 곳(회사 이름과 제품 이름은 Player Settings 값).
    public static string MacResultsFolder(string companyName, string productName) =>
        "$HOME/Library/Application Support/" + companyName + "/" + productName + "/" + PerfOutput.FolderName;

    public static string WindowsResultsFolder(string companyName, string productName) =>
        "%USERPROFILE%\\AppData\\LocalLow\\" + companyName + "\\" + productName + "\\" + PerfOutput.FolderName;

    // 이번 실행에서 새 기록이 생기지 않았을 때 실행 파일이 보여 주는 말(예전 판의 요약을 이번 결과처럼 보이지 않는다).
    public const string NoNewRecordingMessage =
        "No new round recording was written by this run. Check that the round reached the Result screen.";

    // REBOOT.app 옆에 두는 run_benchmark.command. 한 판을 돌리고, 이번 실행이 새로 쓴 요약만 보여 준다
    // (시작 전에 가장 최근이던 파일과 끝난 뒤 가장 최근 파일이 같으면 새 기록이 없는 것이다).
    public static string MacLauncherScript(string appBundleName, string companyName, string productName)
    {
        var text = new StringBuilder();
        void Line(string line) => text.Append(line).Append('\n');
        Line("#!/bin/sh");
        Line("# REBOOT performance benchmark (written by RebootBuild next to the player).");
        Line("# Starts " + appBundleName + " with " + Argument + ": one round starts by itself, the round recording is");
        Line("# written when the Result screen is reached, and the game closes. Options can follow, for example:");
        Line("#   ./" + MacScriptName + " " + CapArgument + " 300 " + SearchArgument + " brute");
        Line("cd \"$(dirname \"$0\")\" || exit 1");
        Line("RESULTS=\"" + MacResultsFolder(companyName, productName) + "\"");
        Line("BEFORE=$(ls -t \"$RESULTS\"/round_*.txt 2>/dev/null | head -n 1)");
        Line("echo \"REBOOT benchmark: one 100-second round plays by itself. Do not touch the keyboard or mouse until the game closes.\"");
        Line("echo \"Results folder: $RESULTS\"");
        Line("open -W -n \"./" + appBundleName + "\" --args " + Argument + " \"$@\"");
        Line("LATEST=$(ls -t \"$RESULTS\"/round_*.txt 2>/dev/null | head -n 1)");
        Line("if [ -n \"$LATEST\" ] && [ \"$LATEST\" != \"$BEFORE\" ]; then");
        Line("  echo \"\"");
        Line("  cat \"$LATEST\"");
        Line("else");
        Line("  echo \"" + NoNewRecordingMessage + "\"");
        Line("fi");
        return text.ToString();
    }

    // REBOOT.exe 옆에 두는 run_benchmark.bat. cmd.exe가 읽도록 줄 끝은 CRLF이고 글자는 ASCII만 쓴다.
    // 맥용과 같이 시작 전과 끝난 뒤의 가장 최근 파일 이름을 견줘, 이번 실행이 새로 쓴 요약만 보여 준다.
    public static string WindowsLauncherScript(string exeName, string companyName, string productName)
    {
        const string newest = "('dir /b /o-d \"%RESULTS%\\round_*.txt\" 2^>nul')";
        var text = new StringBuilder();
        void Line(string line) => text.Append(line).Append("\r\n");
        Line("@echo off");
        Line("rem REBOOT performance benchmark (written by RebootBuild next to the player).");
        Line("rem Starts " + exeName + " with " + Argument + ": one round starts by itself, the round recording is");
        Line("rem written when the Result screen is reached, and the game closes. Options can follow, for example:");
        Line("rem   " + WindowsScriptName + " " + CapArgument + " 300 " + SearchArgument + " brute");
        Line("cd /d \"%~dp0\"");
        Line("set \"RESULTS=" + WindowsResultsFolder(companyName, productName) + "\"");
        Line("set \"BEFORE=\"");
        Line("for /f \"delims=\" %%F in " + newest + " do if not defined BEFORE set \"BEFORE=%%F\"");
        Line("echo REBOOT benchmark: one 100-second round plays by itself. Do not touch the keyboard or mouse until the game closes.");
        Line("echo Results folder: %RESULTS%");
        Line("start \"\" /wait \"" + exeName + "\" " + Argument + " %*");
        Line("set \"LATEST=\"");
        Line("for /f \"delims=\" %%F in " + newest + " do if not defined LATEST set \"LATEST=%%F\"");
        Line("set \"NEW=\"");
        Line("if defined LATEST if /i not \"%LATEST%\"==\"%BEFORE%\" set \"NEW=1\"");
        Line("if defined NEW (");
        Line("  echo.");
        Line("  type \"%RESULTS%\\%LATEST%\"");
        Line(") else (");
        Line("  echo " + NoNewRecordingMessage);
        Line(")");
        Line("pause");
        return text.ToString();
    }
}
