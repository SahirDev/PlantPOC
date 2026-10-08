using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Right toolbar of the Plant UI + the Day / Night button.
///   Worker group (worker mode only): TPP (third person), FPP (first person), Fly camera - current one highlighted.
///   Overview / worker switch (Power Plant Area only).
///   Hide / show UI (always - brings the panels back).
///   Day / Night toggle and the (i) Plant Status button beside it (Power Plant Area only).
/// All through the same CommunicationManager functions React uses.
/// </summary>
public class PlantToolbar : MonoBehaviour
{
    [Header("Worker view (worker mode only)")]
    [SerializeField] private GameObject workerGroup;
    [SerializeField] private Button tppButton;
    [SerializeField] private Button fppButton;
    [SerializeField] private Button flyButton;
    [SerializeField] private Image tppHighlight, fppHighlight, flyHighlight;

    [Header("Other")]
    [SerializeField] private Button overviewButton;
    [SerializeField] private Button hideButton;
    [SerializeField] private Image hideIcon;
    [SerializeField] private Sprite hideSprite;
    [SerializeField] private Sprite showSprite;

    [Header("Day / Night")]
    [SerializeField] private Button dayNightButton;
    [SerializeField] private Image dayNightIcon;
    [SerializeField] private TMP_Text dayNightLabel;
    [SerializeField] private Sprite daySprite, eveningSprite, nightSprite;

    [Header("Plant Status (i) button - beside Day / Night")]
    [SerializeField] private Button infoButton;
    [SerializeField] private Image infoBackground;
    [SerializeField] private Color infoIdle = new Color(0.07f, 0.09f, 0.17f, 0.95f);
    [SerializeField] private Color infoOpen = new Color(0.17f, 0.32f, 0.85f, 1f);
    [SerializeField, Min(0f)] private float gapBesideDayNight = 10f;

    [SerializeField] private Color highlightColor = new Color(0.17f, 0.27f, 0.6f, 1f);

    [Header("Placement")]
    [Tooltip("Keep the toolbar right under the Day / Night button (where that button is when it is hidden), " +
             "growing downwards. Off = the toolbar stays where you put it.")]
    [SerializeField] private bool stackUnderDayNight = true;
    [SerializeField, Min(0f)] private float gapBelowDayNight = 12f;

    private readonly Vector3[] corners = new Vector3[4];

    private void Awake()
    {
        if (tppButton != null) tppButton.onClick.AddListener(() => SetView(CommunicationManager.WorkerViewTPP));
        if (fppButton != null) fppButton.onClick.AddListener(() => SetView(CommunicationManager.WorkerViewFPP));
        if (flyButton != null) flyButton.onClick.AddListener(() => SetView(CommunicationManager.WorkerViewFly));
        if (overviewButton != null) overviewButton.onClick.AddListener(ToggleOverview);
        if (hideButton != null) hideButton.onClick.AddListener(() => { if (PlantUI.Instance != null) PlantUI.Instance.ToggleHidden(); });
        if (dayNightButton != null) dayNightButton.onClick.AddListener(NextTimeOfDay);
        if (infoButton != null) infoButton.onClick.AddListener(() => { if (PlantUI.Instance != null) PlantUI.Instance.ToggleStatusPanel(); });
    }

    /// <summary>Called by PlantUI a few times per second.</summary>
    public void Refresh(bool workerActive, bool uiHidden, bool explosionView = false)
    {
        PlaceUnderDayNight();

        // Explosion view: the whole toolbar and the day / night button are off (only Collapse All is shown).
        SetActive(gameObject, !explosionView);
        if (explosionView)
        {
            SetActive(dayNightButton != null ? dayNightButton.gameObject : null, false);
            SetActive(infoButton != null ? infoButton.gameObject : null, false);
            return;
        }

        SetActive(workerGroup, workerActive && !uiHidden);
        if (workerActive && PlantUI.Instance != null)
        {
            CharacterViewStateMachine views = PlantUI.Instance.Views();
            CharacterViewStateMachine.ViewMode mode = views != null ? views.CurrentMode : CharacterViewStateMachine.ViewMode.TPP;
            Highlight(tppHighlight, mode == CharacterViewStateMachine.ViewMode.TPP);
            Highlight(fppHighlight, mode == CharacterViewStateMachine.ViewMode.FPP);
            Highlight(flyHighlight, mode == CharacterViewStateMachine.ViewMode.FlyCam);
        }

        bool plantArea = PlantUI.Instance != null && PlantUI.Instance.OverviewCamera != null;
        SetActive(overviewButton != null ? overviewButton.gameObject : null, plantArea && !uiHidden);
        if (hideIcon != null) hideIcon.sprite = uiHidden ? showSprite : hideSprite;

        bool dayNight = DayNightController.Instance != null;
        SetActive(dayNightButton != null ? dayNightButton.gameObject : null, dayNight && !uiHidden);
        if (dayNight) ShowTimeOfDay(DayNightController.Instance.Current);

        // (i) Plant Status: Power Plant Area only, left of Day / Night.
        SetActive(infoButton != null ? infoButton.gameObject : null, dayNight && !uiHidden);
        if (infoButton != null && dayNight)
        {
            PlaceBesideDayNight();
            bool open = PlantUI.Instance != null && PlantUI.Instance.IsStatusPanelOpen;
            if (infoBackground != null) infoBackground.color = open ? infoOpen : infoIdle;
        }
    }

