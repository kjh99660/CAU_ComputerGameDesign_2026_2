using System.Collections.Generic;

// 활성 Enemy 목록을 중복 없이 유지하고 Hot Path에서 Scene 검색을 제거한다.
public sealed class EnemyRegistry
{
    private readonly List<EnemyAgent> _active = new();

    public IReadOnlyList<EnemyAgent> Active => _active;
    public int Count => _active.Count;

    public bool Add(EnemyAgent enemy)
    {
        if (enemy == null || _active.Contains(enemy))
            return false;
        _active.Add(enemy);
        return true;
    }

    public bool Remove(EnemyAgent enemy)
    {
        int index = _active.IndexOf(enemy);
        if (index < 0)
            return false;

        int last = _active.Count - 1;
        _active[index] = _active[last];
        _active.RemoveAt(last);
        return true;
    }

    public void Clear() => _active.Clear();
}
