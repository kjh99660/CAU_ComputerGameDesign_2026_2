using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// 검토용 강조 캡처. 배치 인자 -rebootHighlight <그룹 경로>(예: Env_RebootHub/ArenaProps_CC0)를 주면 모든 캡처에서
// 그 그룹의 자식마다 화면에 보이는 부분을 연두색 테두리로 감싸고 번호표를 붙인다. 번호와 이름, 보이는 화면은
// {tag}_highlight.md에 쓴다. 가림은 실제 깊이로 판정한다(다른 물체 뒤에 가린 부분에는 테두리가 없다).
// 마스크를 그리는 동안 렌더러 재질을 잠시 바꾸지만 끝나면 되돌리고, Scene은 저장하지 않는다.
public static partial class RebootLevelDiagnostics
{
    private static readonly Color32 HighlightColor = new Color32(110, 255, 70, 255);
    private static readonly Color32 BadgeFill = new Color32(10, 12, 20, 255);
    private static List<GameObject> _highlightItems;
    private static Dictionary<int, List<string>> _highlightSeen;

    // 5x7 숫자 글꼴(위 행부터).
    private static readonly string[] Digits =
    {
        "01110100011001110101110011000101110", "00100011000010000100001000010001110",
        "01110100010000100010001000100011111", "11110000010000101110000010000111110",
        "00010001100101010010111110001000010", "11111100001111000001000011000101110",
        "00110010001000011110100011000101110", "11111000010001000100010000100001000",
        "01110100011000101110100011000101110", "01110100011000101111000010001001100",
    };

    private static void BeginHighlight()
    {
        _highlightItems = null;
        _highlightSeen = null;
        string path = GetArg("-rebootHighlight");
        if (string.IsNullOrEmpty(path)) return;
        GameObject group = GameObject.Find(path);
        if (group == null)
        {
            Debug.LogWarning($"[RebootLevelDiagnostics] Highlight group not found: {path}");
            return;
        }
        _highlightItems = new List<GameObject>();
        foreach (Transform child in group.transform) _highlightItems.Add(child.gameObject);
        _highlightSeen = new Dictionary<int, List<string>>();
    }

    private static void EndHighlight(string outputDirectory, string tag, StringBuilder report)
    {
        if (_highlightItems == null) return;
        var text = new StringBuilder();
        text.AppendLine($"# Highlighted items ({tag})");
        text.AppendLine();
        text.AppendLine("| # | Object | Position (x, y, z) | Visible in |");
        text.AppendLine("| --- | --- | --- | --- |");
        for (int i = 0; i < _highlightItems.Count; i++)
        {
            GameObject item = _highlightItems[i];
            string seen = _highlightSeen.TryGetValue(i, out List<string> views) ? string.Join(", ", views) : "-";
            text.AppendLine($"| {i + 1} | {item.name} | {Fmt(item.transform.position)} | {seen} |");
        }
        File.WriteAllText(Path.Combine(outputDirectory, $"{tag}_highlight.md"), text.ToString());
        report.AppendLine($"Highlight legend: {tag}_highlight.md ({_highlightItems.Count} items).");
        _highlightItems = null;
        _highlightSeen = null;
    }

    private static void DrawHighlights(Camera camera, Texture2D image, string viewName)
    {
        int width = image.width, height = image.height;
        int[] owner = RenderHighlightMasks(camera, width, height, out List<(int index, RectInt box)> visible);

        Color32[] pixels = image.GetPixels32();
        bool[] ring = Dilate(owner, width, height, Mathf.Max(2, width / 640));
        for (int p = 0; p < pixels.Length; p++)
        {
            if (owner[p] != 0) pixels[p] = Color32.Lerp(pixels[p], HighlightColor, 0.18f);
            else if (ring[p]) pixels[p] = HighlightColor;
        }

        var placed = new List<RectInt>();
        foreach ((int index, RectInt box) in visible)
        {
            DrawBadge(pixels, width, height, index + 1, box, placed);
            if (!_highlightSeen.TryGetValue(index, out List<string> views))
                _highlightSeen[index] = views = new List<string>();
            views.Add(viewName);
        }
        image.SetPixels32(pixels);
        image.Apply();
    }

