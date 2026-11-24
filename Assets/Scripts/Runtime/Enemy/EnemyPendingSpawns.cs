using System;
using UnityEngine;

// An enemy announced by a warning ring: where it will appear, the variety drawn for it, how long until it appears,
// and the round (Tick) that announced it.
public struct EnemyPendingSpawn
{
    public Vector3 Position;
    public float Variation;
    public float Duration;
    public float Remaining;
    public int Tick;
    public int Marker;      // the warning ring showing it, -1 without one

    // 0 when announced, 1 when the enemy appears.
    public float Progress => Duration > 0f ? Mathf.Clamp01(1f - Remaining / Duration) : 1f;
    public float Elapsed => Mathf.Max(0f, Duration - Remaining);
}

// Enemies announced by warning rings and not yet spawned, in the order they were announced. Plain C# without Unity
// objects so the countdown rules can be tested on their own. The storage is reused: announcing, counting down and
// taking spawns allocate nothing once it is large enough for the busiest moment.
public sealed class EnemyPendingSpawns
{
    private EnemyPendingSpawn[] _items;
    private int _count;

    public EnemyPendingSpawns(int capacity = 32)
    {
        _items = new EnemyPendingSpawn[Math.Max(1, capacity)];
    }

    public int Count => _count;

    public EnemyPendingSpawn this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[index];
        }
    }

    public void Add(Vector3 position, float variation, float seconds, int tick, int marker)
    {
        if (_count == _items.Length)
            Array.Resize(ref _items, _items.Length * 2);
        float duration = Mathf.Max(0f, seconds);
        _items[_count++] = new EnemyPendingSpawn
        {
            Position = position, Variation = variation, Duration = duration, Remaining = duration, Tick = tick,
            Marker = marker
        };
    }

    // Counts every announcement down by the game time that passed. Not called while the game is paused, so the
    // announcements hold. A remainder too small to be a frame (rounding of summed frame times) counts as over.
    public void Advance(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        for (int i = 0; i < _count; i++)
        {
            float left = _items[i].Remaining - deltaTime;
            _items[i].Remaining = left > DueTolerance ? left : 0f;
        }
    }

    private const float DueTolerance = 0.0001f;

    // Takes the oldest announcement that is due (its countdown is over) or stale (announced in another round than
    // tick). The caller spawns a due one of this round and only hides the ring of a stale one.
    public bool TryTakeDue(int tick, out EnemyPendingSpawn spawn)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_items[i].Remaining > 0f && _items[i].Tick == tick)
                continue;
            spawn = _items[i];
            RemoveAt(i);
            return true;
        }
        spawn = default;
        return false;
    }

    // Takes the oldest announcement whatever its countdown, for cancelling them all.
    public bool TryTakeFirst(out EnemyPendingSpawn spawn)
    {
        if (_count == 0)
        {
            spawn = default;
            return false;
        }
        spawn = _items[0];
        RemoveAt(0);
        return true;
    }

    public void Clear() => _count = 0;

    private void RemoveAt(int index)
    {
        _count--;
        if (index < _count)
            Array.Copy(_items, index + 1, _items, index, _count - index);
        _items[_count] = default;
    }
}
