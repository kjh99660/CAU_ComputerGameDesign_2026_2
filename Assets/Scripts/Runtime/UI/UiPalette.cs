using UnityEngine;

// REBOOT UI 색 규칙(시안 "D 고스트"). Editor 테마 빌더와 런타임 View가 같은 값을 쓴다.
// 흰색 = 기본 정보, 청록 = 강조/준비 완료(통신 허브 색), 빨강 = 위험/감점(보안 경고등 색).
public static class UiPalette
{
    public static readonly Color Primary = new(0.914f, 0.933f, 0.949f, 1f);  // #E9EEF2
    public static readonly Color Accent = new(0.098f, 0.941f, 0.878f, 1f);   // #19F0E0
    public static readonly Color Alert = new(1f, 0.231f, 0.231f, 1f);        // #FF3B3B
    public static readonly Color Ink = new(0.949f, 0.961f, 0.969f, 1f);      // #F2F5F7
    public static readonly Color Muted = new(0.553f, 0.592f, 0.639f, 1f);    // #8D97A3
    public static readonly Color Base = new(0.039f, 0.047f, 0.063f, 1f);     // #0A0C10

    public static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
