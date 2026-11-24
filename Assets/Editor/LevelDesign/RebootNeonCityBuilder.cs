using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds the multi-level city stage: Assets/Scenes/Main_NeonCity.unity.
// The scene is Main.unity (all game objects, UI and wiring as they are) with the floating plaza and its
// surroundings removed and the street block of the LowPolySciFiCity demo scene put in their place.
// Main.unity and the demo scene are only read. Running the builder again rebuilds the stage from them.
// Every position and size below is a level-design value and provisional.
public static partial class RebootNeonCityBuilder
{
    public const string MainScenePath = "Assets/Scenes/Main.unity";
    public const string CityScenePath = "Assets/LowPolySciFiCity/Scenes/LP_SciFiCity_DEMO.unity";
    public const string ScenePath = "Assets/Scenes/Main_NeonCity.unity";
    public const string StageRootName = "Stage_NeonCity";
    public const string AddedRootName = "Added";
    public const string SpawnPointName = "PlayerSpawnPoint";
    private const string PackPrefabs = "Assets/LowPolySciFiCity/Prefabs/";

    // Roots of Main.unity that make up the old stage: the plaza, the buildings around and under it, the skyline.
    private static readonly string[] RemovedRoots = { "MainMap", "CenterMap", "BackGroundMap", "Cube", "Sample" };
    // Roots of the demo scene that make up the city. Its cameras, sun and post-processing are left behind.
    private static readonly string[] CityRoots = { "Floor", "Buildings", "Props", "Vehicles", "Nature", "Light" };

    // Where a round starts: on the south road at the mouth of the central alley, looking up the alley.
    private static readonly Vector3 PlayerStart = new(0.5f, 0.15f, -15f);
    // The part of the city enemies may walk in.
    private static readonly Vector3 NavCentre = new(3f, 6.5f, -4f);
    private static readonly Vector3 NavSize = new(80f, 15f, 86f);
    // Enemies follow the player off any roof of the block: the highest walkable one is three storeys up.
    private const float MaxDropHeight = 10.5f;

    // Police tape, its posts and the gate frames along the south road and the tower. They only collide at the
    // posts, so the player would walk through the tape, and they cut the largest open space of the level in two.
    private static readonly string[] LaneClutter = { "SM_Floor_RoadPart_05", "SM_Floor_RoadPart_06", "SM_Floor_RoadPart_07", "SM_Floor_RoadPart_08" };
    // Street furniture smaller than this (m) stops colliding: chairs, stools, boxes, trays. It stays visible, but
    // neither the player nor the crowd gets caught on it. Litter bins stay solid.
    private const float SmallPropFootprint = 0.7f, SmallPropHeight = 1.3f;

    // A straight two-flight stair from the street up to the second storey (6.7 m), climbing toward +z along a
    // wall whose roof is on the +x side: flight, a kiosk whose roof is the half-way landing, flight, top landing.
    private readonly struct StairTower
    {
        public readonly string Name;
        public readonly float X, BottomZ;
        public readonly Vector3[] RailingsToOpen;   // glass railing pieces of the roof where the landing meets it
        public readonly Vector3 CarSpot;            // a car parked in the way is moved here
        public readonly float CarYaw;

        public StairTower(string name, float x, float bottomZ, Vector3[] railingsToOpen, Vector3 carSpot, float carYaw)
        {
            Name = name;
            X = x;
            BottomZ = bottomZ;
            RailingsToOpen = railingsToOpen;
            CarSpot = carSpot;
            CarYaw = carYaw;
        }
    }

    private const float FlightRun = 4.5f, FlightRise = 3.3f, FlightWidth = 2.5f, LandingRun = 2.5f, StreetLevel = 0.05f;

    // The demo block has one stair up, on its north side. These two give the second storey three ways up, so it
    // is a loop and not a dead end: from the west road, and from the central alley.
    private static readonly StairTower[] Stairs =
    {
        new("Stair_WestRoad", -16.25f, -4.5f, new[] { new Vector3(-14.8f, 7.2f, 7.4f), new Vector3(-14.8f, 7.2f, 9.0f) }, new Vector3(-21.5f, 0.1f, -9f), 0f),
        new("Stair_CentralAlley", 3.75f, -11.25f, new[] { new Vector3(5.2f, 7.2f, 0.8f), new Vector3(5.2f, 7.2f, 2.5f) }, new Vector3(-9f, 0.1f, -13.3f), 90f),
    };

