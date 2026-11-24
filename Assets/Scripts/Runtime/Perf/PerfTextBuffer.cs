using System;

// 미리 잡아 둔 char 배열에 글자와 숫자를 쓴다. 성능 오버레이가 몇 번씩 글을 고쳐도 문자열을 새로 만들지 않도록
// TMP_Text.SetCharArray에 그대로 넘긴다. 자리가 모자라면 넘치는 글자는 버린다. Unity API를 쓰지 않는다.
public sealed class PerfTextBuffer
{
    private const int MaximumDecimals = 6;
    private static readonly long[] Pow10 = { 1L, 10L, 100L, 1_000L, 10_000L, 100_000L, 1_000_000L };
    // 소수점 아래까지 정확히 셀 수 있는 한계(약 9e15). 넘으면 숫자 대신 n/a를 쓴다.
    private const double LargestExact = 9e15;

    private readonly char[] _chars;
    private readonly char[] _digits = new char[20];

    public PerfTextBuffer(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), "The text buffer needs room for at least one character.");
        _chars = new char[capacity];
    }

    public char[] Chars => _chars;
    public int Length { get; private set; }

    public void Clear() => Length = 0;

    public void Append(char value)
    {
        if (Length < _chars.Length)
            _chars[Length++] = value;
    }

    public void Append(string value)
    {
        if (value == null)
            return;
        for (int i = 0; i < value.Length; i++)
            Append(value[i]);
    }

    public void Append(long value)
    {
        if (value < 0)
        {
            Append('-');
            AppendDigits(unchecked((ulong)(-(value + 1)) + 1UL));
            return;
        }
        AppendDigits((ulong)value);
    }

    // 소수점 아래 decimals자리(0~6)로 반올림해 쓴다(0.5는 0에서 먼 쪽으로). 숫자가 아니거나 너무 크면 n/a.
    public void Append(double value, int decimals)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            Append("n/a");
            return;
        }
        decimals = Math.Max(0, Math.Min(MaximumDecimals, decimals));
        double scaled = Math.Round(Math.Abs(value) * Pow10[decimals], MidpointRounding.AwayFromZero);
        if (scaled >= LargestExact)
        {
            Append("n/a");
            return;
        }
        long whole = (long)scaled;
        if (value < 0d && whole != 0)
            Append('-');
        AppendDigits((ulong)(whole / Pow10[decimals]));
        if (decimals == 0)
            return;
        Append('.');
        long fraction = whole % Pow10[decimals];
        for (int place = decimals - 1; place >= 0; place--)
            Append((char)('0' + (int)(fraction / Pow10[place] % 10L)));
    }

    // 테스트와 진단용. 오버레이는 Chars와 Length를 그대로 쓴다.
    public override string ToString() => new string(_chars, 0, Length);

    private void AppendDigits(ulong value)
    {
        int count = 0;
        do
        {
            _digits[count++] = (char)('0' + (int)(value % 10UL));
            value /= 10UL;
        } while (value != 0UL);
        while (count > 0)
            Append(_digits[--count]);
    }
}
