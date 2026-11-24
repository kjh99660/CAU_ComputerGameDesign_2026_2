using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Builds what a destroyed enemy leaves behind and points CombatVfxSettings at it:
//   - the parts the robot breaks into: one mesh per body part, made by giving every triangle to the part of the bone
//     that moves it most, stored in its anchor bone's space in metres (EnemyDebrisSet);
//   - the "REBOOT/Enemy Remains" material, a copy of the enemy's glow material that can be eaten away;
//   - the enemy model, whose pooled copies hold the last pose of fallen enemies.
// Run again after the enemy model changes.
//   Unity -batchmode -executeMethod EnemyRemainsBuilder.BuildBatch -quit
public static class EnemyRemainsBuilder
{
    private const string PrefabPath = "Assets/Prefabs/Enemy/EnemyPrototype.prefab";
    private const string ModelPath = "Assets/MeshyImports/EnemyHumanoid/EmenyHumanoid.fbx";
    private const string GlowMaterialPath = "Assets/Prefabs/Enemy/EnemyHumanoid_Glow.mat";
    private const string Folder = "Assets/Prefabs/Enemy/Remains";
    public const string SetPath = Folder + "/EnemyHumanoidDebris.asset";
    public const string MaterialPath = Folder + "/EnemyHumanoid_Remains.mat";
    private const string SettingsPath = "Assets/Resources/CombatVfxSettings.asset";
    private const string ShaderName = "REBOOT/Enemy Remains";

    // Each part hangs on one bone (rig prefix left out). Every other bone joins the part of its nearest ancestor
    // that is an anchor, so fingers go with the forearm, toes with the shin and the lower spine with the pelvis.
    private static readonly string[] PartNames =
        { "Pelvis", "Chest", "Head", "LeftUpperArm", "LeftForearm", "RightUpperArm", "RightForearm", "LeftThigh", "LeftShin", "RightThigh", "RightShin" };
    private static readonly string[] AnchorBones =
        { "Hips", "Spine2", "Head", "LeftArm", "LeftForeArm", "RightArm", "RightForeArm", "LeftUpLeg", "LeftLeg", "RightUpLeg", "RightLeg" };

