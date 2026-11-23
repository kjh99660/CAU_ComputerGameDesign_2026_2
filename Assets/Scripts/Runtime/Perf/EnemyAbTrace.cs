using System;
using System.Globalization;
using System.Text;

// 적 N명과 플레이어의 프레임별 XZ 위치를 담은 궤적. 두 방식이 "같은 위치"를 보도록 입력으로 쓴다.
// 글자 파일은 밀리미터 정수로 저장해, 어느 기기에서 읽어도 같은 값이 된다.
public sealed class EnemyAbTrace
{
    public const string FormatTag = "reboot-enemy-ab-trace-v1";

    public int EnemyCount { get; private set; }
    public int FrameCount { get; private set; }
    public float DeltaTime { get; private set; }
    public string Source { get; private set; }

    private int[] _enemyMm;   // [frame][enemy][x, z]
    private int[] _playerMm;  // [frame][x, z]

    public EnemyAbTrace(int enemyCount, int frameCount, float deltaTime, string source)
    {
        if (enemyCount <= 0) throw new ArgumentOutOfRangeException(nameof(enemyCount));
        if (frameCount <= 0) throw new ArgumentOutOfRangeException(nameof(frameCount));
        EnemyCount = enemyCount;
        FrameCount = frameCount;
        DeltaTime = deltaTime;
        Source = source ?? string.Empty;
        _enemyMm = new int[frameCount * enemyCount * 2];
        _playerMm = new int[frameCount * 2];
    }

    public static int ToMillimetres(float metres) => (int)Math.Round(metres * 1000d, MidpointRounding.AwayFromZero);

    public void SetEnemy(int frame, int enemy, float x, float z)
    {
        int index = (frame * EnemyCount + enemy) * 2;
        _enemyMm[index] = ToMillimetres(x);
        _enemyMm[index + 1] = ToMillimetres(z);
    }

    public void SetPlayer(int frame, float x, float z)
    {
        _playerMm[frame * 2] = ToMillimetres(x);
        _playerMm[frame * 2 + 1] = ToMillimetres(z);
    }

    public float EnemyX(int frame, int enemy) => _enemyMm[(frame * EnemyCount + enemy) * 2] / 1000f;
    public float EnemyZ(int frame, int enemy) => _enemyMm[(frame * EnemyCount + enemy) * 2 + 1] / 1000f;
    public float PlayerX(int frame) => _playerMm[frame * 2] / 1000f;
    public float PlayerZ(int frame) => _playerMm[frame * 2 + 1] / 1000f;

    // 머리말 네 줄 뒤에 프레임마다 한 줄: "플레이어x 플레이어z | 적0x 적0z 적1x 적1z ..." (밀리미터 정수).
    public string Serialize()
    {
        var text = new StringBuilder(FrameCount * (EnemyCount * 14 + 20));
        text.Append("# ").Append(FormatTag).Append('\n');
        text.Append("# enemies=").Append(EnemyCount.ToString(CultureInfo.InvariantCulture))
            .Append(" frames=").Append(FrameCount.ToString(CultureInfo.InvariantCulture))
            .Append(" dt=").Append(DeltaTime.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        text.Append("# source=").Append(Source.Replace('\n', ' ')).Append('\n');
        text.Append("# units=millimetres; line = playerX playerZ | enemy0X enemy0Z enemy1X enemy1Z ...\n");
        for (int frame = 0; frame < FrameCount; frame++)
        {
            text.Append(_playerMm[frame * 2].ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(_playerMm[frame * 2 + 1].ToString(CultureInfo.InvariantCulture)).Append(" |");
            int start = frame * EnemyCount * 2;
            for (int i = 0; i < EnemyCount * 2; i++)
                text.Append(' ').Append(_enemyMm[start + i].ToString(CultureInfo.InvariantCulture));
            text.Append('\n');
        }
        return text.ToString();
    }

    public static EnemyAbTrace Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) throw new FormatException("Trace is empty.");
        string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 5 || !lines[0].Contains(FormatTag))
            throw new FormatException("Not a " + FormatTag + " file.");

