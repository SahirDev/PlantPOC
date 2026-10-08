using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Equipment info panel of the Plant UI (top-left): Boiler, Turbine or Electrical Panel. Shown by PlantUI while the
/// worker is at that equipment. Header with status (Running / Standby / High / Alarm) and collapse button,
/// parameter rows refreshed every <see cref="refreshSeconds"/>, a slider that drives the equipment and (boiler /
/// turbine) a Start / Stop Operation button. Everything goes through the same functions React uses.
///   Boiler     - slider = Burner Power  -> water level falls, temperature / pressure rise
///   Turbine    - slider = Steam Load    -> temperature, pressure, speed, flow, vibration rise
///   Electrical - slider = Voltage       -> generator kV, loading, oil / winding temperature rise; 75 % high, 80 % alarm
/// </summary>
public class EquipmentInfoPanel : MonoBehaviour
{
    public enum Kind { Boiler, Turbine, Electrical }

    [SerializeField] private Kind kind = Kind.Boiler;
    [SerializeField, Min(1f)] private float refreshSeconds = 5f;

    [Header("Header")]
    [SerializeField] private TMP_Text title;
    [SerializeField] private Image statusDot;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button collapseButton;
    [SerializeField] private Image collapseIcon;
    [SerializeField] private Sprite collapseSprite;  // chevron up (open)
    [SerializeField] private Sprite expandSprite;    // chevron down (collapsed)
    [Tooltip("Everything under the header (hidden when collapsed).")]
    [SerializeField] private GameObject body;

    [Header("Operation (boiler / turbine)")]
    [SerializeField] private Button operationButton;
    [SerializeField] private TMP_Text operationLabel;
    [SerializeField] private Image operationIcon;
    [SerializeField] private Sprite startSprite;
    [SerializeField] private Sprite stopSprite;
    [SerializeField] private Color startColor = new Color(0.12f, 0.36f, 0.28f, 1f);
    [SerializeField] private Color stopColor = new Color(0.45f, 0.14f, 0.14f, 1f);

    [Header("Values (in row order)")]
    [SerializeField] private TMP_Text[] values = new TMP_Text[0];

    [Header("Slider")]
    [SerializeField] private Slider slider;
    [SerializeField] private TMP_Text sliderValue;

    [Header("Status colours")]
    [SerializeField] private Color runningColor = new Color(0.3f, 0.9f, 0.92f);
    [SerializeField] private Color standbyColor = new Color(0.6f, 0.65f, 0.75f);
    [SerializeField] private Color highColor = new Color(1f, 0.74f, 0.25f);
    [SerializeField] private Color alarmColor = new Color(1f, 0.38f, 0.32f);

    private GameObject source;
    private BoilerFluidController boiler;
    private TurbineData turbine;
    private ElectricalPanelInfo electrical;
    private bool collapsed;
    private float nextRefresh, nextStatus;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // ------------------------------------------------------------------ setup

    private void Awake()
    {
        if (collapseButton != null) collapseButton.onClick.AddListener(ToggleCollapsed);
        if (operationButton != null) operationButton.onClick.AddListener(ToggleOperation);
        if (slider != null)
        {
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(OnSlider);
        }
    }

    /// <summary>Called by PlantUI: show for this equipment object (boiler / turbine / electrical panel) or hide.</summary>
    public void Show(bool show, GameObject equipment)
    {
        if (!show)
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            return;
        }

