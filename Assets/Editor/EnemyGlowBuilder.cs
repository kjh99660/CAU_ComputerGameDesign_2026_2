using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Makes the enemy readable in a dark stage. Builds the mask of its glowing parts and the material that uses it
// (shader REBOOT/Enemy Lit), then puts that material on the gameplay prefab's skin.
//   - visor: the band of the face at eye height,
//   - chest light: a disc on the front of the upper chest,
//   - the light strips already painted on the armour (the saturated, bright texels of the colour texture).
// The parts are found on the mesh itself (which bone moves them, where they sit in the rest pose) and painted into
// the texture through the mesh's own UVs, so nothing is added to the enemy at run time. Running it again gives the
// same result. Nothing is taken from outside the project.
public static class EnemyGlowBuilder
{
    public const string MaskPath = "Assets/Prefabs/Enemy/EnemyHumanoid_Glow.png";
    public const string MaterialPath = "Assets/Prefabs/Enemy/EnemyHumanoid_Glow.mat";
    private const string BaseColorPath = "Assets/MeshyImports/EnemyHumanoid/meshy_basecolor.png";
    private const string ShaderName = "REBOOT/Enemy Lit";
    private const int MaskSize = 1024;

    // Provisional look values. Tune on the material afterwards; they are only written when the material is created.
    private const float GlowStrength = 6f;
    private const float RimStrength = 1.2f;
    private const float RimPower = 3f;