        int enemies = 0, frames = 0;
        float deltaTime = 0f;
        foreach (string part in lines[1].TrimStart('#', ' ').Split(' '))
        {
            string[] pair = part.Split('=');
            if (pair.Length != 2) continue;
            if (pair[0] == "enemies") enemies = int.Parse(pair[1], CultureInfo.InvariantCulture);
            else if (pair[0] == "frames") frames = int.Parse(pair[1], CultureInfo.InvariantCulture);
            else if (pair[0] == "dt") deltaTime = float.Parse(pair[1], CultureInfo.InvariantCulture);
        }
        string source = lines[2].StartsWith("# source=", StringComparison.Ordinal) ? lines[2].Substring("# source=".Length) : string.Empty;
        var trace = new EnemyAbTrace(enemies, frames, deltaTime, source);

        int frame = 0;
        for (int line = 0; line < lines.Length; line++)
        {
            if (lines[line].Length == 0 || lines[line][0] == '#') continue;
            if (frame >= frames) throw new FormatException("Trace has more frame lines than its header says.");
            string[] halves = lines[line].Split('|');
            if (halves.Length != 2) throw new FormatException($"Frame {frame}: missing '|'.");
            string[] player = halves[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] values = halves[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (player.Length != 2 || values.Length != enemies * 2)
                throw new FormatException($"Frame {frame}: expected 2 + {enemies * 2} numbers, found {player.Length} + {values.Length}.");
            trace._playerMm[frame * 2] = int.Parse(player[0], CultureInfo.InvariantCulture);
            trace._playerMm[frame * 2 + 1] = int.Parse(player[1], CultureInfo.InvariantCulture);
            int start = frame * enemies * 2;
            for (int i = 0; i < values.Length; i++)
                trace._enemyMm[start + i] = int.Parse(values[i], CultureInfo.InvariantCulture);
            frame++;
        }
        if (frame != frames) throw new FormatException($"Trace header says {frames} frames but the file has {frame}.");
        return trace;
    }

    // 시험용 합성 궤적: 정수 난수로 흩어 놓은 적이 가운데(플레이어)로 곧게 다가온다. 삼각함수를 쓰지 않아 기기와 무관하게 같다.
    public static EnemyAbTrace Synthetic(int seed, int enemyCount, int frameCount, float halfExtent = 24f, float speed = 3.5f, float deltaTime = 1f / 60f)
    {
        var trace = new EnemyAbTrace(enemyCount, frameCount, deltaTime, $"synthetic seed={seed} halfExtent={halfExtent} speed={speed}");
        uint state = unchecked((uint)seed) * 2654435761u + 1u;
        var x = new double[enemyCount];
        var z = new double[enemyCount];
        for (int i = 0; i < enemyCount; i++)
        {
            x[i] = NextUnit(ref state) * 2d * halfExtent - halfExtent;
            z[i] = NextUnit(ref state) * 2d * halfExtent - halfExtent;
        }
        for (int frame = 0; frame < frameCount; frame++)
        {
            trace.SetPlayer(frame, 0f, 0f);
            for (int i = 0; i < enemyCount; i++)
            {
                trace.SetEnemy(frame, i, (float)x[i], (float)z[i]);
                double distance = Math.Sqrt(x[i] * x[i] + z[i] * z[i]);
                // 플레이어 2m 앞에서 멈춘다(실제 적의 공격 거리와 비슷한 밀집을 만든다).
                if (distance <= 2d) continue;
                double step = Math.Min(speed * deltaTime, distance - 2d);
                x[i] -= x[i] / distance * step;
                z[i] -= z[i] / distance * step;
            }
        }
        return trace;
    }

    private static double NextUnit(ref uint state)
    {
        state = state * 1664525u + 1013904223u;
        return (state >> 8) / 16777216d;
    }
}
