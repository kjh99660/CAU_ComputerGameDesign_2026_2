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

    public EnemyPool(EnemyAgent prefab, EnemyRuntime owner, Transform root)
    {
        _prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
        _owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
        _root = root != null ? root : throw new ArgumentNullException(nameof(root));
    }

    public EnemyAgent Get()
    {
        EnemyAgent enemy = _available.Count > 0
            ? _available.Pop()
            : UnityEngine.Object.Instantiate(_prefab, _root);
        enemy.Initialize(_owner);
        return enemy;
    }

    public void Release(EnemyAgent enemy)
    {
        if (enemy == null)
            return;
        enemy.DeactivateForPool(_root);
        _available.Push(enemy);
    }
}
