using System;

// 한 판의 프레임 요약. 프레임이 없으면 FPS·시간·평균 값은 NaN이다(0으로 채우지 않는다).
public readonly struct RoundFrameSummary
{
    // 센 프레임 수(0 이하이거나 숫자가 아닌 시간은 세지 않고 IgnoredFrames에 남긴다).
    public readonly long Frames;
    public readonly long IgnoredFrames;
    // 버퍼가 가득 찬 뒤의 프레임 수. 합계·평균·최저에는 들어가고 1% low 계산에서만 빠진다.
    public readonly long OverflowFrames;
    // 1% low를 계산한 가장 느린 프레임 수(저장된 프레임의 1%, 올림, 최소 1).
    public readonly int SlowestFrames;
    public readonly double TotalSeconds;
    public readonly double AverageFps, OnePercentLowFps, MinimumFps;
    public readonly double AverageFrameMs, WorstFrameMs;
    public readonly double EnemiesAverage;
    public readonly int EnemiesMax;

    public RoundFrameSummary(long frames, long ignoredFrames, long overflowFrames, int slowestFrames, double totalSeconds,
        double averageFps, double onePercentLowFps, double minimumFps, double averageFrameMs, double worstFrameMs,
        double enemiesAverage, int enemiesMax)
    {
        Frames = frames;
        IgnoredFrames = ignoredFrames;
        OverflowFrames = overflowFrames;
        SlowestFrames = slowestFrames;
        TotalSeconds = totalSeconds;
        AverageFps = averageFps;
        OnePercentLowFps = onePercentLowFps;
        MinimumFps = minimumFps;
        AverageFrameMs = averageFrameMs;
        WorstFrameMs = worstFrameMs;
        EnemiesAverage = enemiesAverage;
        EnemiesMax = enemiesMax;
    }

    public bool HasFrames => Frames > 0;

    public static RoundFrameSummary Empty(long ignoredFrames) => new RoundFrameSummary(0, ignoredFrames, 0, 0, 0d,
        double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, 0);
}

// 한 판 동안 매 프레임의 실제 시간(초)과 활성 Enemy 수를 받아, 판이 끝나면 평균·1% low·최저 FPS를 낸다.
// Unity API를 쓰지 않으므로 EditMode에서 같은 입력으로 검증한다. 프레임마다 할당하지 않는다:
// 프레임 시간 버퍼는 만들 때 한 번 잡고, 요약은 그 버퍼 안에서 정렬해 계산한다.
//   평균 FPS   = 프레임 수 / 프레임 시간의 합
//   1% low FPS = 가장 느린 1% 프레임의 수 / 그 프레임들의 시간 합 (= 그 프레임들의 평균 프레임 시간을 FPS로 바꾼 값)
//   최저 FPS   = 1 / 가장 긴 프레임 시간
public sealed class RoundFrameRecorder
{
    // 한 판(Battle 100초 + Finishing 2.5초)을 평균 약 1,950 FPS까지 담는다. 1프레임에 4바이트, 약 0.8MB.
    public const int DefaultCapacity = 200_000;

    private readonly float[] _seconds;
    private int _stored;
    private long _frames, _ignored, _overflow;
    private double _totalSeconds, _enemySum;
    private float _slowest;
    private int _enemyMax;

    public RoundFrameRecorder(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), "The frame buffer needs room for at least one frame.");
        _seconds = new float[capacity];
    }

    public int Capacity => _seconds.Length;
    public long FrameCount => _frames;
    public long IgnoredFrames => _ignored;
    public long OverflowFrames => _overflow;
    public double TotalSeconds => _totalSeconds;

    public void Clear()
    {
        _stored = 0;
        _frames = _ignored = _overflow = 0;
        _totalSeconds = _enemySum = 0d;
        _slowest = 0f;
        _enemyMax = 0;
    }

    // 한 프레임의 실제 시간(초)과 그때의 활성 Enemy 수를 더한다.
    public void Add(float seconds, int activeEnemies)
    {
        if (!(seconds > 0f) || float.IsInfinity(seconds))
        {
            _ignored++;
            return;
        }
        _frames++;
        _totalSeconds += seconds;
        if (seconds > _slowest)
            _slowest = seconds;
        if (activeEnemies > 0)
            _enemySum += activeEnemies;
        if (activeEnemies > _enemyMax)
            _enemyMax = activeEnemies;
        if (_stored < _seconds.Length)
            _seconds[_stored++] = seconds;
        else
            _overflow++;
    }

    // 지금까지의 요약. 저장된 프레임의 순서를 바꾼다(정렬). 순서는 다른 곳에 쓰지 않으므로, 요약한 뒤 더 더하거나
    // 다시 요약해도 값은 맞다.
    public RoundFrameSummary Summarize()
    {
        if (_frames == 0)
            return RoundFrameSummary.Empty(_ignored);

        Array.Sort(_seconds, 0, _stored);
        int slowest = Math.Max(1, (_stored + 99) / 100);
        double slowestSeconds = 0d;
        for (int i = _stored - slowest; i < _stored; i++)
            slowestSeconds += _seconds[i];

        return new RoundFrameSummary(
            _frames, _ignored, _overflow, slowest, _totalSeconds,
            averageFps: _frames / _totalSeconds,
            onePercentLowFps: slowest / slowestSeconds,
            minimumFps: 1d / _slowest,
            averageFrameMs: _totalSeconds * 1000d / _frames,
            worstFrameMs: _slowest * 1000d,
            enemiesAverage: _enemySum / _frames,
            enemiesMax: _enemyMax);
    }
}
