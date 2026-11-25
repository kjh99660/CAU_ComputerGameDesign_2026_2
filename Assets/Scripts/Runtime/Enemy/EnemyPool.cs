using System;
using System.Collections.Generic;
using UnityEngine;

// Enemy 인스턴스를 재사용하여 반복적인 Instantiate/Destroy를 피한다.
public sealed class EnemyPool
{
    private readonly EnemyAgent _prefab;
    private readonly EnemyRuntime _owner;
    private readonly Transform _root;
    private readonly Stack<EnemyAgent> _available = new();

    // Enemies this pool has instantiated so far (pooled or in play).
    public int CreatedCount { get; private set; }

    public EnemyPool(EnemyAgent prefab, EnemyRuntime owner, Transform root)
    {
        _prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
        _owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
        _root = root != null ? root : throw new ArgumentNullException(nameof(root));
    }

    public EnemyAgent Get()
    {
        if (!OptimizationManager.EnemyPooling)
            DiscardAvailable();
        EnemyAgent enemy = _available.Count > 0 ? _available.Pop() : Create();
        enemy.Initialize(_owner);
        return enemy;
    }

    // Creates enemies ahead of time, at most `budget` in this call, until the pool has created `total`. Returns true
    // once it has. A mass spawn in the middle of a round then takes its robots from the pool instead of instantiating
    // them all in one frame.
    public bool Prewarm(int total, int budget)
    {
        if (!OptimizationManager.EnemyPooling || !OptimizationManager.EnemyPrewarming)
            return true;
        for (int i = 0; i < budget && CreatedCount < total; i++)
        {
            EnemyAgent enemy = Create();
            enemy.Initialize(_owner);
            enemy.DeactivateForPool(_root);
            _available.Push(enemy);
        }
        return CreatedCount >= total;
    }

    private EnemyAgent Create()
    {
        CreatedCount++;
        return UnityEngine.Object.Instantiate(_prefab, _root);
    }

    public void Release(EnemyAgent enemy)
    {
        if (enemy == null)
            return;
        enemy.DeactivateForPool(_root);
        if (OptimizationManager.EnemyPooling)
            _available.Push(enemy);
        else
        {
            DestroyEnemy(enemy);
            DiscardAvailable();
        }
    }

    private void DiscardAvailable()
    {
        while (_available.Count > 0)
            DestroyEnemy(_available.Pop());
    }

    private void DestroyEnemy(EnemyAgent enemy)
    {
        CreatedCount--;
        UnityEngine.Object.Destroy(enemy.gameObject);
    }
}
