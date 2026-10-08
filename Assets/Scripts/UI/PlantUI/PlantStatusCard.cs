using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One equipment card of the Plant Status panel (Boiler 01, Turbine 03, ...): title, status, value rows and a
/// read-only bar (load / smoke level). Only shows values - PlantStatusPanel fills it. The chevron (or the header)
/// collapses it. Built by Tools > Thermal Plant > 18; restyle freely, keep the references.
/// </summary>
public class PlantStatusCard : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private TMP_Text title;
    [SerializeField] private Image statusDot;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button collapseButton;
    [SerializeField] private Image collapseIcon;
    [SerializeField] private Sprite collapseSprite;
    [SerializeField] private Sprite expandSprite;
    [SerializeField] private GameObject body;

    [Header("Values (left column top to bottom, then right column)")]
    [SerializeField] private TMP_Text[] values = new TMP_Text[0];

    [Header("Bar (read only)")]
    [SerializeField] private Image barFill;
    [SerializeField] private RectTransform barKnob;
    [SerializeField] private TMP_Text barValue;

    private bool collapsed;
    private Color[] defaultColors;
    private bool alert;
    private Color alertColor = new Color(1f, 0.32f, 0.3f);
    private Outline outline;
    private Color outlineColor;

    private void Awake()
    {
        if (collapseButton != null) collapseButton.onClick.AddListener(ToggleCollapsed);
    }

    /// <summary>Warning look: blinking red status dot, red border.</summary>
    public void SetAlert(bool on, Color color)
    {
        alertColor = color;
        if (alert == on) return;
        alert = on;
        if (outline == null && TryGetComponent(out outline)) outlineColor = outline.effectColor;
        if (outline != null)
        {
            outline.effectColor = on ? new Color(color.r, color.g, color.b, 0.9f) : outlineColor;
            outline.effectDistance = on ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
        }
        if (!on && statusDot != null) statusDot.canvasRenderer.SetAlpha(1f);
    }

    /// <summary>Value in the alert colour (true) or back to its own colour (false).</summary>
    public void SetValueAlert(int index, bool on)
    {
        if (index < 0 || index >= values.Length || values[index] == null) return;
        if (defaultColors == null)
        {
            defaultColors = new Color[values.Length];
            for (int i = 0; i < values.Length; i++) defaultColors[i] = values[i] != null ? values[i].color : Color.white;
        }
        values[index].color = on ? alertColor : defaultColors[index];
    }

    private void Update()
    {
        if (!alert || statusDot == null) return;
        statusDot.canvasRenderer.SetAlpha(Mathf.PingPong(Time.unscaledTime * 2.5f, 1f) > 0.5f ? 1f : 0.25f);
    }

    public void ToggleCollapsed()
    {
        collapsed = !collapsed;
        if (body != null) body.SetActive(!collapsed);
        if (collapseIcon != null) collapseIcon.sprite = collapsed ? expandSprite : collapseSprite;
    }

    public void SetTitle(string text)
    {
        if (title != null && title.text != text) title.text = text;
    }

    public void SetStatus(string text, Color color)
    {
        if (statusText != null) { statusText.text = text; statusText.color = color; }
        if (statusDot != null) statusDot.color = color;
    }

    public void SetValue(int index, string text)
    {
        if (index >= 0 && index < values.Length && values[index] != null) values[index].text = text;
    }

    public void SetValueColor(int index, Color color)
    {
        if (index >= 0 && index < values.Length && values[index] != null) values[index].color = color;
    }

    /// <param name="fill01">0..1 bar fill.</param>
    public void SetBar(float fill01, string text)
    {
        fill01 = Mathf.Clamp01(fill01);
        if (barFill != null) barFill.fillAmount = fill01;
        if (barKnob != null)
        {
            barKnob.anchorMin = new Vector2(fill01, 0.5f);
            barKnob.anchorMax = new Vector2(fill01, 0.5f);
            barKnob.anchoredPosition = Vector2.zero;
        }
        if (barValue != null) barValue.text = text;
    }
}