        if (equipment != source)
        {
            source = equipment;
            boiler = null; turbine = null; electrical = null;
            if (equipment != null)
            {
                boiler = equipment.GetComponentInChildren<BoilerFluidController>();
                if (boiler == null) boiler = equipment.GetComponentInParent<BoilerFluidController>();
                turbine = equipment.GetComponentInParent<TurbineData>();
                if (turbine == null) turbine = equipment.GetComponentInChildren<TurbineData>();
                equipment.TryGetComponent(out electrical);
            }
            if (boiler == null && kind == Kind.Boiler) boiler = BoilerFluidController.instance;
            nextRefresh = 0f;
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
            nextRefresh = 0f;
            SyncSlider();
        }
    }

    public void ToggleCollapsed()
    {
        collapsed = !collapsed;
        if (body != null) body.SetActive(!collapsed);
        if (collapseIcon != null) collapseIcon.sprite = collapsed ? expandSprite : collapseSprite;
    }

    // ------------------------------------------------------------------ per frame

    private void Update()
    {
        if (Time.unscaledTime >= nextStatus)
        {
            nextStatus = Time.unscaledTime + 0.3f;
            RefreshStatus();
        }

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + refreshSeconds;
            RefreshValues();
        }
    }

    private void RefreshStatus()
    {
        string text;
        Color color;
        bool operating = false;

        switch (kind)
        {
            case Kind.Boiler:
                operating = boiler != null && boiler.IsOperating;
                text = operating ? "Running" : "Standby";
                color = operating ? runningColor : standbyColor;
                break;
            case Kind.Turbine:
                operating = turbine != null && turbine.IsOperating;
                text = operating ? "Running" : "Standby";
                color = operating ? runningColor : standbyColor;
                break;
            default:
                float v = electrical != null ? electrical.GeneratorValue : 0f;
                bool alarm = electrical != null && v >= electrical.WarningThreshold;
                bool high = electrical != null && v >= electrical.RedThreshold;
                text = alarm ? "Alarm" : high ? "High" : v > 0.5f ? "Running" : "Standby";
                color = alarm ? alarmColor : high ? highColor : v > 0.5f ? runningColor : standbyColor;
                if (alarm) color.a = Mathf.PingPong(Time.unscaledTime * 3f, 1f) > 0.5f ? 1f : 0.35f;
                break;
        }

        if (statusText != null) { statusText.text = text; statusText.color = color; }
        if (statusDot != null) statusDot.color = color;

        if (operationButton != null)
        {
            if (operationLabel != null) operationLabel.text = operating ? "Stop Operation" : "Start Operation";
            if (operationIcon != null) operationIcon.sprite = operating ? stopSprite : startSprite;
            if (operationButton.targetGraphic != null) operationButton.targetGraphic.color = operating ? stopColor : startColor;
        }
    }

    private void RefreshValues()
    {
        switch (kind)
        {
            case Kind.Boiler:
            {
                BoilerMonitorValues.Values v = BoilerMonitorValues.Current();
                Set(0, $"{v.temperature:F1} °C");
                Set(1, $"{v.waterLevel:F1} %");
                Set(2, $"{v.pressure:F2} bar");
                Set(3, $"{v.burner:F0} %");
                Set(4, $"{v.valve:F0} %");
                Set(5, $"{v.winding:F0} °C");
                break;
            }
            case Kind.Turbine:
            {
                if (turbine == null) break;
                TurbineDataPayload p = turbine.ToPayload();
                if (title != null && !string.IsNullOrEmpty(p.name)) title.text = p.name;
                Set(0, $"{p.temperature:F1} °C");
                Set(1, $"{p.steamPressure:F1} bar");
                Set(2, $"{p.rpm:F0} rpm");
                Set(3, $"{p.steamMassFlowRate:F1} kg/s");
                Set(4, $"{p.vibration:F1} µm");
                break;
            }
            default:
            {
                if (electrical != null) electrical.RefreshUI(); // new live values -> HUD.LastElectricalValues
                ElectricalValuesPayload e = HUDController.HasInstance ? HUDController.Instance.LastElectricalValues : null;
                if (e == null) break;
                Set(0, $"{e.generatorKV:F0} KV");
                Set(1, $"{e.gridKV:F0} KV");
                Set(2, $"{e.loading:F0} %");
                Set(3, $"{e.oilTemp:F0} °C");
                Set(4, $"{e.windingTemp:F0} °C");
                break;
            }
        }

        SyncSlider();
    }

    private void Set(int index, string text)
    {
        if (index < values.Length && values[index] != null) values[index].text = text;
    }

    // ------------------------------------------------------------------ controls

    private void OnSlider(float value)
    {
        if (sliderValue != null) sliderValue.text = $"{value:F0} %";
        CommunicationManager bridge = CommunicationManager.HasInstance ? CommunicationManager.Instance : null;
        string text = value.ToString(Invariant);

        switch (kind)
        {
            case Kind.Boiler:
                if (bridge != null) bridge.SetBoilerBurnerPower_Extern(text);
                else if (boiler != null) boiler.SetBurnerPower(value / 100f);
                break;
            case Kind.Turbine:
                if (turbine != null) turbine.SetLoad(value / 100f);
                break;
            default:
                if (bridge != null) bridge.SetControlRoomSlider_Extern(text);
                else if (electrical != null) electrical.SetGeneratorValue(value);
                break;
        }

        // Show the effect soon (then back to the normal 5 s rhythm).
        nextRefresh = Mathf.Min(nextRefresh, Time.unscaledTime + 0.4f);
    }

    private void SyncSlider()
    {
        if (slider == null) return;
        float value = kind == Kind.Boiler ? (boiler != null ? boiler.BurnerPower * 100f : slider.value)
            : kind == Kind.Turbine ? (turbine != null ? turbine.Load * 100f : slider.value)
            : (electrical != null ? electrical.GeneratorValue : slider.value);
        if (!Mathf.Approximately(slider.value, value)) slider.SetValueWithoutNotify(value);
        if (sliderValue != null) sliderValue.text = $"{value:F0} %";
    }

    private void ToggleOperation()
    {
        if (!CommunicationManager.HasInstance) return;
        CommunicationManager bridge = CommunicationManager.Instance;
        if (kind == Kind.Boiler)
        {
            if (boiler != null && boiler.IsOperating) bridge.BoilerStopOperation_Extern();
            else bridge.BoilerStartOperation_Extern();
        }
        else if (kind == Kind.Turbine)
        {
            if (turbine != null && turbine.IsOperating) bridge.TurbineStopOperation_Extern();
            else bridge.TurbineStartOperation_Extern();
        }
        nextStatus = 0f;
    }
}
