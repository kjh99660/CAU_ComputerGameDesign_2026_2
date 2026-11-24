using UnityEngine;
using UnityEngine.SceneManagement;

// Loads a scene for a PlayMode test by its path, whether or not it is in the player build. Since 2026-11-25 the build
// holds only the Neon City stage (Main_NeonCity.unity); the flat arena (Main.unity) stays in the project for the
// tests and the A/B benchmark, so tests load it through the Editor instead of the build list.
public static class TestScenes
{
    public const string Arena = "Assets/Scenes/Main.unity";
    public const string NeonCity = "Assets/Scenes/Main_NeonCity.unity";

    public static AsyncOperation Load(string path) =>
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
}
