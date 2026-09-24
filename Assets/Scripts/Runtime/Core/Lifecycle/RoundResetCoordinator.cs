using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 참여 시스템의 생산을 멈추고 Reset 완료까지 순서를 관리한다.
public sealed class RoundResetCoordinator : MonoBehaviour
{
    [SerializeField] private MonoBehaviour[] participants = new MonoBehaviour[0];

    private bool _running;
    private bool _producersStopped;

    public void RegisterParticipant(MonoBehaviour participant)
    {
        if (participant == null || !(participant is IRoundResettable))
        {
            Debug.LogError("A reset participant must implement IRoundResettable.", this);
            return;
        }

        if (participants == null)
            participants = new MonoBehaviour[0];
        foreach (MonoBehaviour registered in participants)
            if (registered == participant)
                return;

        var expanded = new List<MonoBehaviour>(participants) { participant };
        participants = expanded.ToArray();
    }

    // Reset 전에 모든 참여 시스템의 새 이벤트 생산을 차단한다.
    public bool StopProducers()
    {
        if (_running || !ValidateParticipants())
            return false;

        try
        {
            foreach (MonoBehaviour behaviour in participants)
                ((IRoundResettable)behaviour).StopProducing();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            return false;
        }

        _producersStopped = true;
        return true;
    }

    // 생산 중지 후 지정된 Tick의 Reset 절차를 시작한다.
    public bool BeginReset(int tick, Action<int> completed)
    {
        if (_running || !_producersStopped || completed == null)
            return false;

        _producersStopped = false;
        _running = true;
        StartCoroutine(Reset(tick, completed));
        return true;
    }

    // 등록된 모든 참여자가 Reset 계약을 구현했는지 확인한다.
    private bool ValidateParticipants()
    {
        if (participants == null || participants.Length == 0)
        {
            Debug.LogError("Round reset requires at least one participant.", this);
            return false;
        }

        foreach (MonoBehaviour participant in participants)
        {
            if (participant == null || !(participant is IRoundResettable))
            {
                Debug.LogError("Every reset participant must implement IRoundResettable.", this);
                return false;
            }
        }
        return true;
    }

    // 참여 시스템을 순서대로 초기화하고 완료를 보고한다.
    private IEnumerator Reset(int tick, Action<int> completed)
    {
        foreach (MonoBehaviour behaviour in participants)
        {
            yield return ((IRoundResettable)behaviour).ResetRound(tick);
        }

        _running = false;
        completed(tick);
    }
}
