using System.Collections.Generic;
using UnityEngine;

// Enemy를 XZ 평면의 Uniform Grid에 배치해 주변 후보 검색 범위를 제한한다.
public sealed class EnemySpatialGrid
{
    private readonly Dictionary<Vector2Int, HashSet<EnemyAgent>> _cells = new();
    private readonly Dictionary<EnemyAgent, Vector2Int> _locations = new();
    private readonly float _cellSize;

    public EnemySpatialGrid(float cellSize)
    {
        _cellSize = Mathf.Max(0.5f, cellSize);
    }

    public void AddOrUpdate(EnemyAgent enemy)
    {
        if (enemy == null)
            return;

        Vector2Int next = ToCell(enemy.transform.position);
        if (_locations.TryGetValue(enemy, out Vector2Int current))
        {
            if (current == next)
                return;
            RemoveFromCell(enemy, current);
        }

        if (!_cells.TryGetValue(next, out HashSet<EnemyAgent> occupants))
        {
            occupants = new HashSet<EnemyAgent>();
            _cells.Add(next, occupants);
        }
        occupants.Add(enemy);
        _locations[enemy] = next;
    }

    public void Remove(EnemyAgent enemy)
    {
        if (enemy == null || !_locations.TryGetValue(enemy, out Vector2Int cell))
            return;
        RemoveFromCell(enemy, cell);
        _locations.Remove(enemy);
    }

    public void Query(Vector3 center, float radius, List<EnemyAgent> results)
    {
        results.Clear();
        float safeRadius = Mathf.Max(0f, radius);
        Vector2Int min = ToCell(center - new Vector3(safeRadius, 0f, safeRadius));
        Vector2Int max = ToCell(center + new Vector3(safeRadius, 0f, safeRadius));

        for (int x = min.x; x <= max.x; x++)
        for (int y = min.y; y <= max.y; y++)
        {
            if (!_cells.TryGetValue(new Vector2Int(x, y), out HashSet<EnemyAgent> occupants))
                continue;
            foreach (EnemyAgent enemy in occupants)
                if (enemy != null)
                    results.Add(enemy);
        }
    }

    public void Clear()
    {
        _cells.Clear();
        _locations.Clear();
    }

    private Vector2Int ToCell(Vector3 position) => new(
        Mathf.FloorToInt(position.x / _cellSize),
        Mathf.FloorToInt(position.z / _cellSize));

    private void RemoveFromCell(EnemyAgent enemy, Vector2Int cell)
    {
        if (!_cells.TryGetValue(cell, out HashSet<EnemyAgent> occupants))
            return;
        occupants.Remove(enemy);
        if (occupants.Count == 0)
            _cells.Remove(cell);
    }
}
