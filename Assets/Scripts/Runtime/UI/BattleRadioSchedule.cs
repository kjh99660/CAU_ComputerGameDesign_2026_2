using System;

// 전투 중 무전을 언제 띄울지 정한다. Unity에 의존하지 않아 EditMode에서 검증한다.
// 줄마다 한 판에 한 번, 전투 시계(남은 초)가 그 줄의 시각을 위에서 아래로 지나가는 순간에만 뜬다.
// 그래서 그 시각보다 짧게 시작한 판(예: 12초 판의 50초 줄)에서는 뜨지 않는다.
// 한 번에 여러 시각을 지나가면 가장 늦은 시각(남은 초가 가장 작은 줄) 하나만 띄우고 나머지는 지나간 것으로 친다.
public sealed class BattleRadioSchedule
{
    private BattleRadioLine[] _lines = Array.Empty<BattleRadioLine>();
    private bool[] _played = Array.Empty<bool>();
    private float _previous = float.NaN;

    public int LineCount => _lines.Length;

    // 새 판을 시작한다(Ready). 대사 배열은 그때의 에셋 값을 쓴다. null이나 빈 배열이면 무전을 쓰지 않는다.
    public void Begin(BattleRadioLine[] lines)
    {
        _lines = lines ?? Array.Empty<BattleRadioLine>();
        if (_played.Length != _lines.Length) _played = new bool[_lines.Length];
        else Array.Clear(_played, 0, _played.Length);
        _previous = float.NaN;
    }

    // 전투 중 시계가 바뀔 때마다 부른다. 지금 띄울 줄의 번호를 돌려주고, 없으면 -1이다.
    // 판의 첫 관측은 기준만 잡는다(시작 시각과 같은 줄은 뜨지 않는다).
    public int Advance(float remainingSeconds)
    {
        if (float.IsNaN(_previous))
        {
            _previous = remainingSeconds;
            return -1;
        }
        float previous = _previous;
        _previous = remainingSeconds;
        if (remainingSeconds >= previous) return -1;
        int chosen = -1;
        for (int i = 0; i < _lines.Length; i++)
        {
            if (_played[i]) continue;
            float trigger = _lines[i].remainingSeconds;
            if (previous > trigger && remainingSeconds <= trigger)
            {
                _played[i] = true;
                if (chosen < 0 || trigger < _lines[chosen].remainingSeconds) chosen = i;
            }
        }
        return chosen;
    }

    public BattleRadioLine Line(int index) => _lines[index];
}
