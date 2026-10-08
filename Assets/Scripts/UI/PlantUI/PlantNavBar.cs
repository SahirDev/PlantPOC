using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Bottom navigation bar of the Plant UI: Home + one button per area. The button of the current scene is
/// highlighted. Home = Power Plant Area in overview (if already there: back to the overview camera).
/// Scenes load through CommunicationManager.ChangeScene_Extern (same as React).
/// </summary>
public class PlantNavBar : MonoBehaviour
{
    public const string PlantScene = "Main_Scene";

    [Serializable]
    public class Item
    {
        public Button button;
        [Tooltip("Scene loaded by this button (Main_Scene, BoilerRoom, TurbineRoom, Control_Room).")]
        public string sceneName;
        [Tooltip("Background shown highlighted for the current scene.")]
        public Image highlight;
        public TMP_Text label;
        public Image icon;
    }

    [SerializeField] private Button homeButton;
    [SerializeField] private Item[] items = new Item[0];
    [SerializeField] private Color normalText = new Color(0.86f, 0.89f, 0.95f);
    [SerializeField] private Color activeText = Color.white;
    [SerializeField] private Color activeBackground = new Color(0.17f, 0.27f, 0.6f, 1f);

    private string shownScene;

    private void Awake()
    {
        if (homeButton != null) homeButton.onClick.AddListener(Home);
        foreach (Item item in items)
        {
            if (item?.button == null) continue;
            string scene = item.sceneName;
            item.button.onClick.AddListener(() => Go(scene));
        }
    }

    private void OnEnable() => shownScene = null;

    private void Update()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (scene == shownScene) return;
        shownScene = scene;

        foreach (Item item in items)
        {
            if (item == null) continue;
            bool active = item.sceneName == scene;
            if (item.highlight != null) item.highlight.color = active ? activeBackground : new Color(activeBackground.r, activeBackground.g, activeBackground.b, 0f);
            if (item.label != null) item.label.color = active ? activeText : normalText;
            if (item.icon != null) item.icon.color = active ? activeText : normalText;
        }
    }

    public void Home()
    {
        if (SceneManager.GetActiveScene().name == PlantScene)
        {
            if (PlantIsometricCameraController.HasInstance) PlantIsometricCameraController.Instance.EnableIsometricMode();
            return;
        }
        Go(PlantScene);
    }

    public void Go(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || SceneManager.GetActiveScene().name == sceneName) return;
        if (CommunicationManager.HasInstance) CommunicationManager.Instance.ChangeScene_Extern(sceneName);
        else if (SceneController.HasInstance) SceneController.Instance.ChangeScene(sceneName);
    }
}
