using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// 측정이 어떤 환경에서 이뤄졌는지와, 그 환경에서 어떤 지표가 유효한지를 기록한다.
// 결과 CSV는 항상 이 정보와 함께 남겨, 다른 기기의 수치로 오해되지 않게 한다.
public sealed class PerfEnvironment
{
    public const string BatchModeNote = "batchmode: frames are not presented to a display; frame time / FPS / render counters are not recorded";
    public const string NoGraphicsNote = "no graphics device (-nographics): nothing is rendered";

    public string CapturedAtUtc, Label, EnvironmentClass;
    public string UnityVersion, Platform, OperatingSystem, DeviceModel, ProcessorType, ScriptingBackend;
    public int ProcessorCount, SystemMemoryMb, GraphicsMemoryMb;
    public string GraphicsDeviceName, GraphicsDeviceType, QualityLevel;
    public bool IsEditor, IsBatchMode, IsDevelopmentBuild;
    public int ScreenWidth, ScreenHeight;
    public bool StopwatchHighResolution;
    public long StopwatchFrequency;
    public bool FrameMetricsSupported;
    public string FrameMetricsNote;

    // 프레임 시간·FPS·렌더 카운터는 화면에 실제로 그려 내보내는 환경에서만 의미가 있다.
    public static bool SupportsFrameMetrics(bool isBatchMode, GraphicsDeviceType deviceType, out string note)
    {
        if (deviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            note = NoGraphicsNote;
            return false;
        }
        if (isBatchMode)
        {
            note = BatchModeNote;
            return false;
        }
        note = string.Empty;
        return true;
    }

    public static string Classify(bool isEditor, bool isBatchMode, bool isDevelopmentBuild)
    {
        if (isEditor) return isBatchMode ? "editor-batchmode" : "editor-playmode";
        string build = isDevelopmentBuild ? "player-development" : "player-release";
        return isBatchMode ? build + "-batchmode" : build;
    }

    public static PerfEnvironment Capture(string label)
    {
        var env = new PerfEnvironment
        {
            CapturedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            Label = string.IsNullOrEmpty(label) ? "unlabeled" : label,
            UnityVersion = Application.unityVersion,
            Platform = Application.platform.ToString(),
            OperatingSystem = SystemInfo.operatingSystem,
            DeviceModel = SystemInfo.deviceModel,
            ProcessorType = SystemInfo.processorType,
            ProcessorCount = SystemInfo.processorCount,
            SystemMemoryMb = SystemInfo.systemMemorySize,
            GraphicsDeviceName = SystemInfo.graphicsDeviceName,
            GraphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
            GraphicsMemoryMb = SystemInfo.graphicsMemorySize,
            QualityLevel = QualitySettings.names.Length > 0 ? QualitySettings.names[QualitySettings.GetQualityLevel()] : string.Empty,
            IsEditor = Application.isEditor,
            IsBatchMode = Application.isBatchMode,
            IsDevelopmentBuild = UnityEngine.Debug.isDebugBuild,
            ScreenWidth = Screen.width,
            ScreenHeight = Screen.height,
            StopwatchHighResolution = Stopwatch.IsHighResolution,
            StopwatchFrequency = Stopwatch.Frequency,
#if ENABLE_IL2CPP
            ScriptingBackend = "IL2CPP",
#elif ENABLE_MONO
            ScriptingBackend = "Mono",
#else
            ScriptingBackend = "unknown",
#endif
        };
        env.EnvironmentClass = Classify(env.IsEditor, env.IsBatchMode, env.IsDevelopmentBuild);
        env.FrameMetricsSupported = SupportsFrameMetrics(env.IsBatchMode, SystemInfo.graphicsDeviceType, out env.FrameMetricsNote);
        return env;
    }

    public List<KeyValuePair<string, string>> ToRows()
    {
        var rows = new List<KeyValuePair<string, string>>();
        void Add(string key, object value) =>
            rows.Add(new KeyValuePair<string, string>(key, Convert.ToString(value, CultureInfo.InvariantCulture)));
        Add("label", Label);
        Add("captured_at_utc", CapturedAtUtc);
        Add("environment_class", EnvironmentClass);
        Add("unity_version", UnityVersion);
        Add("platform", Platform);
        Add("operating_system", OperatingSystem);
        Add("device_model", DeviceModel);
        Add("processor", ProcessorType);
        Add("processor_count", ProcessorCount);
        Add("system_memory_mb", SystemMemoryMb);
        Add("graphics_device", GraphicsDeviceName);
        Add("graphics_api", GraphicsDeviceType);
        Add("graphics_memory_mb", GraphicsMemoryMb);
        Add("quality_level", QualityLevel);
        Add("is_editor", IsEditor ? 1 : 0);
        Add("is_batchmode", IsBatchMode ? 1 : 0);
        Add("is_development_build", IsDevelopmentBuild ? 1 : 0);
        Add("scripting_backend", ScriptingBackend);
        Add("screen", ScreenWidth + "x" + ScreenHeight);
        Add("stopwatch_high_resolution", StopwatchHighResolution ? 1 : 0);
        Add("stopwatch_frequency_hz", StopwatchFrequency);
        Add("candidate_counts_valid", 1);
        Add("cpu_time_valid", 1);
        Add("cpu_time_note", "Stopwatch wall time of the measured code on this machine only");
        Add("frame_metrics_valid", FrameMetricsSupported ? 1 : 0);
        Add("frame_metrics_note", FrameMetricsSupported ? (IsEditor ? "Editor Play Mode includes Editor overhead; not a player measurement" : string.Empty) : FrameMetricsNote);
        return rows;
    }

    public void WriteCsv(string path, IEnumerable<KeyValuePair<string, string>> extraRows = null)
    {
        var text = new StringBuilder("key,value\n");
        foreach (KeyValuePair<string, string> row in ToRows())
            text.Append(PerfCsv.Escape(row.Key)).Append(',').Append(PerfCsv.Escape(row.Value)).Append('\n');
        if (extraRows != null)
            foreach (KeyValuePair<string, string> row in extraRows)
                text.Append(PerfCsv.Escape(row.Key)).Append(',').Append(PerfCsv.Escape(row.Value)).Append('\n');
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }
}

// CSV 값 쓰기 도우미. 숫자는 항상 마침표 소수점(Invariant)으로 쓰고, 유효하지 않은 값은 빈칸으로 둔다.
public static class PerfCsv
{
    public static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static string Number(double value, string format = "0.###") =>
        double.IsNaN(value) || double.IsInfinity(value) ? string.Empty : value.ToString(format, CultureInfo.InvariantCulture);

    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    // 유효하지 않으면 빈칸.
    public static string Optional(long value, bool valid) => valid ? Number(value) : string.Empty;

    public static double Mean(IReadOnlyList<double> values)
    {
        if (values == null || values.Count == 0) return double.NaN;
        double sum = 0d;
        for (int i = 0; i < values.Count; i++) sum += values[i];
        return sum / values.Count;
    }

    // 정렬한 값에서 가장 가까운 순위를 고른다(percentile 0~1).
    public static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values == null || values.Count == 0) return double.NaN;
        var sorted = new double[values.Count];
        for (int i = 0; i < sorted.Length; i++) sorted[i] = values[i];
        Array.Sort(sorted);
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Min(sorted.Length - 1, Math.Max(0, index))];
    }
}
