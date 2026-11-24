using System;

// Bookkeeping for the sound-effect voices, kept apart from AudioSource so it can be tested without audio.
// A voice is busy until its sound would have ended; a paused voice keeps the time it had left until it resumes.
public sealed class SfxVoiceTable
{
    private readonly int[] _cue;
    private readonly bool[] _gameplay;
    private readonly double[] _start;
    private readonly double[] _end;
    private readonly bool[] _paused;
    private readonly double[] _left;
    private readonly double[] _lastStart;

    public SfxVoiceTable(int voices, int cues)
    {
        voices = Math.Max(0, voices);
        _cue = new int[voices];
        _gameplay = new bool[voices];
        _start = new double[voices];
        _end = new double[voices];
        _paused = new bool[voices];
        _left = new double[voices];
        _lastStart = new double[Math.Max(0, cues)];
        for (int v = 0; v < voices; v++)
        {
            _cue[v] = -1;
            _start[v] = double.NegativeInfinity;
            _end[v] = double.NegativeInfinity;
        }
        for (int c = 0; c < _lastStart.Length; c++) _lastStart[c] = double.NegativeInfinity;
    }

    public int Count => _cue.Length;
    public int CueOf(int voice) => _cue[voice];
    public bool IsGameplay(int voice) => _gameplay[voice];
    public bool IsPaused(int voice) => _paused[voice];
    public bool IsBusy(int voice, double now) => _paused[voice] || _end[voice] > now;

    public int PausedCount
    {
        get
        {
            int count = 0;
            for (int v = 0; v < _paused.Length; v++)
                if (_paused[v]) count++;
            return count;
        }
    }

    public int ActiveCount(int cue, double now)
    {
        int count = 0;
        for (int v = 0; v < _cue.Length; v++)
            if (_cue[v] == cue && IsBusy(v, now)) count++;
        return count;
    }

    // Picks the voice that plays a cue now and marks it busy for the given length. Returns -1 when there is no voice
    // or the cue started less than minInterval ago. At the cue's voice limit its oldest voice starts over; when every
    // voice is busy the oldest playing voice is taken, and a voice held by Pause only as a last resort.
    public int Claim(int cue, bool gameplay, double now, double seconds, int maxVoices, double minInterval)
    {
        if (_cue.Length == 0 || cue < 0 || cue >= _lastStart.Length) return -1;
        if (now - _lastStart[cue] < minInterval) return -1;
        int free = -1, same = 0, oldestSame = -1;
        for (int v = 0; v < _cue.Length; v++)
        {
            if (!IsBusy(v, now))
            {
                if (free < 0) free = v;
                continue;
            }
            if (_cue[v] != cue) continue;
            same++;
            if (oldestSame < 0 || _start[v] < _start[oldestSame]) oldestSame = v;
        }
        int voice = same >= Math.Max(1, maxVoices) ? oldestSame : free >= 0 ? free : Oldest();
        _cue[voice] = cue;
        _gameplay[voice] = gameplay;
        _start[voice] = now;
        _end[voice] = now + Math.Max(0.0, seconds);
        _paused[voice] = false;
        _left[voice] = 0.0;
        _lastStart[cue] = now;
        return voice;
    }

    public void Pause(int voice, double now)
    {
        if (_paused[voice] || _end[voice] <= now) return;
        _paused[voice] = true;
        _left[voice] = _end[voice] - now;
    }

    public void Resume(int voice, double now)
    {
        if (!_paused[voice]) return;
        _paused[voice] = false;
        _end[voice] = now + _left[voice];
    }

    public void Release(int voice)
    {
        _paused[voice] = false;
        _end[voice] = double.NegativeInfinity;
    }

    private int Oldest()
    {
        int best = -1;
        for (int v = 0; v < _cue.Length; v++)
            if (!_paused[v] && (best < 0 || _start[v] < _start[best])) best = v;
        if (best >= 0) return best;
        for (int v = 0; v < _cue.Length; v++)
            if (best < 0 || _start[v] < _start[best]) best = v;
        return best;
    }
}

// Small deterministic random numbers for choosing clips and pitch.
public struct SfxRandom
{
    private uint _state;

    public SfxRandom(uint seed)
    {
        _state = seed != 0 ? seed : 0x6D2B79F5u;
    }

    // [0, 1)
    public float Value()
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return (_state >> 8) * (1f / 16777216f);
    }

    // An index in [0, count) that differs from last whenever there is more than one to choose from.
    public int Pick(int count, int last)
    {
        if (count <= 1) return 0;
        bool avoid = last >= 0 && last < count;
        int index = (int)(Value() * (avoid ? count - 1 : count));
        if (avoid && index >= last) index++;
        return Math.Min(index, count - 1);
    }
}
