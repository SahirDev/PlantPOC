using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor only: pressing Play in ANY scene starts from Bootstrap (CommunicationManager, SceneController,
/// HUD, minimap panel - exactly like the WebGL build) and then opens the scene you were editing.
/// Switch it off with Tools > Thermal Plant > Play From Bootstrap.
/// </summary>
[InitializeOnLoad]
internal static class PlayFromBootstrap
{
    private const string MenuPath = "Tools/Thermal Plant/Play From Bootstrap";
    private const string EnabledKey = "ThermalPlant.PlayFromBootstrap";
    private const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";

    /// <summary>Read by BootstrapLoader (editor only): the scene to open instead of Main_Scene.</summary>
    public const string StartSceneKey = "ThermalPlant.StartScene";

    static PlayFromBootstrap()
    {
        EditorApplication.delayCall += Apply;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(EnabledKey, true);
        set => EditorPrefs.SetBool(EnabledKey, value);
    }

    [MenuItem(MenuPath, priority = 30)]
    private static void Toggle()
    {
        Enabled = !Enabled;
        Apply();
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    private static void Apply()
    {
        EditorSceneManager.playModeStartScene = Enabled ? AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapPath) : null;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;

        Apply();
        SessionState.SetString(StartSceneKey, string.Empty);
        if (!Enabled || EditorSceneManager.playModeStartScene == null) return;

        // Play loads scenes from disk: unsaved changes would be missing.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorApplication.isPlaying = false;
            return;
        }

        string edited = SceneManager.GetActiveScene().name;
        if (edited != "Bootstrap") SessionState.SetString(StartSceneKey, edited);
    }
}