    [MenuItem("Tools/REBOOT Feel/Build Enemy Remains")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder);
        GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            SkinnedMeshRenderer skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skin == null || skin.sharedMesh == null) throw new IOException("No skinned mesh in " + PrefabPath);
            EnemyDebrisSet set = BuildParts(skin);
            Material material = BuildMaterial();
            Wire(set, material);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }
        AssetDatabase.SaveAssets();
    }

    public static void BuildBatch()
    {
        Build();
        Debug.Log("REBOOT enemy remains built in " + Folder);
    }

    private static EnemyDebrisSet BuildParts(SkinnedMeshRenderer skin)
    {
        Mesh source = skin.sharedMesh;
        Transform[] bones = skin.bones;
        int partCount = PartNames.Length;
        var anchorIndex = new int[partCount];
        for (int p = 0; p < partCount; p++)
        {
            anchorIndex[p] = -1;
            for (int b = 0; b < bones.Length && anchorIndex[p] < 0; b++)
                if (bones[b] != null && Short(bones[b].name) == AnchorBones[p]) anchorIndex[p] = b;
            if (anchorIndex[p] < 0) throw new IOException("The enemy rig has no bone named " + AnchorBones[p]);
        }
        // Which part each bone belongs to: its own anchor, else the nearest anchored ancestor.
        var partOfBone = new int[bones.Length];
        for (int b = 0; b < bones.Length; b++)
        {
            partOfBone[b] = 0;
            for (Transform t = bones[b]; t != null; t = t.parent)
            {
                int found = System.Array.FindIndex(AnchorBones, anchor => anchor == Short(t.name));
                if (found < 0) continue;
                partOfBone[b] = found;
                break;
            }
        }

        Vector3[] vertices = source.vertices;
        Vector3[] normals = source.normals;
        Vector2[] uvs = source.uv;
        BoneWeight[] weights = source.boneWeights;
        Matrix4x4[] bindposes = source.bindposes;
        int[] triangles = source.triangles;
        if (vertices.Length == 0 || weights.Length != vertices.Length)
            throw new IOException("Could not read the enemy mesh (" + vertices.Length + " vertices, " + weights.Length + " weights).");

        var partOfVertex = new int[vertices.Length];
        for (int v = 0; v < vertices.Length; v++) partOfVertex[v] = partOfBone[Dominant(weights[v])];

        var partTriangles = new List<int>[partCount];
        for (int p = 0; p < partCount; p++) partTriangles[p] = new List<int>();
        for (int t = 0; t < triangles.Length; t += 3)
        {
            int a = partOfVertex[triangles[t]], b = partOfVertex[triangles[t + 1]], c = partOfVertex[triangles[t + 2]];
            int part = b == c ? b : a;   // two out of three decide; otherwise the first corner's part
            partTriangles[part].Add(triangles[t]);
            partTriangles[part].Add(triangles[t + 1]);
            partTriangles[part].Add(triangles[t + 2]);
        }

        EnemyDebrisSet set = AssetDatabase.LoadAssetAtPath<EnemyDebrisSet>(SetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<EnemyDebrisSet>();
            AssetDatabase.CreateAsset(set, SetPath);
        }
        var existing = new Dictionary<string, Mesh>();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(SetPath))
            if (asset is Mesh mesh) existing[mesh.name] = mesh;

        var parts = new List<EnemyDebrisSet.Part>();
        var keep = new HashSet<string>();
        for (int p = 0; p < partCount; p++)
        {
            List<int> list = partTriangles[p];
            if (list.Count == 0) continue;
            Transform anchor = bones[anchorIndex[p]];
            float scale = anchor.lossyScale.x;
            Matrix4x4 toBone = bindposes[anchorIndex[p]];
            var remap = new Dictionary<int, int>();
            var partVertices = new List<Vector3>();
            var partNormals = new List<Vector3>();
            var partUvs = new List<Vector2>();
            var partTris = new int[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                int original = list[i];
                if (!remap.TryGetValue(original, out int index))
                {
                    index = partVertices.Count;
                    remap.Add(original, index);
                    partVertices.Add(toBone.MultiplyPoint3x4(vertices[original]) * scale);
                    partNormals.Add(normals.Length == vertices.Length ? toBone.MultiplyVector(normals[original]).normalized : Vector3.up);
                    partUvs.Add(uvs.Length == vertices.Length ? uvs[original] : Vector2.zero);
                }
                partTris[i] = index;
            }
            string meshName = "EnemyPart_" + PartNames[p];
            if (!existing.TryGetValue(meshName, out Mesh partMesh))
            {
                partMesh = new Mesh { name = meshName };
                AssetDatabase.AddObjectToAsset(partMesh, set);
            }
            partMesh.Clear();
            partMesh.indexFormat = partVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            partMesh.SetVertices(partVertices);
            partMesh.SetNormals(partNormals);
            partMesh.SetUVs(0, partUvs);
            partMesh.SetTriangles(partTris, 0);
            partMesh.RecalculateBounds();
            keep.Add(meshName);
            parts.Add(new EnemyDebrisSet.Part
            {
                name = PartNames[p],
                bone = anchor.name,
                mesh = partMesh,
                bakedScale = scale,
                radius = Mathf.Max(0.04f, partMesh.bounds.extents.magnitude * 0.55f)
            });
            Debug.Log("Enemy part " + PartNames[p] + ": " + partVertices.Count + " vertices, " + list.Count / 3 + " triangles");
        }
        foreach (KeyValuePair<string, Mesh> old in existing)
            if (!keep.Contains(old.Key)) Object.DestroyImmediate(old.Value, true);
        set.parts = parts.ToArray();
        EditorUtility.SetDirty(set);
        return set;
    }

    private static Material BuildMaterial()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null) throw new IOException("Shader " + ShaderName + " not found");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(MaterialPath) };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = shader;
        Material glow = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
        if (glow != null)
        {
            foreach (string texture in new[] { "_BaseMap", "_GlowMap" })
                if (glow.HasProperty(texture)) material.SetTexture(texture, glow.GetTexture(texture));
            foreach (string value in new[] { "_Metallic", "_Smoothness", "_GlowStrength", "_RimStrength", "_RimPower" })
                if (glow.HasProperty(value)) material.SetFloat(value, glow.GetFloat(value));
            if (glow.HasProperty("_GlowColor")) material.SetColor("_GlowColor", glow.GetColor("_GlowColor"));
        }
        material.enableInstancing = false;
        EditorUtility.SetDirty(material);
        return material;
    }

    // Fills only empty fields, so a choice made in the inspector is kept.
    private static void Wire(EnemyDebrisSet set, Material material)
    {
        var settings = AssetDatabase.LoadAssetAtPath<CombatVfxSettings>(SettingsPath);
        if (settings == null) throw new IOException("Missing " + SettingsPath);
        if (settings.debrisSet == null) settings.debrisSet = set;
        if (settings.remainsMaterial == null) settings.remainsMaterial = material;
        if (settings.remainsBodyModel == null) settings.remainsBodyModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        EditorUtility.SetDirty(settings);
    }

    private static int Dominant(BoneWeight weight)
    {
        int bone = weight.boneIndex0;
        float best = weight.weight0;
        if (weight.weight1 > best) { best = weight.weight1; bone = weight.boneIndex1; }
        if (weight.weight2 > best) { best = weight.weight2; bone = weight.boneIndex2; }
        if (weight.weight3 > best) bone = weight.boneIndex3;
        return bone;
    }

    private static string Short(string boneName)
    {
        int colon = boneName.LastIndexOf(':');
        return colon >= 0 ? boneName.Substring(colon + 1) : boneName;
    }
}
