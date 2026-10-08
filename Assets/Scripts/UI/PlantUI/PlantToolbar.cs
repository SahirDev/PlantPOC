using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Right toolbar of the Plant UI + the Day / Night button.
///   Worker group (worker mode only): TPP (third person), FPP (first person), Fly camera - current one highlighted.
///   Overview / worker switch (Power Plant Area only).
///   Hide / show UI (always - brings the panels back).
///   Day / Evening / Night (Power Plant Area only).
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

    [SerializeField] private Color highlightColor = new Color(0.17f, 0.27f, 0.6f, 1f);

    private void Awake()
    {
        if (tppButton != null) tppButton.onClick.AddListener(() => SetView(CommunicationManager.WorkerViewTPP));
        if (fppButton != null) fppButton.onClick.AddListener(() => SetView(CommunicationManager.WorkerViewFPP));
        if (flyButton != null) flyButton.onClick.AddListener(() => SetView(CommunicationManager.WorkerViewFly));
        if (overviewButton != null) overviewButton.onClick.AddListener(ToggleOverview);
        if (hideButton != null) hideButton.onClick.AddListener(() => { if (PlantUI.Instance != null) PlantUI.Instance.ToggleHidden(); });
        if (dayNightButton != null) dayNightButton.onClick.AddListener(NextTimeOfDay);
    }

    /// <summary>Called by PlantUI a few times per second.</summary>
    public void Refresh(bool workerActive, bool uiHidden)
    {
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
        DayNightController.TimeOfDay next = (DayNightController.TimeOfDay)(((int)DayNightController.Instance.Current + 1) % 3);
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
