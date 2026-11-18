using UnityEngine;

// 공격 후딜 캔슬.
// Attack1, Attack2, Charge_Release 상태가 시작된 뒤 cancelTime(초)이 지나면 "캔슬 가능"을 알린다.
//  - 이동 입력 → PlayerRuntime이 이동을 허용 → isRunning=true → (트랜지션) Run
//  - 공격 입력 (콤보 구간 밖) → PlayerRuntime이 AttackRestart 트리거 → (트랜지션) Attack1
// 실제로 캔슬되어 다른 상태로 넘어가는 순간 칼을 자동으로 칼집에 넣는다 (Sheathe 이벤트가 건너뛰어질 수 있으므로).
// 시간은 Animator 기준이라 Pause, Finishing 슬로모션, 상태 재생 속도(0.7배 등)가 자동으로 반영된다.
public class PlayerAttackCancel : MonoBehaviour, IPlayerCancelGate, IPlayerPresentation
{
    [SerializeField] private AttackHitData attack1;       // Attack1 상태에서 쓸 값
    [SerializeField] private AttackHitData attack2;       // Attack2 상태에서 쓸 값
    [SerializeField] private AttackHitData chargeAttack;  // Charge_Release 상태에서 쓸 값
    [SerializeField] private bool debugLog = false;       // 켜면 캔슬 가능 시점과 실제 캔슬 시점을 콘솔에 출력

    private Animator _animator;
    private SwordEventRelay _sword;
    private bool _allowed = true;          // 팀원 구조가 없는 씬에서는 항상 허용

    // 같은 공격 한 번(인스턴스)에 대해 한 번만 처리하기 위한 기록
    private int _trackedHash;              // 추적 중인 공격 상태
    private float _lastElapsed;            // 지난 프레임의 경과 시간 (줄어들면 같은 상태가 다시 시작된 것)
    private bool _loggedOpen;              // "캔슬 가능" 로그를 이미 찍었는지
    private bool _handledExit;             // 이 공격에서 빠져나가는 처리를 이미 했는지

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _sword = GetComponentInChildren<SwordEventRelay>(true);
    }

    // PlayerRuntime이 매 프레임 물어본다. (PlayerRuntime이 이 컴포넌트의 Update보다 먼저 실행되므로 그때그때 계산)
    public bool IsCancelOpen
    {
        get
        {
            if (!_allowed || _animator == null) return false;
            AnimatorStateInfo info = JudgedState();
            AttackHitData data = GetData(info);
            if (data == null || data.cancelTime < 0f) return false;
            return Elapsed(info) >= data.cancelTime;
        }
    }

    private void LateUpdate()
    {
        if (_animator == null) return;

        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        AttackHitData data = GetData(current);
        if (data == null)
        {
            _trackedHash = 0;
            return;
        }

        // 새 공격(같은 Attack1 재시작 포함)이면 기록 초기화.
        // 재시작은 상태 이름이 같으므로 "경과 시간이 줄어들었는지"로 판단한다. (Pause 중에는 Animator가 멈춰 그대로 유지)
        float elapsed = Elapsed(current);
        if (current.fullPathHash != _trackedHash || elapsed < _lastElapsed)
        {
            _trackedHash = current.fullPathHash;
            _loggedOpen = false;
            _handledExit = false;
        }
        _lastElapsed = elapsed;

        if (data.cancelTime < 0f) return;
        bool open = Elapsed(current) >= data.cancelTime;

        if (debugLog && open && !_loggedOpen)
        {
            _loggedOpen = true;
            Debug.Log($"[Cancel] {StateName(current)} 캔슬 가능 시작 ({Elapsed(current):F2}초)", this);
        }

        // 캔슬 가능한 상태에서 다른 상태로 넘어가기 시작한 순간 = 실제 캔슬 (또는 끝까지 재생 후 종료)
        if (open && !_handledExit && _animator.IsInTransition(0))
        {
            _handledExit = true;
            AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);

            // 콤보로 이어지는 경우(Attack2, 차지)는 칼을 넣지 않는다.
            bool comboContinue = next.IsName("Attack2") || next.IsName("Charge_Windup");
            if (!comboContinue && _sword != null)
                _sword.Sheathe();

            if (debugLog)
                Debug.Log($"[Cancel] {StateName(current)} → {StateName(next)} ({Elapsed(current):F2}초에 전환 시작)" +
                          (comboContinue ? "" : ", 칼 넣기"), this);
        }
    }

    // 판단 기준 상태: 공격 계열 상태로 넘어가는 중이면 넘어가는 쪽, 그 외(Run/Idle로 빠져나가는 중)면 떠나는 쪽.
    // → 콤보나 재시작으로 새 공격이 시작되면 즉시 다시 막히고, 캔슬로 Run에 가는 블렌드 동안은 계속 열려 있다.
    private AnimatorStateInfo JudgedState()
    {
        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        if (_animator.IsInTransition(0))
        {
            AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
            if (IsAttackFamily(next)) return next;
        }
        return current;
    }

    private AttackHitData GetData(AnimatorStateInfo info)
    {
        if (info.IsName("Attack1")) return attack1;
        if (info.IsName("Attack2")) return attack2;
        if (info.IsName("Charge_Release")) return chargeAttack;
        return null;
    }

    private static bool IsAttackFamily(AnimatorStateInfo info) =>
        info.IsName("Attack1") || info.IsName("Attack2") ||
        info.IsName("Charge_Windup") || info.IsName("Charge_Hold") || info.IsName("Charge_Release");

    private static float Elapsed(AnimatorStateInfo info) => info.normalizedTime * info.length;   // 상태 시작 후 지난 시간 (초)

    private static string StateName(AnimatorStateInfo info)
    {
        if (info.IsName("Attack1")) return "Attack1";
        if (info.IsName("Attack2")) return "Attack2";
        if (info.IsName("Charge_Release")) return "Charge_Release";
        if (info.IsName("Charge_Windup")) return "Charge_Windup";
        if (info.IsName("Idle")) return "Idle";
        if (info.IsTag("RunMotion")) return "Run";
        return "기타";
    }

    // ── 팀원 구조가 보내 주는 알림 (PlayerRuntime이 자동으로 찾아서 호출) ──
    public void ApplyState(GameState state) =>
        _allowed = state == GameState.Battle || state == GameState.Finishing;

    public void ResetPresentation()
    {
        _trackedHash = 0;
        _lastElapsed = 0f;
        _loggedOpen = false;
        _handledExit = false;
    }
}