    [MenuItem("Tools/REBOOT Level/Neon City: Rebuild Main_NeonCity From Main")]
    public static void RebuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        Debug.Log(Build());
    }

    // Unity -batchmode -executeMethod RebootNeonCityBuilder.BuildBatch
    public static void BuildBatch()
    {
        try
        {
            Debug.Log("[NeonCity] BUILD OK\n" + Build());
        }
        catch (Exception exception)
        {
            Debug.LogError("[NeonCity] BUILD FAILED\n" + exception);
            EditorApplication.Exit(1);
        }
    }

    // Returns a short report of what was taken out, moved and added.
    public static string Build()
    {
        var report = new StringBuilder();

        // 1. Start from Main.unity as it is now. Saving as a copy keeps the stage scene's own .meta.
        Scene main = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        if (!EditorSceneManager.SaveScene(main, ScenePath, true))
            throw new InvalidOperationException("Could not write " + ScenePath);
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // 2. Take the old stage out.
        foreach (GameObject root in scene.GetRootGameObjects())
            if (RemovedRoots.Contains(root.name) || root.name == StageRootName)
            {
                report.AppendLine("removed root " + root.name);
                UnityEngine.Object.DestroyImmediate(root);
            }

        // 3. Bring the city in. The demo scene is opened next to this one, emptied into it and closed unsaved.
        var stage = new GameObject(StageRootName);
        Scene city = EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Additive);
        foreach (GameObject root in city.GetRootGameObjects())
        {
            if (!CityRoots.Contains(root.name))
                continue;
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetParent(stage.transform, true);
        }
        EditorSceneManager.CloseScene(city, true);
        SceneManager.SetActiveScene(scene);

        // 4. Make it a place to fight in.
        ClearTheLanes(stage.transform, report);
        var added = new GameObject(AddedRootName);
        added.transform.SetParent(stage.transform, false);
        foreach (StairTower stair in Stairs)
            AddStairTower(stage.transform, added.transform, stair, report);
        CloseSafeSpots(stage.transform, added.transform, report);
        Physics.SyncTransforms();

        // 5. Game wiring that pointed into the old stage.
        Transform spawn = PlaceSpawnPoint(scene);
        PlacePlayer(scene, spawn);
        AddNavigation(scene, stage.transform);

        // 6. How it looks.
        ApplyLook(scene, stage.transform, report);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save " + ScenePath);
        return report.ToString();
    }

    // ---------------------------------------------------------------- lanes
    private static void ClearTheLanes(Transform stage, StringBuilder report)
    {
        int clutter = 0;
        Transform floor = stage.Find("Floor");
        if (floor != null)
            for (int i = floor.childCount - 1; i >= 0; i--)
            {
                Transform child = floor.GetChild(i);
                if (!LaneClutter.Any(name => child.name.StartsWith(name, StringComparison.Ordinal)))
                    continue;
                UnityEngine.Object.DestroyImmediate(child.gameObject);
                clutter++;
            }
        report.AppendLine("removed " + clutter + " police-tape, post and gate pieces");

        int softened = 0;
        Transform props = stage.Find("Props");
        if (props != null)
            foreach (Collider collider in props.GetComponentsInChildren<Collider>(true))
            {
                Vector3 size = collider.bounds.size;
                if (Mathf.Max(size.x, size.z) >= SmallPropFootprint || size.y >= SmallPropHeight || collider.name.Contains("Trash"))
                    continue;
                collider.enabled = false;
                softened++;
            }
        report.AppendLine("turned off the colliders of " + softened + " small props");
    }

    // ---------------------------------------------------------------- stairs
    private static void AddStairTower(Transform stage, Transform parent, StairTower stair, StringBuilder report)
    {
        float length = FlightRun * 2f + LandingRun * 2f;
        var footprint = new Bounds(
            new Vector3(stair.X, 5f, stair.BottomZ + length * 0.5f),
            new Vector3(FlightWidth - 0.2f, 10f, length - 0.2f));
        MakeRoom(stage, footprint, stair, report);
        foreach (Vector3 railing in stair.RailingsToOpen)
            RemoveNear(stage, railing, 0.4f, "SM_Props_Fence", report);

        var root = new GameObject(stair.Name);
        root.transform.SetParent(parent, false);
        Quaternion climbing = Quaternion.Euler(0f, 180f, 0f);   // the stair prefab rises toward its own -z
        float z = stair.BottomZ;
        Place("Buildings/SM_Buildings_Stairs_02", root.transform, new Vector3(stair.X, StreetLevel, z), climbing);
        z += FlightRun;
        // The kiosk is 2.5 m along the climb and 5 m across: half of it stands inside the wall.
        Place("Buildings/SM_BuildingPart_26", root.transform, new Vector3(stair.X, StreetLevel, z + LandingRun * 0.5f), Quaternion.Euler(0f, 90f, 0f));
        z += LandingRun;
        Place("Buildings/SM_Buildings_Stairs_02", root.transform, new Vector3(stair.X, StreetLevel + FlightRise, z), climbing);
        z += FlightRun;
        float landingZ = z + LandingRun * 0.5f, landingTop = StreetLevel + FlightRise * 2f + 0.05f;
        Place("Floor/SM_Floor_Tile_06", root.transform, new Vector3(stair.X, StreetLevel + FlightRise * 2f, landingZ), Quaternion.identity);
        // Glass railing on the two open sides of the top landing, so running up the stair does not carry anyone
        // off its far end. The side toward the roof stays open.
        PlaceOnFloor("Props/SM_Props_Fence_01", root.transform, new Vector3(stair.X, landingTop, z + LandingRun - 0.06f), 0f, LandingRun);
        PlaceOnFloor("Props/SM_Props_Fence_01", root.transform, new Vector3(stair.X - FlightWidth * 0.5f + 0.06f, landingTop, landingZ), 90f, LandingRun);
        // The kiosk's street side would be a blank wall: a vending machine, a sign and a strip lamp on it.
        // Its long axis is across the climb, so that side is at x - 2.5 and faces -x.
        var wall = new Vector3(stair.X - 2.5f, StreetLevel, stair.BottomZ + FlightRun + LandingRun * 0.5f);
        Quaternion facingStreet = Quaternion.Euler(0f, 270f, 0f);   // the kit's signs and machines face their own +z
        PlaceOnFloor("Props/SM_Props_VendingMachine_01", root.transform, wall + new Vector3(-0.42f, 0f, -0.55f), 270f);
        Place("BuildingParts/SM_Buildings_Part_59", root.transform, wall + new Vector3(-0.06f, 2.55f, 0.55f), facingStreet);
        Place("BuildingParts/SM_Buildings_Part_39", root.transform, wall + new Vector3(-0.12f, 3.05f, 0f), facingStreet);
        report.AppendLine("added " + stair.Name + " at x=" + stair.X + ", z=" + stair.BottomZ + ".." + (stair.BottomZ + length));
    }

    private static GameObject Place(string prefabPath, Transform parent, Vector3 position, Quaternion rotation)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PackPrefabs + prefabPath + ".prefab");
        if (prefab == null)
            throw new InvalidOperationException("Missing prefab " + prefabPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.transform.SetPositionAndRotation(position, rotation);
        return instance;
    }

    // Puts a prefab so that the middle of its footprint is at the point and its underside on the point's height,
    // whatever its own pivot is.
    // length > 0 stretches it along its own x axis to that length (a railing made to fit an edge).
    private static void PlaceOnFloor(string prefabPath, Transform parent, Vector3 point, float yaw, float length = 0f)
    {
        GameObject instance = Place(prefabPath, parent, point, Quaternion.identity);
        if (length > 0f && TryGetRendererBounds(instance.transform, out Bounds unrotated) && unrotated.size.x > 0.01f)
        {
            Vector3 scale = instance.transform.localScale;
            scale.x *= length / unrotated.size.x;
            instance.transform.localScale = scale;
        }
        instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (!TryGetRendererBounds(instance.transform, out Bounds bounds))
            return;
        instance.transform.position += new Vector3(point.x - bounds.center.x, point.y - bounds.min.y, point.z - bounds.center.z);
    }

    // Empties the place a stair is built in: lamp posts and small things are removed, a parked car is moved.
    private static void MakeRoom(Transform stage, Bounds footprint, StairTower stair, StringBuilder report)
    {
        foreach (Transform group in stage)
        {
            if (group.name == AddedRootName)
                continue;
            for (int i = group.childCount - 1; i >= 0; i--)
            {
                Transform item = group.GetChild(i);
                if (!TryGetRendererBounds(item, out Bounds bounds) || !bounds.Intersects(footprint))
                    continue;
                if (group.name == "Vehicles")
                {
                    item.SetPositionAndRotation(stair.CarSpot, Quaternion.Euler(0f, stair.CarYaw, 0f));
                    report.AppendLine("moved " + item.name + " out of " + stair.Name + " to " + stair.CarSpot);
                    continue;
                }
                // Only what stands in the way: lamps, plants, signs, air conditioners. Never a house or a floor tile.
                bool small = bounds.size.x * bounds.size.z < 6f && item.GetComponent<Light>() == null;
                if (group.name == "Floor" && !item.name.StartsWith("SM_Floor_Light", StringComparison.Ordinal))
                    small = false;
                if (group.name == "Buildings" && !item.name.StartsWith("SM_Buildings_Part", StringComparison.Ordinal) && !item.name.StartsWith("SM_Buildings_Roof", StringComparison.Ordinal))
                    small = false;
                if (!small)
                    continue;
                report.AppendLine("removed " + group.name + "/" + item.name + " from " + stair.Name);
                UnityEngine.Object.DestroyImmediate(item.gameObject);
            }
        }
    }

    private static void RemoveNear(Transform stage, Vector3 point, float radius, string namePrefix, StringBuilder report)
    {
        foreach (Renderer renderer in stage.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.name.StartsWith(namePrefix, StringComparison.Ordinal) || (renderer.bounds.center - point).magnitude > radius)
                continue;
            GameObject item = PrefabUtility.GetOutermostPrefabInstanceRoot(renderer.gameObject) ?? renderer.gameObject;
            report.AppendLine("removed " + item.name + " at " + renderer.bounds.center);
            UnityEngine.Object.DestroyImmediate(item);
            return;
        }
        report.AppendLine("WARNING: nothing named " + namePrefix + "* near " + point);
    }

    private static bool TryGetRendererBounds(Transform item, out Bounds bounds)
    {
        Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
        bounds = default;
        if (renderers.Length == 0)
            return false;
        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    // ---------------------------------------------------------------- wiring
    private static Transform PlaceSpawnPoint(Scene scene)
    {
        GameObject spawn = scene.GetRootGameObjects().FirstOrDefault(root => root.name == SpawnPointName);
        if (spawn == null)
        {
            spawn = new GameObject(SpawnPointName);
            SceneManager.MoveGameObjectToScene(spawn, scene);
        }
        // Stand on the street itself: nothing moves the player down while the round has not started.
        Vector3 start = PlayerStart;
        if (Physics.Raycast(PlayerStart + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 6f, 1 << 0, QueryTriggerInteraction.Ignore))
            start.y = ground.point.y;
        spawn.transform.SetPositionAndRotation(start, Quaternion.identity);
        return spawn.transform;
    }

    private static void PlacePlayer(Scene scene, Transform spawn)
    {
        PlayerRuntime player = FindInScene<PlayerRuntime>(scene);
        if (player == null)
            throw new InvalidOperationException("Main.unity has no PlayerRuntime.");
        player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        var serialized = new SerializedObject(player);
        serialized.FindProperty("spawnPoint").objectReferenceValue = spawn;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // Main.unity leaves the player scripts' debugLog on: a line in the log for every dodge and skill.
        foreach (MonoBehaviour behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;
            var settings = new SerializedObject(behaviour);
            SerializedProperty log = settings.FindProperty("debugLog");
            if (log == null || log.propertyType != SerializedPropertyType.Boolean || !log.boolValue)
                continue;
            log.boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void AddNavigation(Scene scene, Transform stage)
    {
        var volumeObject = new GameObject("EnemyNavVolume");
        volumeObject.transform.SetParent(stage, false);
        volumeObject.transform.position = NavCentre;
        var volume = volumeObject.AddComponent<EnemyNavVolume>();
        volume.Configure(NavSize, 0.5f, 1 << 0);
        var volumeSettings = new SerializedObject(volume);
        volumeSettings.FindProperty("maxDropHeight").floatValue = MaxDropHeight;
        volumeSettings.FindProperty("maxClimbDegrees").floatValue = EnemyClimbDegrees;
        volumeSettings.ApplyModifiedPropertiesWithoutUndo();

        EnemyRuntime enemies = FindInScene<EnemyRuntime>(scene);
        if (enemies == null)
            throw new InvalidOperationException("Main.unity has no EnemyRuntime.");
        var serialized = new SerializedObject(enemies);
        serialized.FindProperty("navigation").objectReferenceValue = volume;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null)
                return found;
        }
        return null;
    }
}
