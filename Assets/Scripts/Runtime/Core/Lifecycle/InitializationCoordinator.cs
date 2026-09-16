using System.Collections;
using UnityEngine;

// 필수 시스템의 초기화를 순서대로 기다린 뒤 완료 요청을 발행한다.
public sealed class InitializationCoordinator : MonoBehaviour
{
    [SerializeField] private MonoBehaviour[] participants = new MonoBehaviour[0];

    private IGameEventBus _events;
    private bool _started;

    // 초기화 완료 요청을 보낼 이벤트 버스를 연결한다.
    public void Configure(IGameEventBus events)
    {
        _events = events;
    }

    // 참여 시스템을 확인하고 지정된 Tick의 초기화를 시작한다.
    public bool BeginInitialization(int tick)
    {
        if (_started || _events == null || !ValidateParticipants())
            return false;

        _started = true;
        StartCoroutine(Initialize(tick));
        return true;
    }

    // 등록된 모든 참여자가 초기화 계약을 구현했는지 확인한다.
    private bool ValidateParticipants()
    {
        if (participants == null || participants.Length == 0)
        {
            Debug.LogError("Initialization requires at least one participant.", this);
            return false;
        }

        foreach (MonoBehaviour participant in participants)
        {
            if (participant == null || !(participant is IRoundInitializable))
            {
                Debug.LogError("Every initialization participant must implement IRoundInitializable.", this);
                return false;
            }
        }
        return true;
    }

    // 참여 시스템을 순서대로 초기화하고 완료 요청을 큐에 넣는다. 하위 시스템들은 IRoundInitializable 상속받아 InitializeRound를 구현해야 한다.
    private IEnumerator Initialize(int tick)
    {
        foreach (MonoBehaviour behaviour in participants)
        {
            yield return ((IRoundInitializable)behaviour).InitializeRound(tick);
        }

        _events.EnqueueRequest(new InitializationCompletedRequest(tick));
    }
}
