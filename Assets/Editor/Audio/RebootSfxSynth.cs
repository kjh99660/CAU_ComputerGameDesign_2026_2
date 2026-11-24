using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// The two sound layers the recorded CC0 sounds do not have, made from code:
//   - HitSub: a deep thump laid under heavy blows and the special, the weight a recorded hit lacks on small speakers;
//   - PlayerGlitch: a digital break-up laid quietly under the player's hit, matching the screen glitch.
// Every other sound is a recorded CC0 sound in Assets/ThirdParty (see RebootSfxBuilder). Recipes use fixed seeds, so the
// same code makes the same sounds (the editor rewrites nothing when run again). Pure C# with no Unity types.
public static class RebootSfxSynth
{
    public const int SampleRate = 44100;
    private const double Tau = Math.PI * 2.0;

    // One generated file. Cue is the SfxCue it belongs to; a cue with several files picks one at random.
    public sealed class Sound
    {
        public readonly string Name;
        public readonly string Cue;
        public readonly float[] Samples;

        public Sound(string name, string cue, float[] samples)
        {
            Name = name;
            Cue = cue;
            Samples = samples;
        }

        public double Seconds => Samples.Length / (double)SampleRate;
    }

    public static List<Sound> BuildAll()
    {
        return new List<Sound>
        {
            Make("sfx_hit_sub_01", "HitSub", SubThump(78f, 36f, 0.09f, 0.34f)),
            Make("sfx_hit_sub_02", "HitSub", SubThump(70f, 32f, 0.11f, 0.38f)),
            Make("sfx_player_glitch_01", "PlayerGlitch", Glitch(401)),
            Make("sfx_player_glitch_02", "PlayerGlitch", Glitch(402)),
        };
    }

    // 16-bit mono PCM WAV.
    public static byte[] ToWav(float[] samples)
    {
        int dataBytes = samples.Length * 2;
        using (var stream = new MemoryStream(44 + dataBytes))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);
            foreach (float sample in samples)
            {
                float clamped = sample < -1f ? -1f : sample > 1f ? 1f : sample;
                writer.Write((short)Math.Round(clamped * 32767f));
            }
            writer.Flush();
            return stream.ToArray();
        }
    }

    private static Sound Make(string name, string cue, float[] samples) => new Sound(name, cue, Finish(samples));

    // A sine that drops fast in pitch and dies away; a little drive adds harmonics so it is felt on laptop speakers too.
    private static float[] SubThump(float startHz, float endHz, float decay, float seconds)
    {
        var x = new float[Count(seconds)];
        Osc osc = default;
        for (int i = 0; i < x.Length; i++)
        {
            float t = i / (float)SampleRate;
            float hz = endHz + (startHz - endHz) * Exp(t, 0.045f);
            x[i] = osc.Sine(hz) * Ramp(t, 0.002f) * Exp(t, decay);
        }
        return Saturate(x, 2.2f);
    }

    // Short segments of a square tone, a crushed noise or a gap, chosen at random.
    private static float[] Glitch(uint seed)
    {
        int n = Count(0.32f);
        var noise = new Noise(seed);
        var x = new float[n];
        Osc tone = default;
        float[] steps = { 220f, 330f, 440f, 660f, 880f, 1320f };
        int segmentEnd = 0, kind = 2, hold = 1;
        float hz = 440f, held = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            if (i >= segmentEnd)
            {
                segmentEnd = i + Count(0.016f + 0.03f * noise.Unit());
                float pick = noise.Unit();
                kind = pick < 0.45f ? 0 : pick < 0.8f ? 1 : 2;
                hz = steps[(int)(noise.Unit() * steps.Length) % steps.Length];
                hold = kind == 0 ? 4 : 6;
            }
            float value = kind == 0 ? (tone.Sine(hz) >= 0f ? 1f : -1f)
                : kind == 1 ? (float)Math.Round(noise.Next() * 3f) / 3f
                : 0f;
            if (i % hold == 0) held = value;
            x[i] = held * Exp(t, 0.14f);
        }
        return x;
    }

    private static float[] Saturate(float[] x, float drive)
    {
        Normalize(x);
        float scale = 1f / (float)Math.Tanh(drive);
        for (int i = 0; i < x.Length; i++) x[i] = (float)Math.Tanh(drive * x[i]) * scale;
        return x;
    }

    // Removes DC, trims the silent end, adds tiny fades against clicks and sets the peak to -1 dBFS.
    private static float[] Finish(float[] x)
    {
        float lastIn = 0f, lastOut = 0f;
        for (int i = 0; i < x.Length; i++)
        {
            float y = x[i] - lastIn + 0.9986f * lastOut;
            lastIn = x[i];
            lastOut = y;
            x[i] = y;
        }
        float peak = Peak(x);
        if (peak <= 1e-9f) return x;
        int end = x.Length;
        while (end > 1 && Math.Abs(x[end - 1]) < peak * 0.001f) end--;
        var trimmed = new float[end];
        Array.Copy(x, trimmed, end);
        int fadeIn = Math.Min(18, end / 4), fadeOut = Math.Min(Count(0.01f), end / 4);
        for (int i = 0; i < fadeIn; i++) trimmed[i] *= i / (float)fadeIn;
        for (int i = 0; i < fadeOut; i++) trimmed[end - 1 - i] *= i / (float)fadeOut;
        return Normalize(trimmed, 0.89f);
    }

    private static float[] Normalize(float[] x, float peak = 1f)
    {
        float max = Peak(x);
        if (max <= 1e-9f) return x;
        float scale = peak / max;
        for (int i = 0; i < x.Length; i++) x[i] *= scale;
        return x;
    }

    private static float Peak(float[] x)
    {
        float max = 0f;
        for (int i = 0; i < x.Length; i++) max = Math.Max(max, Math.Abs(x[i]));
        return max;
    }

    private static int Count(float seconds) => Math.Max(1, (int)(seconds * SampleRate));
    private static float Exp(float t, float tau) => t < 0f ? 0f : (float)Math.Exp(-t / tau);
    private static float Ramp(float t, float seconds) => t <= 0f ? 0f : t >= seconds ? 1f : t / seconds;

    // xorshift32 white noise.
    private struct Noise
    {
        private uint _state;

        public Noise(uint seed)
        {
            _state = seed * 2654435761u + 0x9E3779B9u;
            if (_state == 0) _state = 1;
        }

        public float Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state >> 8) * (2f / 16777216f) - 1f;
        }

        public float Unit() => (Next() + 1f) * 0.5f;
    }

    // Phase-accumulating sine oscillator.
    private struct Osc
    {
        private double _phase;

        public float Sine(double hz)
        {
            float value = (float)Math.Sin(_phase * Tau);
            _phase += hz / SampleRate;
            _phase -= Math.Floor(_phase);
            return value;
        }
    }
}