    // 모든 렌더러를 검은 무광으로 바꾸고, 강조 대상을 하나씩 흰색으로 그려 보이는 픽셀을 모은다.
    private static int[] RenderHighlightMasks(Camera camera, int width, int height, out List<(int index, RectInt box)> visible)
    {
        visible = new List<(int, RectInt)>();
        var owner = new int[width * height];
        // 가리는 쪽(검정)은 보통 재질처럼 앞면만, 강조 대상(흰색)은 양면으로 그린다(간판 같은 얇은 판은 양면 재질이다).
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        var black = new Material(unlit);
        black.SetColor("_BaseColor", Color.black);
        var white = new Material(unlit);
        white.SetColor("_BaseColor", Color.white);
        white.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

        var saved = new List<(Renderer renderer, Material[] materials)>();
        var disabled = new List<Renderer>();
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!r.enabled) continue;
            if (r is LineRenderer || r is TrailRenderer || r is ParticleSystemRenderer)
            {
                r.enabled = false;
                disabled.Add(r);
                continue;
            }
            saved.Add((r, r.sharedMaterials));
            r.sharedMaterials = Filled(black, r.sharedMaterials.Length);
        }
        bool fog = RenderSettings.fog;
        CameraClearFlags clearFlags = camera.clearFlags;
        Color background = camera.backgroundColor;
        var data = camera.GetComponent<UniversalAdditionalCameraData>();
        bool post = data != null && data.renderPostProcessing;
        RenderSettings.fog = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        if (data != null) data.renderPostProcessing = false;

        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var mask = new Texture2D(width, height, TextureFormat.RGB24, false);
        // 화면비가 캡처와 같도록 대상 텍스처를 먼저 걸고 시야 절두체를 계산한다.
        camera.targetTexture = rt;
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
        try
        {
            for (int i = 0; i < _highlightItems.Count; i++)
            {
                Renderer[] renderers = _highlightItems[i].GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
                if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;

                foreach (Renderer r in renderers) r.sharedMaterials = Filled(white, r.sharedMaterials.Length);
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                mask.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                RenderTexture.active = previous;
                foreach (Renderer r in renderers) r.sharedMaterials = Filled(black, r.sharedMaterials.Length);

                Color32[] px = mask.GetPixels32();
                int minX = width, minY = height, maxX = -1, maxY = -1;
                for (int p = 0; p < px.Length; p++)
                {
                    if (px[p].r < 128) continue;
                    owner[p] = i + 1;
                    int x = p % width, y = p / width;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
                if (maxX >= 0) visible.Add((i, new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1)));
            }
        }
        finally
        {
            foreach ((Renderer renderer, Material[] materials) in saved) renderer.sharedMaterials = materials;
            foreach (Renderer r in disabled) r.enabled = true;
            RenderSettings.fog = fog;
            camera.clearFlags = clearFlags;
            camera.backgroundColor = background;
            if (data != null) data.renderPostProcessing = post;
            camera.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(mask);
            Object.DestroyImmediate(black);
            Object.DestroyImmediate(white);
        }
        return owner;
    }

    private static Material[] Filled(Material material, int count)
    {
        var materials = new Material[Mathf.Max(1, count)];
        for (int i = 0; i < materials.Length; i++) materials[i] = material;
        return materials;
    }

    // 마스크 바깥으로 radius 픽셀 두께의 고리(마스크 안쪽은 제외).
    private static bool[] Dilate(int[] owner, int width, int height, int radius)
    {
        var horizontal = new bool[owner.Length];
        for (int p = 0; p < owner.Length; p++)
        {
            if (owner[p] == 0) continue;
            int x = p % width, row = p - x;
            for (int dx = -radius; dx <= radius; dx++)
            {
                int nx = x + dx;
                if (nx >= 0 && nx < width) horizontal[row + nx] = true;
            }
        }
        var result = new bool[owner.Length];
        for (int p = 0; p < horizontal.Length; p++)
        {
            if (!horizontal[p]) continue;
            int y = p / width;
            for (int dy = -radius; dy <= radius; dy++)
            {
                int ny = y + dy;
                if (ny >= 0 && ny < height) result[p + dy * width] = true;
            }
        }
        return result;
    }

    // 보이는 부분의 위쪽 가운데에 번호표를 붙인다. 다른 번호표와 겹치면 위로 밀고, 밀렸으면 지시선을 긋는다.
    private static void DrawBadge(Color32[] pixels, int width, int height, int number, RectInt box, List<RectInt> placed)
    {
        const int scale = 3, pad = 6, border = 2;
        string text = number.ToString();
        int w = text.Length * 5 * scale + (text.Length - 1) * scale + pad * 2;
        int h = 7 * scale + pad * 2;
        int cx = box.x + box.width / 2;
        int x = Mathf.Clamp(cx - w / 2, 2, width - w - 2);
        int y = box.yMax + 6;
        if (y + h > height - 2) y = Mathf.Max(2, box.y - h - 6);
        var rect = new RectInt(x, y, w, h);
        for (int attempt = 0; attempt < 8 && placed.Exists(o => o.Overlaps(rect)); attempt++)
            rect.y = Mathf.Min(height - h - 2, rect.y + h + 4);
        placed.Add(rect);

        if (rect.y > box.yMax + 6)
            for (int ly = box.yMax; ly < rect.y; ly++)
                for (int lx = cx - 1; lx <= cx; lx++)
                    Set(pixels, width, height, lx, ly, HighlightColor);

        for (int py = rect.y; py < rect.y + h; py++)
            for (int px = rect.x; px < rect.x + w; px++)
            {
                bool edge = px < rect.x + border || px >= rect.xMax - border || py < rect.y + border || py >= rect.yMax - border;
                Set(pixels, width, height, px, py, edge ? HighlightColor : BadgeFill);
            }

        for (int d = 0; d < text.Length; d++)
        {
            string glyph = Digits[text[d] - '0'];
            int gx = rect.x + pad + d * 6 * scale;
            for (int row = 0; row < 7; row++)
                for (int col = 0; col < 5; col++)
                {
                    if (glyph[row * 5 + col] != '1') continue;
                    for (int sy = 0; sy < scale; sy++)
                        for (int sx = 0; sx < scale; sx++)
                            Set(pixels, width, height, gx + col * scale + sx, rect.yMax - pad - 1 - (row * scale + sy), HighlightColor);
                }
        }
    }

    private static void Set(Color32[] pixels, int width, int height, int x, int y, Color32 color)
    {
        if (x >= 0 && x < width && y >= 0 && y < height) pixels[y * width + x] = color;
    }
}
