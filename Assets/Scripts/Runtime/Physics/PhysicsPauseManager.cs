using System.Collections.Generic;
using UnityEngine;

// 활성 Rigidbody를 Registry로 관리하고 상태 전환 시 운동 상태를 스냅샷/복원한다.
public sealed class PhysicsPauseManager
{
    private readonly HashSet<Rigidbody> _bodies = new();
    private readonly Dictionary<Rigidbody, BodySnapshot> _snapshots = new();
    private GameState _state = GameState.Initializing;
    private int _tick;
    private bool _isSuspended = true;

    public int RegisteredCount => _bodies.Count;

    public void Register(Rigidbody body, int tick)
    {
        if (body == null || !_bodies.Add(body))
            return;

        if (_isSuspended)
            SuspendBody(body, tick);
    }

    public void Unregister(Rigidbody body)
    {
        if (body == null)
            return;

        _bodies.Remove(body);
        _snapshots.Remove(body);
    }

    public void ApplyState(GameState state, int tick)
    {
        if (tick < _tick)
            return;

        _state = state;
        _tick = tick;
        if (state == GameState.Battle || state == GameState.Finishing)
            ResumeAll(tick);
        else
            SuspendAll(tick);
    }

    public void ClearRound(int tick)
    {
        _tick = tick;
        _snapshots.Clear();
        RemoveDestroyedBodies();
        _isSuspended = _state != GameState.Battle && _state != GameState.Finishing;
    }

    private void SuspendAll(int tick)
    {
        _isSuspended = true;
        RemoveDestroyedBodies();
        foreach (Rigidbody body in _bodies)
            SuspendBody(body, tick);
    }

    private void ResumeAll(int tick)
    {
        _isSuspended = false;
        RemoveDestroyedBodies();
        foreach (Rigidbody body in _bodies)
        {
            if (!_snapshots.TryGetValue(body, out BodySnapshot snapshot) || snapshot.Tick != tick)
                continue;

            body.isKinematic = snapshot.IsKinematic;
            body.useGravity = snapshot.UseGravity;
            if (!body.isKinematic)
            {
                body.linearVelocity = snapshot.LinearVelocity;
                body.angularVelocity = snapshot.AngularVelocity;
            }
            _snapshots.Remove(body);
        }
    }

    private void SuspendBody(Rigidbody body, int tick)
    {
        if (body == null)
            return;

        if (!_snapshots.ContainsKey(body))
            _snapshots.Add(body, new BodySnapshot(body, tick));

        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.isKinematic = true;
    }

    private void RemoveDestroyedBodies()
    {
        _bodies.RemoveWhere(body => body == null);
        var destroyed = new List<Rigidbody>();
        foreach (KeyValuePair<Rigidbody, BodySnapshot> pair in _snapshots)
            if (pair.Key == null)
                destroyed.Add(pair.Key);
        foreach (Rigidbody body in destroyed)
            _snapshots.Remove(body);
    }

    private readonly struct BodySnapshot
    {
        public BodySnapshot(Rigidbody body, int tick)
        {
            LinearVelocity = body.isKinematic ? Vector3.zero : body.linearVelocity;
            AngularVelocity = body.isKinematic ? Vector3.zero : body.angularVelocity;
            IsKinematic = body.isKinematic;
            UseGravity = body.useGravity;
            Tick = tick;
        }

        public Vector3 LinearVelocity { get; }
        public Vector3 AngularVelocity { get; }
        public bool IsKinematic { get; }
        public bool UseGravity { get; }
        public int Tick { get; }
    }
}
