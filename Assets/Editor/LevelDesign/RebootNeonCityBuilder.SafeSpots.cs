using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Closes the places of the city where the player could stand for a whole round without any enemy reaching them.
// A bot found them by comparing every point the player can get to with the cells enemies walk on
// (Docs/Design/08_NEON_CITY_STAGE.md). Each one is closed in the least visible way that works:
//  - two shops whose door the demo scene leaves slid up: the door is lowered. The doorway is 1.24 m wide, too
//    narrow for the enemy navigation, so nothing could follow the player into the room behind it.
//  - two police cars parked side by side, whose roofs a dodge roll climbs: an invisible box over them that only
//    the player collides with.
//  - the entrance steps behind the start: one step is 0.44 m, just over what enemies climbed. Their climb limit
//    is raised instead of walling the steps off.
// Every value is a level-design value and provisional.
public static partial class RebootNeonCityBuilder
{
    public const string PlayerBlockersName = "PlayerBlockers";
    // Enemies link two 0.5 m cells whose heights differ by at most tan(limit) x 0.5 m: 0.42 m at 40 degrees,
    // 0.48 m at 44. From 46 degrees up they would also step onto benches.
    public const float EnemyClimbDegrees = 44f;

    // Doors of the demo scene that stand open (slid up), by where they are on the map. A closed door stands
    // ClosedDoorLift above the origin of its building part, as in the copies of the same part whose door is shut.
    private static readonly Vector2[] ShopDoorsToClose = { new(-5.07f, 11.05f), new(18.94f, -9.95f) };
    private const float ClosedDoorLift = 0.1f, DoorSearchRadius = 0.3f, GroundFloorTop = 4f;

    private readonly struct PlayerBlocker
    {
        public readonly string Name;
        public readonly Vector3 Centre, Size;
        public readonly float Yaw;

        public PlayerBlocker(string name, Vector3 centre, Vector3 size, float yaw)
        {
            Name = name;
            Centre = centre;
            Size = size;
            Yaw = yaw;
        }
    }

    private static readonly PlayerBlocker[] PlayerBlockers =
    {
        // The bounds of SM_Car_02 (1) and (2), 5 cm in at the sides and 6 cm in at the front and the back so
        // that no street beside them is lost, from under the street to 2.75 m.
        new("PlayerBlocker_PoliceCars", new Vector3(-27.64f, 1.35f, 2.775f), new Vector3(4.48f, 2.8f, 3.71f), 0f),
    };

    [MenuItem("Tools/REBOOT Level/Neon City: Close Safe Spots In Main_NeonCity")]
    public static void CloseSafeSpotsFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        Debug.Log(CloseSafeSpotsInStageScene());
    }

    // Unity -batchmode -executeMethod RebootNeonCityBuilder.CloseSafeSpotsBatch
    public static void CloseSafeSpotsBatch()
    {
        try
        {
            Debug.Log("[NeonCity] SAFE SPOTS OK\n" + CloseSafeSpotsInStageScene());
        }
        catch (Exception exception)
        {
            Debug.LogError("[NeonCity] SAFE SPOTS FAILED\n" + exception);
            EditorApplication.Exit(1);
        }
    }

    // Applies only this file's changes to the stage scene as it is; the stage is not rebuilt. Running it again
    // changes nothing.
    public static string CloseSafeSpotsInStageScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject stage = scene.GetRootGameObjects().FirstOrDefault(root => root.name == StageRootName);
        Transform added = stage != null ? stage.transform.Find(AddedRootName) : null;
        EnemyNavVolume volume = FindInScene<EnemyNavVolume>(scene);
        if (added == null || volume == null)
            throw new InvalidOperationException(ScenePath + " is not the built stage: " + StageRootName + "/" + AddedRootName + " or its EnemyNavVolume is missing.");

        var report = new StringBuilder();
        CloseSafeSpots(stage.transform, added, report);
        var volumeSettings = new SerializedObject(volume);
        volumeSettings.FindProperty("maxClimbDegrees").floatValue = EnemyClimbDegrees;
        volumeSettings.ApplyModifiedPropertiesWithoutUndo();
        report.AppendLine("enemy climb limit " + EnemyClimbDegrees + " degrees");

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save " + ScenePath);
        return report.ToString();
    }

    private static void CloseSafeSpots(Transform stage, Transform added, StringBuilder report)
    {
        CloseShopDoors(stage, report);
        AddPlayerBlockers(added, report);
    }

    private static void CloseShopDoors(Transform stage, StringBuilder report)
    {
        Transform buildings = stage.Find("Buildings");
        foreach (Vector2 spot in ShopDoorsToClose)
        {
            Transform door = null;
            if (buildings != null)
                foreach (Transform item in buildings.GetComponentsInChildren<Transform>(true))
                {
                    Vector3 at = item.position;
                    if (item.parent == null || at.y > GroundFloorTop || !item.name.StartsWith("SM_Buildings_Door", StringComparison.Ordinal) ||
                        (new Vector2(at.x, at.z) - spot).magnitude > DoorSearchRadius)
                        continue;
                    door = item;
                    break;
                }
            // Not a warning: the room behind the doorway would silently stay a place nobody can reach.
            if (door == null)
                throw new InvalidOperationException("No ground-floor door near " + spot + " to close. If the demo scene changed, find the open shop door again.");
            // Only the height changes (the building parts stand upright and unscaled), so the door keeps its place in the frame.
            Vector3 closed = door.localPosition;
            report.AppendLine("closed " + door.parent.name + "/" + door.name + ": local y " + closed.y.ToString("0.00") + " -> " + ClosedDoorLift.ToString("0.00"));
            closed.y = ClosedDoorLift;
            door.localPosition = closed;
            // The door belongs to a prefab instance of the demo scene: without this the move is not saved.
            PrefabUtility.RecordPrefabInstancePropertyModifications(door);
        }
    }

    // Blockers that are already there are kept and set again, so that running this twice leaves the scene as it is.
    private static void AddPlayerBlockers(Transform added, StringBuilder report)
    {
        // Ignore Raycast: the camera, the enemy navigation, spawn sight lines and debris are measured against
        // other layers, so none of them sees a blocker.
        int layer = LayerMask.NameToLayer("Ignore Raycast"), player = LayerMask.NameToLayer("Player");
        if (layer < 0 || player < 0)
            throw new InvalidOperationException("Player blockers need the Ignore Raycast and Player layers.");

        Transform root = added.Find(PlayerBlockersName);
        if (root == null)
        {
            root = new GameObject(PlayerBlockersName).transform;
            root.SetParent(added, false);
        }
        root.gameObject.layer = layer;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform stale = root.GetChild(i);
            if (PlayerBlockers.Any(blocker => blocker.Name == stale.name))
                continue;
            report.AppendLine("removed " + stale.name);
            UnityEngine.Object.DestroyImmediate(stale.gameObject);
        }
        foreach (PlayerBlocker blocker in PlayerBlockers)
        {
            Transform item = root.Find(blocker.Name);
            if (item == null)
            {
                item = new GameObject(blocker.Name).transform;
                item.SetParent(root, false);
            }
            item.gameObject.layer = layer;
            item.SetPositionAndRotation(blocker.Centre, Quaternion.Euler(0f, blocker.Yaw, 0f));
            if (!item.TryGetComponent(out BoxCollider box))
                box = item.gameObject.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = blocker.Size;
            box.isTrigger = false;
            box.excludeLayers = ~(1 << player);   // knocked-back enemy bodies and anything else pass through
            report.AppendLine("player blocker " + blocker.Name + " at " + blocker.Centre + ", size " + blocker.Size);
        }
    }
}
