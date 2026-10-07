using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// One turbine: mock live data + operation effects.
/// React (through CommunicationManager):
///   - handleTurbineData every UpdateRate seconds while the worker is at this turbine or it is operating.
///   - GetAllTurbineData_Extern / GetTurbineData_Extern(id) ask for the values at any time.
/// The old UI Toolkit monitor is gone; React draws it from this data.
/// </summary>
public class TurbineData : MonoBehaviour
{
    [SerializeField]
    private TurbineScriptableObject data;

    [Header("Mock Data Simulation")]
    [Space]
    [SerializeField]
    private float UpdateRate = 3;

    [Space]
    [SerializeField]
    private Vector2 TemperatureRange;

    [SerializeField]
    private Vector2 PressureRange;

    [SerializeField]
    private Vector2 RPMRange;

    [SerializeField]
    private Vector2 FlowRateRange;

    [SerializeField]
    private Vector2 VibrationRange;

    [Header("VFX")]
    [SerializeField]
    private ParticleSystem[] effects;

    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;
    private static readonly List<TurbineData> activeTurbines = new List<TurbineData>();

    private SpinObjects cachedSpinObject;
    private bool workerInRange;
    private bool operating;

    public bool IsOperating => operating;

    private void Awake()
    {
        cachedSpinObject = GetComponentInChildren<SpinObjects>();
    }

    private void OnEnable()
    {
        activeTurbines.Add(this);
        InvokeRepeating(nameof(SimulateData), 1f, Mathf.Max(0.1f, UpdateRate));
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(SimulateData));
        activeTurbines.Remove(this);
    }

    private void Start()
    {
        if (HUDController.Instance != null)
        {
            HUDController.Instance.HideEquipmentButtons();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        workerInRange = true;
        if (HUDController.Instance != null)
        {
            HUDController.Instance.SetInRangeOfEquipment(EquipmentType.Turbine, gameObject);
        }

        SendData(); // React gets values immediately, not after up to UpdateRate seconds
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        workerInRange = false;
        if (HUDController.Instance != null)
        {
            HUDController.Instance.ResetInRangeOfEquipment(EquipmentType.Turbine, gameObject);
        }
    }

    public void SimulateData()
    {
        if (data == null) return;

        float temp = Random.Range(TemperatureRange.x, TemperatureRange.y);
        data.Temperature = temp.ToString("F2", Invariant);
        data.TemperatureF = (temp * 1.8f + 32f).ToString("F2", Invariant);

        data.SteamPressure = Random.Range(PressureRange.x, PressureRange.y).ToString("F2", Invariant);
        data.RPM = Random.Range(RPMRange.x, RPMRange.y).ToString("F2", Invariant);
        data.SteamMassFlowRate = Random.Range(FlowRateRange.x, FlowRateRange.y).ToString("F2", Invariant);
        data.Vibration = Random.Range(VibrationRange.x, VibrationRange.y).ToString("F2", Invariant);

        // Only stream while someone can see it; React can always ask with "turbine.get(All)".
        if (workerInRange || operating) SendData();
    }

    public void StartOperation()
    {
        operating = true;

        if (effects != null)
        {
            foreach (var effect in effects)
            {
                if (effect != null)
                {
                    effect.gameObject.SetActive(true);
                    effect.Play();
                }
            }
        }

        if (cachedSpinObject == null) cachedSpinObject = GetComponentInChildren<SpinObjects>();
        if (cachedSpinObject != null) cachedSpinObject.shouldSpin = true;

        SendData();
    }

    public void StopOperation()
    {
        operating = false;

        if (effects != null)
        {
            foreach (var effect in effects)
            {
                if (effect != null)
                {
                    effect.Stop();
                    effect.gameObject.SetActive(false);
                }
            }
        }

        if (cachedSpinObject == null) cachedSpinObject = GetComponentInChildren<SpinObjects>();
        if (cachedSpinObject != null) cachedSpinObject.shouldSpin = false;

        SendData();
    }

    // ------------------------------------------------------------------ React

    public TurbineDataPayload ToPayload()
    {
        var payload = new TurbineDataPayload { operating = operating, name = gameObject.name, id = gameObject.name };
        if (data == null) return payload;

        payload.id = data.TurbineID;
        payload.name = data.TurbineDisplayName;
        payload.type = data.TurbineType;
        payload.steamPressure = ParseValue(data.SteamPressure);
        payload.temperature = ParseValue(data.Temperature);
        payload.temperatureF = ParseValue(data.TemperatureF);
        payload.vibration = ParseValue(data.Vibration);
        payload.rpm = ParseValue(data.RPM);
        payload.steamMassFlowRate = ParseValue(data.SteamMassFlowRate);
        return payload;
    }

    private void SendData()
    {
        CommunicationManager.HandleTurbineData_Extern(ToPayload());
    }

    private static float ParseValue(string value)
    {
        return float.TryParse(value, System.Globalization.NumberStyles.Float, Invariant, out float result) ? result : 0f;
    }

    /// <summary>Values of every turbine in the current scene.</summary>
    public static TurbineDataPayload[] GetAllPayloads()
    {
        var list = new TurbineDataPayload[activeTurbines.Count];
        for (int i = 0; i < activeTurbines.Count; i++) list[i] = activeTurbines[i].ToPayload();
        return list;
    }

    /// <summary>Values of one turbine by id (or GameObject name); null if not in this scene.</summary>
    public static TurbineDataPayload GetPayload(string id)
    {
        foreach (TurbineData turbine in activeTurbines)
        {
            TurbineDataPayload payload = turbine.ToPayload();
            if (payload.id == id || turbine.gameObject.name == id) return payload;
        }

        return null;
    }
}
