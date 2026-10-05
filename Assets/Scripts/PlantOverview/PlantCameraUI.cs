using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PlantCameraUI : MonoBehaviour
{
    [Header("Camera Controller Reference")]
    [Tooltip("Reference to the PlantIsometricCameraController. Auto-found if null.")]
    [SerializeField] private PlantIsometricCameraController cameraController;

    [Header("UI Element References")]
    [Tooltip("The switch button to toggle between worker and plant overview camera.")]
    [SerializeField] private Button switchButton;
    [SerializeField] private Text switchButtonText;
    [SerializeField] private TMP_Text switchButtonTMPText;

    [Tooltip("The card display shown when a highlighted object is selected.")]
    [SerializeField] private GameObject selectionCardObj;
    [SerializeField] private Text selectionNameText;
    [SerializeField] private TMP_Text selectionNameTMPText;
    [SerializeField] private Text selectionBoundsText;
    [SerializeField] private TMP_Text selectionBoundsTMPText;

    [Tooltip("The bottom controls help bar.")]
    [SerializeField] private GameObject controlsHelpObj;
    [SerializeField] private Text controlsHelpText;
    [SerializeField] private TMP_Text controlsHelpTMPText;

    [Header("Dynamic Button Text")]
    [SerializeField] private bool updateButtonTextDynamically = true;
    [SerializeField] private string workerModeButtonText = "Plant Overview [C]";
    [SerializeField] private string isometricModeButtonText = "Switch to Worker [C]";

    [Header("Events")]
    [Tooltip("Invoked when the camera switch button is clicked.")]
    public UnityEvent onSwitchButtonClicked = new UnityEvent();

    public PlantIsometricCameraController CameraController
    {
        get => cameraController;
        set => cameraController = value;
    }

    private void Awake()
    {
        AutoFindReferences();

        if (switchButton != null)
        {
            switchButton.onClick.RemoveListener(OnSwitchButtonClicked);
            switchButton.onClick.AddListener(OnSwitchButtonClicked);
        }

        if (selectionCardObj != null)
        {
            selectionCardObj.SetActive(false);
        }
    }

    /// <summary>
    /// Public click handler. Can be assigned directly to Button.onClick in the Inspector.
    /// Toggles the camera mode via PlantIsometricCameraController.
    /// </summary>
    public void OnSwitchButtonClicked()
    {
        onSwitchButtonClicked.Invoke();

        if (cameraController != null)
        {
            cameraController.SwitchCamera();
        }
    }

    /// <summary>
    /// Public helper to switch camera mode directly.
    /// </summary>
    public void SwitchCamera()
    {
        OnSwitchButtonClicked();
    }

    /// <summary>
    /// Updates the UI state depending on whether Isometric mode is active.
    /// </summary>
    public void SetCameraMode(bool isIsometricMode)
    {
        if (updateButtonTextDynamically)
        {
            string label = isIsometricMode ? isometricModeButtonText : workerModeButtonText;
            SetText(switchButtonText, switchButtonTMPText, label);
        }

        if (controlsHelpObj != null)
        {
            controlsHelpObj.SetActive(isIsometricMode);
        }

        if (!isIsometricMode)
        {
            HideSelection();
        }
    }

    /// <summary>
    /// Displays the selection card with the target box collider's name and world bounds.
    /// </summary>
    public void ShowSelection(BoxCollider box)
    {
        if (box == null)
        {
            HideSelection();
            return;
        }

        Vector3 size = box.bounds.size;
        ShowSelection(box.gameObject.name, size);
    }

    /// <summary>
    /// Displays the selection card with explicit name and size.
    /// </summary>
    public void ShowSelection(string objectName, Vector3 boundsSize)
    {
        if (selectionCardObj != null)
        {
            selectionCardObj.SetActive(true);
        }

        SetText(selectionNameText, selectionNameTMPText, objectName);

        string sizeStr = $"Size: {Mathf.Abs(boundsSize.x):0.1f}m x {Mathf.Abs(boundsSize.y):0.1f}m x {Mathf.Abs(boundsSize.z):0.1f}m";
        SetText(selectionBoundsText, selectionBoundsTMPText, sizeStr);
    }

    /// <summary>
    /// Hides the selection card.
    /// </summary>
    public void HideSelection()
    {
        if (selectionCardObj != null)
        {
            selectionCardObj.SetActive(false);
        }
    }

    /// <summary>
    /// Toggles the visibility of the controls help badge.
    /// </summary>
    public void SetControlsHelpVisible(bool visible)
    {
        if (controlsHelpObj != null)
        {
            controlsHelpObj.SetActive(visible);
        }
    }

    /// <summary>
    /// Toggles entire canvas visibility.
    /// </summary>
    public void SetCanvasVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    private static void SetText(Text uiText, TMP_Text tmpText, string content)
    {
        if (uiText != null) uiText.text = content;
        if (tmpText != null) tmpText.text = content;
    }

    [ContextMenu("Auto-Find References")]
    public void AutoFindReferences()
    {
        if (cameraController == null)
        {
            cameraController = Object.FindAnyObjectByType<PlantIsometricCameraController>(FindObjectsInactive.Include);
        }

        if (switchButton == null)
        {
            switchButton = GetComponentInChildren<Button>(true);
        }

        if (switchButton != null)
        {
            if (switchButtonText == null) switchButtonText = switchButton.GetComponentInChildren<Text>(true);
            if (switchButtonTMPText == null) switchButtonTMPText = switchButton.GetComponentInChildren<TMP_Text>(true);
        }

        if (selectionCardObj == null)
        {
            Transform card = transform.Find("SelectionInfoCard");
            if (card != null) selectionCardObj = card.gameObject;
        }

        if (selectionCardObj != null)
        {
            if (selectionNameText == null)
            {
                Transform nameT = selectionCardObj.transform.Find("ObjectName");
                if (nameT != null) selectionNameText = nameT.GetComponent<Text>();
            }
            if (selectionNameTMPText == null)
            {
                Transform nameT = selectionCardObj.transform.Find("ObjectName");
                if (nameT != null) selectionNameTMPText = nameT.GetComponent<TMP_Text>();
            }

            if (selectionBoundsText == null)
            {
                Transform boundsT = selectionCardObj.transform.Find("ObjectBounds");
                if (boundsT != null) selectionBoundsText = boundsT.GetComponent<Text>();
            }
            if (selectionBoundsTMPText == null)
            {
                Transform boundsT = selectionCardObj.transform.Find("ObjectBounds");
                if (boundsT != null) selectionBoundsTMPText = boundsT.GetComponent<TMP_Text>();
            }
        }

        if (controlsHelpObj == null)
        {
            Transform help = transform.Find("ControlsHelpBadge");
            if (help != null) controlsHelpObj = help.gameObject;
        }

        if (controlsHelpObj != null)
        {
            if (controlsHelpText == null) controlsHelpText = controlsHelpObj.GetComponentInChildren<Text>(true);
            if (controlsHelpTMPText == null) controlsHelpTMPText = controlsHelpObj.GetComponentInChildren<TMP_Text>(true);
        }
    }
}
