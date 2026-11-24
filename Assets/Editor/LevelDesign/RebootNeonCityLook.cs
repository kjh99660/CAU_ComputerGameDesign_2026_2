using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// How the city stage looks. The kit is flat-coloured and was lit like a cartoon; the characters are textured.
// This pass gives the city real surfaces (gloss, relief, wear, wet ground), night lighting from street lamps and
// signs, reflections, haze and a film-like grade, so both read as one picture.
// Nothing in the kit is edited: the stage gets its own materials, and they are assigned in this scene only.
// Every number here is an art value and provisional.
public static partial class RebootNeonCityBuilder
{
    private const string LookRoot = "Assets/LevelDesign/NeonCity";
    private const string KitMaterials = "Assets/LowPolySciFiCity/Materials/";
    private const string CityShader = "REBOOT/City Lit";

    private static void ApplyLook(Scene scene, Transform stage, StringBuilder report)
    {
        foreach (string folder in new[] { "Materials", "Textures", "Settings", "Probes" })
            EnsureFolder(LookRoot + "/" + folder);

        Texture2D relief = BuildReliefTexture();
        Texture2D wear = BuildWearTexture();
        Dictionary<Material, Material> swap = BuildMaterials(relief, wear);
        int swapped = 0;
        foreach (Renderer renderer in stage.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] != null && swap.TryGetValue(materials[i], out Material replacement))
                {
                    materials[i] = replacement;
                    changed = true;
                }
            if (!changed)
                continue;
            renderer.sharedMaterials = materials;
            swapped++;
        }
        report.AppendLine("gave " + swapped + " renderers the stage materials (" + swap.Count + " materials)");

        Light moon = BuildLights(scene, stage, report);
        ApplySkyAndFog(moon);
        ApplyPostProcessing(scene);
        BakeReflections(stage, report);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ---------------------------------------------------------------- textures (made here, no outside source)
    // Noise that repeats after 'period' cells, so the texture tiles without a seam.
    private static float Lattice(int x, int y, int period, int seed)
    {
        x = ((x % period) + period) % period;
        y = ((y % period) + period) % period;
        uint hash = (uint)(x * 374761393 + y * 668265263 + seed * 362437);
        hash = (hash ^ (hash >> 13)) * 1274126177u;
        hash ^= hash >> 16;
        return (hash & 0xFFFFFF) / 16777215f;
    }

    private static float Noise(float u, float v, int period, int seed)
    {
        float x = u * period, y = v * period;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
        fy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
        float a = Mathf.Lerp(Lattice(x0, y0, period, seed), Lattice(x0 + 1, y0, period, seed), fx);
        float b = Mathf.Lerp(Lattice(x0, y0 + 1, period, seed), Lattice(x0 + 1, y0 + 1, period, seed), fx);
        return Mathf.Lerp(a, b, fy);
    }

    private static float Layered(float u, float v, int period, int octaves, float gain, int seed)
    {
        float sum = 0f, weight = 1f, total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Noise(u, v, period, seed + i * 17) * weight;
            total += weight;
            weight *= gain;
            period *= 2;
        }
        return sum / total;
    }

    // Fine relief of a cast or painted surface: pores, dents, a slight waviness.
    private static Texture2D BuildReliefTexture()
    {
        const int size = 512;
        var height = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float broad = Layered(u, v, 5, 3, 0.5f, 11);
                float fine = Layered(u, v, 24, 4, 0.6f, 29);
                float pores = Mathf.Pow(Layered(u, v, 64, 2, 0.5f, 53), 3f);
                height[x + y * size] = broad * 0.22f + fine * 0.5f - pores * 0.18f;
            }
        var pixels = new Color[size * size];
        const float strength = 8f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = height[(x + 1) % size + y * size] - height[(x + size - 1) % size + y * size];
                float dy = height[x + ((y + 1) % size) * size] - height[x + ((y + size - 1) % size) * size];
                Vector3 normal = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                pixels[x + y * size] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f);
            }
        return WriteLookTexture("T_NeonCity_Relief", pixels, size, true);
    }

    // R: where the surface is clean (fine grain). G: broad stains. B: where water stands on flat ground.
    private static Texture2D BuildWearTexture()
    {
        const int size = 512;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float clean = Mathf.Clamp01((Layered(u, v, 14, 5, 0.58f, 101) - 0.30f) * 2.4f);
                float streak = Layered(u * 1f, v * 0.25f, 40, 3, 0.5f, 163);   // stretched: run-off marks
                clean = Mathf.Clamp01(clean * Mathf.Lerp(0.75f, 1.1f, streak));
                float stain = Mathf.SmoothStep(0.25f, 0.75f, Layered(u, v, 3, 4, 0.5f, 211));
                float puddle = Layered(u, v, 4, 3, 0.45f, 307);
                pixels[x + y * size] = new Color(clean, stain, puddle, 1f);
            }
        return WriteLookTexture("T_NeonCity_Wear", pixels, size, false);
    }

    private static Texture2D WriteLookTexture(string name, Color[] pixels, int size, bool normalMap)
    {
        string path = LookRoot + "/Textures/" + name + ".png";
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        texture.SetPixels(pixels);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------------------------------------------------------- materials
    private static Dictionary<Material, Material> BuildMaterials(Texture2D relief, Texture2D wear)
    {
        var swap = new Dictionary<Material, Material>();
        Shader city = Shader.Find(CityShader);
        if (city == null)
            throw new InvalidOperationException("Shader '" + CityShader + "' did not compile.");

        // name, saturation, brightness, gloss clean, gloss worn, relief, wear on colour, wetness, puddle cover
        Surface("M_LP_SciFi_Main", "M_NeonCity_Walls", 0.52f, 0.86f, 0.44f, 0.18f, 0.30f, 0.34f, 0.60f, 0.25f);
        Surface("M_LP_SciFi_Floor", "M_NeonCity_Ground", 0.55f, 0.62f, 0.46f, 0.22f, 0.35f, 0.42f, 1.00f, 0.40f);
        Surface("M_LP_SciFi_Food", "M_NeonCity_Goods", 0.85f, 1.00f, 0.45f, 0.25f, 0.25f, 0.15f, 0.00f, 0.00f);
        Surface("M_LP_SciFi_Ads", "M_NeonCity_Posters", 0.90f, 1.00f, 0.55f, 0.35f, 0.15f, 0.12f, 0.00f, 0.00f);

        // Signs, windows and lamp strips glow harder, so bloom and reflections can pick them up.
        foreach (string guid in AssetDatabase.FindAssets("M_LP_SciFi_Emissive t:Material", new[] { KitMaterials.TrimEnd('/') }))
            Glow(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)), 3.2f);
        foreach (string name in new[] { "M_LP_SciFi_BgBuilding_01", "M_LP_SciFi_BgBuilding_02", "M_LP_SciFi_BgBuilding_03" })
            Glow(AssetDatabase.LoadAssetAtPath<Material>(KitMaterials + name + ".mat"), 2.4f);

        var glass = AssetDatabase.LoadAssetAtPath<Material>(KitMaterials + "M_LP_SciFi_Glass.mat");
        if (glass != null)
        {
            var copy = new Material(glass);
            copy.SetColor("_BaseColor", new Color(0.20f, 0.30f, 0.36f, 0.38f));
            copy.SetFloat("_Smoothness", 0.96f);
            copy.SetFloat("_Metallic", 0.1f);
            swap[glass] = SaveLookMaterial("M_NeonCity_Glass", copy);
        }
        return swap;

        void Surface(string source, string name, float saturation, float brightness, float gloss, float glossWorn,
            float reliefStrength, float wearOnColour, float wetness, float puddles)
        {
            var kit = AssetDatabase.LoadAssetAtPath<Material>(KitMaterials + source + ".mat");
            if (kit == null)
                return;
            var material = new Material(city);
            material.SetTexture("_BaseMap", kit.GetTexture("_BaseMap"));
            material.SetTextureScale("_BaseMap", kit.GetTextureScale("_BaseMap"));
            material.SetTextureOffset("_BaseMap", kit.GetTextureOffset("_BaseMap"));
            material.SetColor("_BaseColor", kit.GetColor("_BaseColor"));
            material.SetFloat("_Saturation", saturation);
            material.SetFloat("_Brightness", brightness);
            material.SetFloat("_Smoothness", gloss);
            material.SetFloat("_SmoothnessWorn", glossWorn);
            material.SetTexture("_DetailNormal", relief);
            material.SetFloat("_DetailScale", 0.8f);
            material.SetFloat("_DetailStrength", reliefStrength);
            material.SetTexture("_GrimeMap", wear);
            material.SetFloat("_GrimeScale", 0.11f);
            material.SetFloat("_GrimeStrength", wearOnColour);
            material.SetFloat("_Wetness", wetness);
            material.SetFloat("_PuddleScale", 0.045f);
            material.SetFloat("_PuddleCover", puddles);
            material.enableInstancing = true;
            swap[kit] = SaveLookMaterial(name, material);
        }

        void Glow(Material kit, float boost)
        {
            if (kit == null)
                return;
            var copy = new Material(kit);
            copy.SetColor("_EmissionColor", kit.GetColor("_EmissionColor") * boost);
            copy.SetFloat("_Smoothness", 0.7f);
            // The copy keeps the kit's keyword and emissive flags; URP drops the glow of a material without them.
            copy.EnableKeyword("_EMISSION");
            swap[kit] = SaveLookMaterial(kit.name.Replace("M_LP_SciFi_", "M_NeonCity_"), copy);
        }
    }

    // Writes over an existing asset in place, so its GUID and the references to it stay.
    private static Material SaveLookMaterial(string name, Material material)
    {
        string path = LookRoot + "/Materials/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        existing.shader = material.shader;
        existing.CopyPropertiesFromMaterial(material);
        existing.shaderKeywords = material.shaderKeywords;
        existing.renderQueue = material.renderQueue;
        existing.globalIlluminationFlags = material.globalIlluminationFlags;
        existing.enableInstancing = material.enableInstancing;
        EditorUtility.SetDirty(existing);
        UnityEngine.Object.DestroyImmediate(material);
        return existing;
    }

    // ---------------------------------------------------------------- lights
    // Places where a fight is most likely: their nearest street lamp casts shadows. The rest light without.
    private static readonly Vector3[] ShadowLampSpots =
    {
        new(0.5f, 0f, -15f), new(0.5f, 0f, 2f), new(-18f, 0f, 2f), new(15f, 0f, -15f), new(-18f, 0f, -15f), new(3f, 0f, 20f),
    };

    private static readonly Vector3[] WorkLightSpots =
    {
        new(-10f, 9.4f, 7.5f), new(10f, 9.4f, 3f), new(0.5f, 9.2f, 6.2f),           // west terrace, east terrace, bridge
        new(18f, 12.6f, -5f), new(30f, 12.6f, -9f), new(-12.5f, 12.6f, -2f),         // third-storey roofs
        new(24f, 12.4f, -6.7f), new(11.5f, 6.4f, 11.5f),                             // east bridge, north landing
        new(3.75f, 6.2f, -5.5f), new(-16.25f, 6.2f, 1.25f),                          // half-way landings of the new stairs
    };

    private static Light BuildLights(Scene scene, Transform stage, StringBuilder report)
    {
        // The one directional light becomes moonlight and sky glow: weak and cool, it only shapes forms and
        // gives every character a soft shadow. The street is lit by its own lamps and signs.
        Light moon = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            var light = root.GetComponent<Light>();
            if (light != null && light.type == LightType.Directional)
                moon = light;
        }
        if (moon != null)
        {
            moon.color = new Color(0.62f, 0.74f, 1f);
            moon.intensity = 0.62f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.9f;
            moon.transform.rotation = Quaternion.Euler(54f, 212f, 0f);

            // The glow of the city itself, from the other side of the sky and without shadows. It puts an edge on
            // whoever stands between two lamps, so a dark enemy never melts into a dark street.
            var glowObject = new GameObject("City Glow");
            SceneManager.MoveGameObjectToScene(glowObject, scene);
            glowObject.transform.SetParent(stage, true);
            glowObject.transform.rotation = Quaternion.Euler(32f, 30f, 0f);
            var glow = glowObject.AddComponent<Light>();
            glow.type = LightType.Directional;
            glow.color = new Color(0.56f, 0.50f, 0.92f);
            glow.intensity = 0.34f;
            glow.shadows = LightShadows.None;
        }

        Transform lightRoot = stage.Find("Light");
        if (lightRoot == null)
            return moon;

        // The demo's coloured fill lights stay where its author put them, toned down: they are the glow of the
        // signs on the walls, not floodlights.
        int fills = 0;
        foreach (Light light in lightRoot.GetComponentsInChildren<Light>(true))
        {
            light.color = Color.Lerp(light.color, Color.white, 0.18f);
            light.intensity *= 0.7f;
            light.range = Mathf.Min(light.range, 16f);
            light.shadows = LightShadows.None;
            fills++;
        }

        // Every lamp post gets a real lamp.
        var lamps = new List<Transform>();
        foreach (Transform child in lightRoot)
            if (child.name.StartsWith("SM_Floor_Light", StringComparison.Ordinal))
                lamps.Add(child);
        var shadowed = new HashSet<Transform>();
        foreach (Vector3 spot in ShadowLampSpots)
        {
            Transform nearest = lamps.OrderBy(lamp => (lamp.position - spot).sqrMagnitude).FirstOrDefault();
            if (nearest != null)
                shadowed.Add(nearest);
        }
        foreach (Transform lamp in lamps)
        {
            var lampObject = new GameObject("LampLight");
            lampObject.transform.SetParent(lamp, false);
            lampObject.transform.localPosition = new Vector3(1.2f, 3.85f, 0f);   // under the head of the arm
            lampObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var light = lampObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1f, 0.80f, 0.56f);
            light.intensity = 70f;
            light.range = 13f;
            light.spotAngle = 118f;
            light.innerSpotAngle = 64f;
            light.shadows = shadowed.Contains(lamp) ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = 0.85f;
        }
        // The upper floors had no lamps of their own: work lights over the terraces, the bridge, the roofs and
        // the two new stairs, so a fight up there is as readable as one in the street.
        var workLights = new GameObject("WorkLights");
        workLights.transform.SetParent(lightRoot, false);
        foreach (Vector3 position in WorkLightSpots)
        {
            var lightObject = new GameObject("WorkLight");
            lightObject.transform.SetParent(workLights.transform, false);
            lightObject.transform.position = position;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.78f, 0.90f, 1f);
            light.intensity = 11f;
            light.range = 10f;
            light.shadows = LightShadows.None;
        }
        report.AppendLine("lights: moon, " + fills + " sign fills, " + lamps.Count + " street lamps (" + shadowed.Count + " with shadows), " + WorkLightSpots.Length + " work lights on the upper floors");
        return moon;
    }

    // ---------------------------------------------------------------- sky, ambient, haze
    private static void ApplySkyAndFog(Light moon)
    {
        var sky = new Material(Shader.Find("REBOOT/Night Sky"));
        sky.SetColor("_Zenith", new Color(0.010f, 0.014f, 0.034f));
        sky.SetColor("_Horizon", new Color(0.150f, 0.105f, 0.235f));
        sky.SetColor("_Ground", new Color(0.020f, 0.020f, 0.034f));
        sky.SetFloat("_GlowHeight", 0.26f);
        RenderSettings.skybox = SaveLookMaterial("M_NeonCity_Sky", sky);
        RenderSettings.sun = moon;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.22f, 0.28f, 0.46f);
        RenderSettings.ambientEquatorColor = new Color(0.25f, 0.21f, 0.37f);
        RenderSettings.ambientGroundColor = new Color(0.11f, 0.10f, 0.14f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 1f;
        // Haze: distant buildings sink into the night, and the street reads in layers.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.070f, 0.075f, 0.135f);
        RenderSettings.fogDensity = 0.011f;
        DynamicGI.UpdateEnvironment();
    }

    // ---------------------------------------------------------------- grade
    private static void ApplyPostProcessing(Scene scene)
    {
        string path = LookRoot + "/Settings/VP_NeonCity.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        for (int i = profile.components.Count - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(profile.components[i], true);
        profile.components.Clear();

        T Add<T>() where T : VolumeComponent
        {
            T component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
        var bloom = Add<Bloom>();
        bloom.threshold.Override(1.0f);
        bloom.intensity.Override(0.72f);
        bloom.scatter.Override(0.72f);
        bloom.highQualityFiltering.Override(true);
        var color = Add<ColorAdjustments>();
        color.postExposure.Override(0.9f);
        color.contrast.Override(10f);
        color.saturation.Override(-6f);
        var balance = Add<WhiteBalance>();
        balance.temperature.Override(-6f);
        var tones = Add<ShadowsMidtonesHighlights>();
        tones.shadows.Override(new Vector4(0.93f, 0.98f, 1.10f, 0f));
        tones.highlights.Override(new Vector4(1.04f, 1.0f, 0.96f, 0f));
        var vignette = Add<Vignette>();
        vignette.intensity.Override(0.24f);
        vignette.smoothness.Override(0.42f);
        var grain = Add<FilmGrain>();
        grain.type.Override(FilmGrainLookup.Medium1);
        grain.intensity.Override(0.16f);
        grain.response.Override(0.8f);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            var volume = root.GetComponent<Volume>();
            if (volume != null && volume.isGlobal)
                volume.sharedProfile = profile;
            var camera = root.GetComponent<Camera>();
            if (camera != null && camera.CompareTag("MainCamera"))
            {
                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
        }
    }

    // ---------------------------------------------------------------- reflections
    // name, centre, size. Each box covers one street so wet ground and glass mirror what is really next to them.
    private static readonly (string name, Vector3 centre, Vector3 size)[] ReflectionBoxes =
    {
        ("SouthRoad", new Vector3(3f, 4f, -17f), new Vector3(70f, 16f, 18f)),
        ("CentralAlley", new Vector3(0.5f, 4f, 2.5f), new Vector3(11f, 16f, 26f)),
        ("WestRoad", new Vector3(-21f, 4f, 0f), new Vector3(13f, 16f, 80f)),
        ("NorthRoad", new Vector3(3f, 4f, 20f), new Vector3(70f, 16f, 12f)),
        ("EastLane", new Vector3(24f, 4f, 2f), new Vector3(9f, 16f, 30f)),
        ("Rooftops", new Vector3(3f, 11f, 2f), new Vector3(44f, 9f, 26f)),
    };

    private static void BakeReflections(Transform stage, StringBuilder report)
    {
        var root = new GameObject("ReflectionProbes");
        root.transform.SetParent(stage, false);
        int baked = 0;
        foreach ((string name, Vector3 centre, Vector3 size) in ReflectionBoxes)
        {
            var probeObject = new GameObject("Reflection_" + name);
            probeObject.transform.SetParent(root.transform, false);
            // Capture from head height in the middle of the street, not from the middle of the box.
            Vector3 eye = new(centre.x, name == "Rooftops" ? centre.y : 1.8f, centre.z);
            probeObject.transform.position = eye;
            var probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = size;
            probe.center = centre - eye;
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.hdr = true;
            probe.nearClipPlane = 0.2f;
            probe.farClipPlane = 150f;
            probe.importance = name == "Rooftops" ? 0 : 1;
            probe.blendDistance = 1.5f;
            string path = LookRoot + "/Probes/Reflection_" + name + ".exr";
            if (Lightmapping.BakeReflectionProbe(probe, path))
            {
                // Kept on the probe itself as a custom texture: a baked one would live in the scene's lighting
                // data, which this stage does not have.
                probe.mode = ReflectionProbeMode.Custom;
                probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                baked++;
            }
            else
            {
                // No bake available (for example no graphics device): draw it once when the scene starts instead.
                probe.mode = ReflectionProbeMode.Realtime;
                probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            }
        }
        report.AppendLine("reflection probes: " + ReflectionBoxes.Length + " (" + baked + " baked)");
    }
}
