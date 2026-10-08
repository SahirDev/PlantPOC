using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Control room guided tour on the Plant UI canvas (screen card instead of the floating card in the room).
/// The room still shows the floor arrows, the ring + marker and plays the chime (TourVisuals).
///   - Before the tour: a small "Guided Tour - Start" pill.
///   - During the tour: the step card - step x of 6, title, instruction, live status, VOLTAGE slider, Back / Next / X.
/// ControlRoomTour fills it; PlantUI shows it only in the control room while the worker is in control.
/// Built by Tools > Thermal Plant > 18. Update Plant UI - restyle freely, keep the references.
/// </summary>
public class TourPanel : MonoBehaviour
{
    [Header("Start (before the tour)")]
    [SerializeField] private GameObject startPill;
    [SerializeField] private Button startButton;

    [Header("Step card")]
    [SerializeField] private GameObject card;
    [SerializeField] private TMP_Text caption;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text body;
    [SerializeField] private RectTransform dotsRow;
    [SerializeField] private Sprite dotSprite;
    [SerializeField] private Image statusIcon;
    [SerializeField] private TMP_Text status;
    [SerializeField] private GameObject voltageRow;
    [SerializeField] private Slider voltageSlider;
    [SerializeField] private TMP_Text voltageValue;
    [SerializeField] private Button backButton;
    [SerializeField] private Button nextButton;
    [SerializeField] private TMP_Text nextLabel;
    [SerializeField] private Button closeButton;

    [Header("Colours")]
    [SerializeField] private Color accent = new Color(1f, 0.62f, 0.25f, 1f);
    [SerializeField] private Color done = new Color(0.35f, 0.88f, 0.5f, 1f);
    [SerializeField] private Color danger = new Color(1f, 0.38f, 0.32f, 1f);
    [SerializeField] private Color dotIdle = new Color(1f, 1f, 1f, 0.18f);

    private readonly List<Image> dots = new List<Image>();
    private bool showingStart = true;

    private static ControlRoomTour Tour => ControlRoomTour.Instance;

    private void Awake()
    {
        if (startButton != null) startButton.onClick.AddListener(() => { if (Tour != null) Tour.StartTour(); });
        if (nextButton != null) nextButton.onClick.AddListener(() => { if (Tour != null) Tour.Next(); });
        if (backButton != null) backButton.onClick.AddListener(() => { if (Tour != null) Tour.Back(); });
        if (closeButton != null) closeButton.onClick.AddListener(() => { if (Tour != null) Tour.StopTour(); });
        if (voltageSlider != null)
        {
            voltageSlider.minValue = 0f;
            voltageSlider.maxValue = 100f;
            voltageSlider.wholeNumbers = true;
            voltageSlider.onValueChanged.AddListener(v => { if (Tour != null) Tour.SetVoltage(v); });
        }
    }

    /// <summary>Called by PlantUI.</summary>
    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
    }

    // ------------------------------------------------------------------ content (ControlRoomTour)

    public void ShowStart()
    {
        showingStart = true;
        SetActive(startPill, true);
        SetActive(card, false);
    }

    /// <param name="stepIndex">0-based; -1 = tour complete.</param>
    public void ShowStep(int stepIndex, int total, string stepTitle, string instruction, bool canBack, string next, bool showVoltage)
    {
        showingStart = false;
        SetActive(startPill, false);
        SetActive(card, true);

        if (caption != null) caption.text = stepIndex < 0 ? "GUIDED TOUR  ·  COMPLETE" : $"STEP {stepIndex + 1} OF {total}";
        if (title != null) title.text = stepTitle;
        if (body != null) body.text = instruction;
        if (nextLabel != null) nextLabel.text = next;
        SetActive(backButton != null ? backButton.gameObject : null, canBack);
        SetActive(closeButton != null ? closeButton.gameObject : null, stepIndex >= 0);
        SetActive(voltageRow, showVoltage);
        if (stepIndex < 0)
        {
            if (status != null) status.text = "";
            if (statusIcon != null) statusIcon.enabled = false;
            if (nextButton != null) nextButton.interactable = true;
        }

        BuildDots(total);
        for (int i = 0; i < dots.Count; i++)
        {
            bool isDone = stepIndex < 0 || i < stepIndex;
            dots[i].color = isDone ? done : i == stepIndex ? accent : dotIdle;
            dots[i].rectTransform.localScale = Vector3.one * (i == stepIndex ? 1.25f : 1f);
        }
    }

    /// <summary>Every frame while a step runs: progress text, Next state, voltage.</summary>
    public void UpdateLive(string statusText, bool stepDone, bool showTick, bool canNext, float voltage, float red, float warning)
    {
        if (showingStart) return;
        bool voltageShown = voltageRow != null && voltageRow.activeSelf;

        if (statusIcon != null) statusIcon.enabled = showTick;
        if (status != null)
        {
            status.text = statusText;
            status.color = stepDone ? done : voltage >= warning && voltageShown ? danger : accent;
        }
        if (nextButton != null) nextButton.interactable = canNext;

        if (voltageShown)
        {
            if (voltageSlider != null && !Mathf.Approximately(voltageSlider.value, voltage)) voltageSlider.SetValueWithoutNotify(voltage);
            if (voltageValue != null)
            {
                voltageValue.text = $"{voltage:F0} %";
                voltageValue.color = voltage >= warning ? danger : voltage >= red ? accent : done;
            }
        }
    }

    private void BuildDots(int total)
    {
        if (dotsRow == null || dots.Count == total) return;
        foreach (Image d in dots) if (d != null) Destroy(d.gameObject);
        dots.Clear();
        for (int i = 0; i < total; i++)
        {
            var go = new GameObject("Dot", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.layer = gameObject.layer;
            go.transform.SetParent(dotsRow, false);
            var image = go.GetComponent<Image>();
            image.sprite = dotSprite;
            image.raycastTarget = false;
            var layout = go.GetComponent<LayoutElement>();
            layout.preferredWidth = layout.preferredHeight = 12f;
            dots.Add(image);
        }
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }
}