    // Top-right corner of the toolbar = bottom-right corner of the Day / Night button (+ gap); when that button is
    // hidden (rooms without day / night) the toolbar moves up into its place. Pivot at the top: it opens downwards.
    private void PlaceUnderDayNight()
    {
        if (!stackUnderDayNight || dayNightButton == null) return;
        var toolbar = (RectTransform)transform;
        var dayNight = (RectTransform)dayNightButton.transform;

        if (toolbar.pivot != Vector2.one)
        {
            toolbar.anchorMin = toolbar.anchorMax = Vector2.one;
            toolbar.pivot = Vector2.one;
        }

        dayNight.GetWorldCorners(corners); // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
        Vector3 target = dayNightButton.gameObject.activeInHierarchy
            ? corners[3] - new Vector3(0f, gapBelowDayNight * toolbar.lossyScale.y, 0f)
            : corners[2];
        if ((toolbar.position - target).sqrMagnitude > 0.01f) toolbar.position = target;
    }

    // Right edge of the (i) button = left edge of Day / Night - gap, same vertical centre and height.
    private void PlaceBesideDayNight()
    {
        if (dayNightButton == null) return;
        var info = (RectTransform)infoButton.transform;
        var dayNight = (RectTransform)dayNightButton.transform;
        if (info.pivot != new Vector2(1f, 0.5f))
        {
            info.anchorMin = info.anchorMax = Vector2.one;
            info.pivot = new Vector2(1f, 0.5f);
        }
        dayNight.GetWorldCorners(corners);
        Vector3 leftMiddle = (corners[0] + corners[1]) * 0.5f;
        Vector3 target = leftMiddle - new Vector3(gapBesideDayNight * info.lossyScale.x, 0f, 0f);
        if ((info.position - target).sqrMagnitude > 0.01f) info.position = target;
        float height = dayNight.rect.height;
        if (height > 1f && Mathf.Abs(info.sizeDelta.y - height) > 0.5f) info.sizeDelta = new Vector2(height, height);
    }

    private void SetView(string view)
    {
        if (CommunicationManager.HasInstance) CommunicationManager.Instance.SetWorkerView_Extern(view);
    }

    private void ToggleOverview()
    {
        if (CommunicationManager.HasInstance) CommunicationManager.Instance.ToggleCameraMode_Extern();
    }

    private void NextTimeOfDay()
    {
        if (DayNightController.Instance == null) return;
        // Day <-> Night only (no Evening from the Unity button).
        DayNightController.TimeOfDay next = DayNightController.Instance.Current == DayNightController.TimeOfDay.Night
            ? DayNightController.TimeOfDay.Day : DayNightController.TimeOfDay.Night;
        DayNightController.Instance.SetTime(DayNightController.Name(next));
        ShowTimeOfDay(next);
    }

    private void ShowTimeOfDay(DayNightController.TimeOfDay time)
    {
        if (dayNightIcon != null)
            dayNightIcon.sprite = time == DayNightController.TimeOfDay.Night ? nightSprite : time == DayNightController.TimeOfDay.Evening ? eveningSprite : daySprite;
        if (dayNightLabel != null)
            dayNightLabel.text = time == DayNightController.TimeOfDay.Night ? "Night" : time == DayNightController.TimeOfDay.Evening ? "Evening" : "Day";
    }

    private void Highlight(Image image, bool on)
    {
        if (image == null) return;
        Color c = highlightColor;
        c.a = on ? highlightColor.a : 0f;
        image.color = c;
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }
}
