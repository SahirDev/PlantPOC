using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Unity UI of the Main_Scene plant overview. Everything else is React now; this canvas only shows
/// the object info card:
///   click a "Highlight" box collider in the overview camera -> card with the ObjectInfo name + description
///   opens beside the box and follows it while the camera pans / rotates / zooms.
/// The card never takes clicks (no raycast targets), so it can't block the camera.
/// </summary>
// After PlantIsometricCameraController (600) has moved the camera this frame.
[DefaultExecutionOrder(700)]
[DisallowMultipleComponent]
public class PlantCameraUI : MonoBehaviour
{
    [Header("Camera Controller Reference")]
    [Tooltip("Reference to the PlantIsometricCameraController. Auto-found if null.")]
    [SerializeField] private PlantIsometricCameraController cameraController;

    [Header("Info card")]
    [Tooltip("The card shown when a highlighted object is selected.")]
    [SerializeField] private GameObject selectionCardObj;
    [SerializeField] private Text selectionNameText;
    [SerializeField] private TMP_Text selectionNameTMPText;
    [Tooltip("Description text (child 'ObjectDescription'). If missing, the 'ObjectBounds' text shows the description.")]
    [SerializeField] private Text selectionDescriptionText;
    [SerializeField] private TMP_Text selectionDescriptionTMPText;
    [SerializeField] private Text selectionBoundsText;
    [SerializeField] private TMP_Text selectionBoundsTMPText;
    [Tooltip("Also show the size line (Size: 10m x 5m x 3m).")]
    [SerializeField] private bool showSize;

    [Header("Card placement")]
    [Tooltip("Gap between the object's edge on screen and the card, in pixels.")]
    [SerializeField, Min(0f)] private float gapFromObject = 24f;
    [Tooltip("Card never goes closer than this to the screen edge, in pixels.")]
    [SerializeField, Min(0f)] private float screenMargin = 16f;

    [Header("Old Unity controls (React does this now)")]
    [Tooltip("Off: the old switch button and controls help badge stay hidden (React shows them).")]
    [SerializeField] private bool showLegacyControls;
    [SerializeField] private Button switchButton;
    [SerializeField] private Text switchButtonText;
    [SerializeField] private TMP_Text switchButtonTMPText;
    [SerializeField] private GameObject controlsHelpObj;
    [SerializeField] private Text controlsHelpText;
    [SerializeField] private TMP_Text controlsHelpTMPText;
    [SerializeField] private bool updateButtonTextDynamically = true;
    [SerializeField] private string workerModeButtonText = "Plant Overview [C]";
    [SerializeField] private string isometricModeButtonText = "Switch to Worker [C]";

    [Header("Events")]
    public UnityEvent onSwitchButtonClicked = new UnityEvent();

    private Canvas rootCanvas;
    private RectTransform cardRect;
    private CanvasGroup cardGroup;
    private BoxCollider target;
    private ObjectInfo targetInfo;

    // Placement is only recomputed when something changed.
    private Matrix4x4 lastCameraMatrix;
    private Matrix4x4 lastTargetMatrix;
    private Vector2 lastScreenSize;
    private Vector2 lastCardSize;

    private readonly Vector3[] corners = new Vector3[8];

    public PlantIsometricCameraController CameraController
    {
        get => cameraController;
        set => cameraController = value;
    }

    public bool IsCardVisible => target != null && selectionCardObj != null && selectionCardObj.activeSelf;

    private void Awake()
    {
        AutoFindReferences();

        rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas != null) rootCanvas = rootCanvas.rootCanvas;

        if (switchButton != null)
        {
            switchButton.onClick.RemoveListener(OnSwitchButtonClicked);
            switchButton.onClick.AddListener(OnSwitchButtonClicked);
            if (!showLegacyControls) switchButton.gameObject.SetActive(false);
        }

        if (controlsHelpObj != null && !showLegacyControls) controlsHelpObj.SetActive(false);