    [MenuItem("Tools/Game Demo Enemy/Apply Humanoid Enemy Glow")]
    public static void Apply()
    {
        Shader shader = Shader.Find(ShaderName) ?? throw new InvalidOperationException("Shader missing: " + ShaderName);
        var colour = AssetDatabase.LoadAssetAtPath<Texture2D>(BaseColorPath)
                     ?? throw new InvalidOperationException("Enemy colour texture missing: " + BaseColorPath);

        GameObject root = PrefabUtility.LoadPrefabContents(GameDemoHumanoidEnemyBuilder.PrefabPath);
        try
        {
            SkinnedMeshRenderer skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true)
                                       ?? throw new InvalidOperationException("Enemy prefab has no skinned mesh.");
            string report = WriteMask(root.transform, skin);

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "EnemyHumanoid_Glow" };
                Material source = skin.sharedMaterial;
                material.SetFloat("_Metallic", source != null && source.HasProperty("_Metallic") ? source.GetFloat("_Metallic") : 0f);
                material.SetFloat("_Smoothness", source != null && source.HasProperty("_Smoothness") ? source.GetFloat("_Smoothness") : 0.5f);
                material.SetColor("_GlowColor", Color.white);
                material.SetFloat("_GlowStrength", GlowStrength);
                material.SetFloat("_RimStrength", RimStrength);
                material.SetFloat("_RimPower", RimPower);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", colour);
            material.SetTexture("_GlowMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath));
            EditorUtility.SetDirty(material);

            skin.sharedMaterial = material;
            PrefabUtility.SaveAsPrefabAsset(root, GameDemoHumanoidEnemyBuilder.PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Enemy Glow] " + report);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void ApplyBatch()
    {
        try { Apply(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // Paints the glowing parts into a texture laid out like the colour texture. Returns what was found.
    private static string WriteMask(Transform root, SkinnedMeshRenderer skin)
    {
        Mesh mesh = skin.sharedMesh;
        Transform[] bones = skin.bones;
        Matrix4x4[] bind = mesh.bindposes;
        BoneWeight[] weights = mesh.boneWeights;
        Vector3[] vertices = mesh.vertices;
        Vector2[] uv = mesh.uv;
        int[] triangles = mesh.triangles;
        if (uv.Length != vertices.Length || weights.Length != vertices.Length)
            throw new InvalidOperationException("Enemy mesh has no UVs or bone weights.");

        // Rest-pose position of every vertex in the root's space, in metres (x right, y up, z forward).
        float scale = root.lossyScale.y;
        var position = new Vector3[vertices.Length];
        var boneName = new string[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            int bone = weights[i].boneIndex0;
            Vector3 world = bones[bone].localToWorldMatrix.MultiplyPoint3x4(bind[bone].MultiplyPoint3x4(vertices[i]));
            position[i] = root.InverseTransformPoint(world) * scale;
            boneName[i] = bones[bone].name;
        }

        Bounds head = BoundsOf(position, boneName, name => name.EndsWith(":Head", StringComparison.Ordinal) || name.Contains("HeadTop"));
        Bounds chest = BoundsOf(position, boneName, name => name.EndsWith(":Spine2", StringComparison.Ordinal) || name.EndsWith(":Spine1", StringComparison.Ordinal));
        if (head.size.y <= 0f || chest.size.y <= 0f)
            throw new InvalidOperationException("Could not find the head or chest of the enemy mesh.");

        // Visor: a band across the front of the face. Chest light: a disc on the front of the upper chest.
        // Whether a texel glows is decided from where it sits on the body in the rest pose, so the edges are a
        // straight band and a round disc and do not follow the triangles.
        float visorLow = head.min.y + head.size.y * 0.47f;
        float visorHigh = head.min.y + head.size.y * 0.60f;
        float visorFront = head.center.z + head.size.z * 0.08f;
        var chestCentre = new Vector2(chest.center.x, chest.min.y + chest.size.y * 0.62f);
        float chestRadius = chest.size.x * 0.2f;
        float chestFront = chest.center.z + chest.size.z * 0.15f;
        Func<Vector3, bool> inVisor = point => point.z > visorFront && point.y > visorLow && point.y < visorHigh;
        Func<Vector3, bool> inChestLight = point => point.z > chestFront &&
            Vector2.Distance(new Vector2(point.x, point.y), chestCentre) < chestRadius;

        var mask = new float[MaskSize * MaskSize];
        int visorTexels = 0, chestTexels = 0;
        for (int t = 0; t < triangles.Length; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            Vector3 normal = Vector3.Cross(position[b] - position[a], position[c] - position[a]).normalized;
            bool onHead = IsHead(boneName[a]) && IsHead(boneName[b]) && IsHead(boneName[c]);
            if (onHead && normal.z > 0.2f)
                visorTexels += Fill(mask, uv[a], uv[b], uv[c], position[a], position[b], position[c], inVisor);
            else if (!onHead && normal.z > 0.45f)
                chestTexels += Fill(mask, uv[a], uv[b], uv[c], position[a], position[b], position[c], inChestLight);
        }
        if (visorTexels == 0)
            throw new InvalidOperationException("No face texels matched the visor band; check the head bounds.");
        Dilate(mask, 1);

        // The light strips painted on the armour: bright and saturated texels of the colour texture.
        var colour = new Texture2D(2, 2, TextureFormat.RGB24, false);
        int painted = 0;
        try
        {
            if (!colour.LoadImage(File.ReadAllBytes(BaseColorPath)))
                throw new InvalidOperationException("Could not read " + BaseColorPath);
            Color32[] texels = colour.GetPixels32();
            int width = colour.width, height = colour.height;
            for (int y = 0; y < MaskSize; y++)
            for (int x = 0; x < MaskSize; x++)
            {
                Color32 texel = texels[y * height / MaskSize * width + x * width / MaskSize];
                int high = Mathf.Max(texel.r, Mathf.Max(texel.g, texel.b));
                int low = Mathf.Min(texel.r, Mathf.Min(texel.g, texel.b));
                if (high < 115 || high - low < 64) continue;
                mask[y * MaskSize + x] = Mathf.Max(mask[y * MaskSize + x], 0.85f);
                painted++;
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(colour); }

        var pixels = new Color32[mask.Length];
        for (int i = 0; i < mask.Length; i++)
        {
            byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01(mask[i]) * 255f);
            pixels[i] = new Color32(value, value, value, 255);
        }
        var texture = new Texture2D(MaskSize, MaskSize, TextureFormat.RGB24, false);
        try
        {
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(MaskPath, texture.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }

        AssetDatabase.ImportAsset(MaskPath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(MaskPath);
        if (importer.sRGBTexture || importer.wrapMode != TextureWrapMode.Clamp || importer.maxTextureSize != MaskSize)
        {
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = MaskSize;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
        return $"visor texels={visorTexels} chest texels={chestTexels} painted texels={painted} " +
               $"head={head.min:F2}..{head.max:F2} chest={chest.min:F2}..{chest.max:F2}";
    }

    private static bool IsHead(string name) => name.EndsWith(":Head", StringComparison.Ordinal) || name.Contains("HeadTop");

    private static Bounds BoundsOf(Vector3[] position, string[] boneName, Func<string, bool> match)
    {
        bool any = false;
        var bounds = new Bounds();
        for (int i = 0; i < position.Length; i++)
        {
            if (!match(boneName[i])) continue;
            if (!any) { bounds = new Bounds(position[i], Vector3.zero); any = true; }
            else bounds.Encapsulate(position[i]);
        }
        return bounds;
    }

    // Paints the texels of one triangle (given in UV space) whose rest position passes the test. Returns how many.
    private static int Fill(float[] mask, Vector2 a, Vector2 b, Vector2 c, Vector3 pa, Vector3 pb, Vector3 pc, Func<Vector3, bool> inside)
    {
        int painted = 0;
        a *= MaskSize; b *= MaskSize; c *= MaskSize;
        int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))) - 1, 0, MaskSize - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))) + 1, 0, MaskSize - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))) - 1, 0, MaskSize - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))) + 1, 0, MaskSize - 1);
        float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        if (Mathf.Abs(area) < 1e-6f) return 0;
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float w0 = ((b.x - p.x) * (c.y - p.y) - (b.y - p.y) * (c.x - p.x)) / area;
            float w1 = ((c.x - p.x) * (a.y - p.y) - (c.y - p.y) * (a.x - p.x)) / area;
            float w2 = 1f - w0 - w1;
            if (w0 < -0.02f || w1 < -0.02f || w2 < -0.02f) continue;
            if (!inside(pa * w0 + pb * w1 + pc * w2)) continue;
            if (mask[y * MaskSize + x] < 1f) painted++;
            mask[y * MaskSize + x] = 1f;
        }
        return painted;
    }

    // Grows the painted area by a few texels so the seams between UV islands do not show as dark lines.
    private static void Dilate(float[] mask, int texels)
    {
        var grown = new List<int>();
        for (int pass = 0; pass < texels; pass++)
        {
            grown.Clear();
            for (int y = 1; y < MaskSize - 1; y++)
            for (int x = 1; x < MaskSize - 1; x++)
            {
                int i = y * MaskSize + x;
                if (mask[i] > 0f) continue;
                if (mask[i - 1] > 0f || mask[i + 1] > 0f || mask[i - MaskSize] > 0f || mask[i + MaskSize] > 0f)
                    grown.Add(i);
            }
            foreach (int i in grown) mask[i] = 1f;
        }
    }
}
