using TMPro;
using UnityEngine;

// 대화 화면과 전투 중 무전 자막이 함께 쓰는 한글 TMP 폰트 하나. UI 테마의 한글 서체(IBM Plex Sans KR)를 쓰고,
// 없으면 나눔고딕으로 대체한다. 실행 중에 동적 폰트로 만들고, 마지막 사용자가 돌려주면 지운다.
// 전투 HUD의 영문 서체(Chakra Petch)에는 한글이 없어서 무전 자막도 이 폰트를 쓴다.
public static class UiDialogueFont
{
    private const string PrimaryFont = "Fonts/IBMPlexSansKR-Regular";
    private const string FallbackFont = "Fonts/NanumGothic-Regular";
    private static TMP_FontAsset _font;
    private static int _users;

    // 폰트를 빌린다. 같은 폰트를 돌려받으며, 쓰고 나면 Release를 한 번 부른다. 폰트를 못 만들면 null(빌린 것이 아님).
    public static TMP_FontAsset Acquire()
    {
        if (_font == null)
        {
            _users = 0;
            Font source = Resources.Load<Font>(PrimaryFont);
            if (source == null) source = Resources.Load<Font>(FallbackFont);
            if (source == null) return null;
            _font = TMP_FontAsset.CreateFontAsset(source);
            if (_font == null) return null;
            _font.name = source.name + " (dialogue, runtime)";
        }
        _users++;
        return _font;
    }

    public static void Release()
    {
        if (_users <= 0) return;
        _users--;
        if (_users > 0 || _font == null) return;
        TMP_FontAsset font = _font;
        _font = null;
        if (font.material != null) DestroyObject(font.material);
        if (font.atlasTextures != null)
            foreach (Texture2D atlas in font.atlasTextures)
                if (atlas != null) DestroyObject(atlas);
        DestroyObject(font);
    }

    private static void DestroyObject(Object target)
    {
        if (Application.isPlaying) Object.Destroy(target);
        else Object.DestroyImmediate(target);
    }
}