        if (selectionCardObj != null)
        {
            cardRect = selectionCardObj.transform as RectTransform;
            MakeNonInteractive(selectionCardObj);
            selectionCardObj.SetActive(false);
        }
    }

    // ------------------------------------------------------------------ camera mode

    public void OnSwitchButtonClicked()
    {
        onSwitchButtonClicked.Invoke();
        if (cameraController != null) cameraController.SwitchCamera();
    }

    public void SwitchCamera()
    {
        OnSwitchButtonClicked();
    }

    /// <summary>Called by the camera controller when overview / worker mode changes.</summary>
    public void SetCameraMode(bool isIsometricMode)
    {
        if (showLegacyControls)
        {
            if (updateButtonTextDynamically)
                SetText(switchButtonText, switchButtonTMPText, isIsometricMode ? isometricModeButtonText : workerModeButtonText);

            if (controlsHelpObj != null) controlsHelpObj.SetActive(isIsometricMode);
        }

        if (!isIsometricMode) HideSelection();
    }

    public void SetControlsHelpVisible(bool visible)
    {
        if (controlsHelpObj != null) controlsHelpObj.SetActive(visible && showLegacyControls);
    }

    public void SetCanvasVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    // ------------------------------------------------------------------ info card

    /// <summary>Opens the info card beside this box collider (name + description from its ObjectInfo).</summary>
    public void ShowSelection(BoxCollider box)
    {
        if (box == null)
        {
            HideSelection();
            return;
        }

        target = box;
        targetInfo = ObjectInfo.For(box);

        string title = targetInfo != null ? targetInfo.DisplayName : box.gameObject.name;
        string description = targetInfo != null ? targetInfo.Description : string.Empty;
        FillCard(title, description, box.bounds.size);

        ForcePlacementUpdate();
        UpdatePlacement();
    }

    /// <summary>Card with explicit texts, not attached to an object (stays where it is).</summary>
    public void ShowSelection(string objectName, Vector3 boundsSize)
    {
        target = null;
        targetInfo = null;
        FillCard(objectName, string.Empty, boundsSize);
    }

    public void HideSelection()
    {
        target = null;
        targetInfo = null;
        if (selectionCardObj != null) selectionCardObj.SetActive(false);
    }

    private void FillCard(string title, string description, Vector3 size)
    {
        if (selectionCardObj == null) return;

        selectionCardObj.SetActive(true);
        SetText(selectionNameText, selectionNameTMPText, title);

        string sizeLine = $"Size: {Mathf.Abs(size.x):0.0}m x {Mathf.Abs(size.y):0.0}m x {Mathf.Abs(size.z):0.0}m";
        bool hasDescriptionField = selectionDescriptionText != null || selectionDescriptionTMPText != null;

        if (hasDescriptionField)
        {
            SetText(selectionDescriptionText, selectionDescriptionTMPText, description);
            SetActive(selectionDescriptionText, selectionDescriptionTMPText, !string.IsNullOrEmpty(description));
            SetText(selectionBoundsText, selectionBoundsTMPText, sizeLine);
            SetActive(selectionBoundsText, selectionBoundsTMPText, showSize);
        }
        else
        {
            // Old card layout: the bounds line shows the description.
            string text = string.IsNullOrEmpty(description) ? (showSize ? sizeLine : string.Empty)
                : showSize ? description + "\n" + sizeLine : description;
            SetText(selectionBoundsText, selectionBoundsTMPText, text);
            SetActive(selectionBoundsText, selectionBoundsTMPText, !string.IsNullOrEmpty(text));
        }

        // The card may resize to the new text (layout group / content size fitter).
        if (cardRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(cardRect);
    }

    private void LateUpdate()
    {
        if (target == null || cardRect == null || !selectionCardObj.activeSelf) return;

        if (!target.gameObject.activeInHierarchy)
        {
            HideSelection();
            return;
        }

        UpdatePlacement();
    }

    private void ForcePlacementUpdate()
    {
        lastScreenSize = Vector2.zero;
    }

    /// <summary>Puts the card beside the box collider as seen by the overview camera.</summary>
    private void UpdatePlacement()
    {
        if (target == null || cardRect == null) return;

        Camera cam = GetCamera();
        if (cam == null) return;

        Vector2 screenSize = new Vector2(Screen.width, Screen.height);
        float scale = rootCanvas != null ? rootCanvas.scaleFactor : 1f;
        Vector2 cardSize = cardRect.rect.size * scale;

        Matrix4x4 cameraMatrix = cam.transform.localToWorldMatrix;
        Matrix4x4 targetMatrix = target.transform.localToWorldMatrix;

        // Nothing moved: keep the card where it is.
        if (cameraMatrix == lastCameraMatrix && targetMatrix == lastTargetMatrix && screenSize == lastScreenSize && cardSize == lastCardSize) return;

        lastCameraMatrix = cameraMatrix;
        lastTargetMatrix = targetMatrix;
        lastScreenSize = screenSize;
        lastCardSize = cardSize;

        // Screen rectangle of the 8 box corners.
        Vector3 center = target.center;
        Vector3 extents = target.size * 0.5f;
        float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
        int inFront = 0;

        for (int i = 0; i < 8; i++)
        {
            Vector3 local = center + new Vector3(
                (i & 1) == 0 ? -extents.x : extents.x,
                (i & 2) == 0 ? -extents.y : extents.y,
                (i & 4) == 0 ? -extents.z : extents.z);

            Vector3 screen = cam.WorldToScreenPoint(target.transform.TransformPoint(local));
            if (screen.z <= 0f) continue;

            inFront++;
            if (screen.x < xMin) xMin = screen.x;
            if (screen.x > xMax) xMax = screen.x;
            if (screen.y < yMin) yMin = screen.y;
            if (screen.y > yMax) yMax = screen.y;
        }

        bool visible = inFront > 0 && xMax >= 0f && xMin <= screenSize.x && yMax >= 0f && yMin <= screenSize.y;
        SetCardAlpha(visible ? 1f : 0f);
        if (!visible) return;

        ObjectInfo.PanelSide side = targetInfo != null ? targetInfo.Side : ObjectInfo.PanelSide.Auto;
        Vector2 offset = targetInfo != null ? targetInfo.ScreenOffset : Vector2.zero;
        float gap = gapFromObject + offset.x;

        float rightX = xMax + gap;
        float leftX = xMin - gap - cardSize.x;
        bool fitsRight = rightX + cardSize.x <= screenSize.x - screenMargin;
        bool fitsLeft = leftX >= screenMargin;

        bool useRight = side == ObjectInfo.PanelSide.Right
            || (side == ObjectInfo.PanelSide.Auto && (fitsRight || !fitsLeft));

        float x = useRight ? rightX : leftX;
        float y = (yMin + yMax) * 0.5f + offset.y - cardSize.y * 0.5f;

        x = Mathf.Clamp(x, screenMargin, Mathf.Max(screenMargin, screenSize.x - screenMargin - cardSize.x));
        y = Mathf.Clamp(y, screenMargin, Mathf.Max(screenMargin, screenSize.y - screenMargin - cardSize.y));

        // (x, y) = bottom-left corner of the card in screen pixels -> card pivot in parent space.
        Vector2 pivotScreen = new Vector2(x + cardSize.x * cardRect.pivot.x, y + cardSize.y * cardRect.pivot.y);
        RectTransform parent = cardRect.parent as RectTransform;
        Camera uiCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;

        if (parent != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pivotScreen, uiCamera, out Vector2 localPoint))
        {
            cardRect.localPosition = new Vector3(localPoint.x, localPoint.y, 0f);
        }
    }

    private Camera GetCamera()
    {
        if (cameraController != null && cameraController.IsometricCamera != null) return cameraController.IsometricCamera;
        return Camera.main;
    }

    private void SetCardAlpha(float alpha)
    {
        if (cardGroup != null && !Mathf.Approximately(cardGroup.alpha, alpha)) cardGroup.alpha = alpha;
    }

    /// <summary>The card is display-only: it must never catch clicks, scroll or hover.</summary>
    private void MakeNonInteractive(GameObject card)
    {
        cardGroup = card.GetComponent<CanvasGroup>();
        if (cardGroup == null) cardGroup = card.AddComponent<CanvasGroup>();
        cardGroup.interactable = false;
        cardGroup.blocksRaycasts = false;

        foreach (Graphic graphic in card.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
    }

    private static void SetText(Text uiText, TMP_Text tmpText, string content)
    {
        if (uiText != null) uiText.text = content;
        if (tmpText != null) tmpText.text = content;
    }

    private static void SetActive(Text uiText, TMP_Text tmpText, bool active)
    {
        if (uiText != null && uiText.gameObject.activeSelf != active) uiText.gameObject.SetActive(active);
        if (tmpText != null && tmpText.gameObject.activeSelf != active) tmpText.gameObject.SetActive(active);
    }

    [ContextMenu("Auto-Find References")]
    public void AutoFindReferences()
    {
        if (cameraController == null)
            cameraController = Object.FindAnyObjectByType<PlantIsometricCameraController>(FindObjectsInactive.Include);

        if (switchButton == null)
        {
            Transform button = transform.Find("CameraSwitchButton");
            if (button != null) switchButton = button.GetComponent<Button>();
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
            Transform card = selectionCardObj.transform;
            FindText(card, "ObjectName", ref selectionNameText, ref selectionNameTMPText);
            FindText(card, "ObjectDescription", ref selectionDescriptionText, ref selectionDescriptionTMPText);
            FindText(card, "ObjectBounds", ref selectionBoundsText, ref selectionBoundsTMPText);
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

    private static void FindText(Transform parent, string childName, ref Text uiText, ref TMP_Text tmpText)
    {
        if (uiText != null || tmpText != null) return;

        Transform child = parent.Find(childName);
        if (child == null) return;

        uiText = child.GetComponent<Text>();
        tmpText = child.GetComponent<TMP_Text>();
    }
}
