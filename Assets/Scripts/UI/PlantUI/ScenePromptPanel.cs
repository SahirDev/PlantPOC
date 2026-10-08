using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Go to Boiler Room" prompt of the Plant UI: shown while the worker stands in a door trigger (SceneChangeCol),
/// hidden when the worker walks out. ENTER (or the Y key) loads that room. Built by Tools > Thermal Plant > 18.
/// </summary>
public class ScenePromptPanel : MonoBehaviour
{
    [SerializeField] private GameObject prompt;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text hint;
    [SerializeField] private Image icon;
    [SerializeField] private Button enterButton;
    [Tooltip("Icons for Main_Scene, BoilerRoom, TurbineRoom, Control_Room.")]
    [SerializeField] private Sprite[] sceneIcons = new Sprite[0];
    [Tooltip("Show the door's own Text (Inspector of SceneChangeCol) instead of 'Go to <room>'.")]
    [SerializeField] private bool useDoorText;

    private SceneChangeCol shown;

    private void Awake()
    {
        if (enterButton != null) enterButton.onClick.AddListener(Enter);
        SetPromptActive(false);
    }

    /// <summary>Called by PlantUI a few times per second.</summary>
    public void Refresh(bool allowed)
    {
        SceneChangeCol door = allowed ? SceneChangeCol.Current : null;
        if (door != null && !door.isActiveAndEnabled) door = null;
        SetPromptActive(door != null);
        if (door == null || door == shown) { shown = door; return; }

        shown = door;
        if (title != null) title.text = useDoorText && !string.IsNullOrWhiteSpace(door.Text) ? door.Text : "Go to " + door.TargetDisplayName;
        if (hint != null) hint.text = "Press Y or click Enter";
        int index = (int)door.selectScene;
        if (icon != null && index >= 0 && index < sceneIcons.Length && sceneIcons[index] != null) icon.sprite = sceneIcons[index];
    }

    private void Enter()
    {
        SceneChangeCol door = shown != null ? shown : SceneChangeCol.Current;
        SetPromptActive(false);
        shown = null;
        if (door != null) door.LoadScene();
    }

    private void SetPromptActive(bool active)
    {
        if (prompt != null && prompt.activeSelf != active) prompt.SetActive(active);
    }
}
