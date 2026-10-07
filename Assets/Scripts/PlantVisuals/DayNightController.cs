using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Day / Evening / Night for Main_Scene.
///   - Darkens / tints the whole view through ScreenTintOverlay (channel "time").
///   - Building lights: street lamps (bulbs + light pools) and floodlight glow on the walls of the main
///     buildings and chimney bases fade in (no real lights: cheap on WebGL).
///   - React: SetTimeOfDay_Extern("day" | "evening" | "night") -> handleTimeOfDayChanged.
///   - Testing in Unity: press N to cycle Day -> Evening -> Night, or use the context menu (⋮) of this component.
///
/// Created by Tools > Thermal Plant > 12. Create Day-Night + Building Lights (Main_Scene).
/// </summary>
public class DayNightController : MonoBehaviour
{
    public enum TimeOfDay { Day, Evening, Night }

    public static DayNightController Instance { get; private set; }

    [Header("Start")]
    [SerializeField] private TimeOfDay startTime = TimeOfDay.Day;

    [Header("Look (multiplied over the whole view)")]
    [SerializeField] private Color eveningTint = new Color(1f, 0.72f, 0.52f, 1f);
    [Tooltip("Night darkness. Brighter = more 'moonlight' on the unlit parts of the plant.")]
    [SerializeField] private Color nightTint = new Color(0.33f, 0.37f, 0.53f, 1f);

    [Header("Street Lamps")]
    [Tooltip("Parent of all street lamps (bulb + light pool renderers are found under it).")]
    [SerializeField] private Transform lampsRoot;
    [Tooltip("Template material of the bulbs (PlantPOC/AdditiveGlow).")]
    [SerializeField] private Material bulbMaterial;
    [Tooltip("Template material of the light pools on the ground (PlantPOC/AdditiveGlow).")]
    [SerializeField] private Material poolMaterial;
    [Tooltip("Template material of the floodlight glow on building walls / chimney bases (PlantPOC/AdditiveGlow).")]
    [SerializeField] private Material washMaterial;
    [Tooltip("Back-face material of the building light volumes (PlantPOC/StencilVolume). Volumes are only drawn at evening / night.")]
    [SerializeField] private Material volumeMaterial;
    [Tooltip("Template material of the soft light on building roofs (PlantPOC/AdditiveGlow).")]
    [SerializeField] private Material roofMaterial;
    [SerializeField] private Color bulbColor = new Color(1f, 0.85f, 0.55f, 1f);
    [SerializeField] private Color poolColor = new Color(1f, 0.72f, 0.38f, 0.55f);
    [SerializeField] private Color washColor = new Color(1f, 0.8f, 0.55f, 0.45f);
    [SerializeField] private Color roofColor = new Color(0.85f, 0.88f, 1f, 0.22f);
    [Tooltip("How bright the lamps are in the evening (0..1). Night = 1.")]
    [SerializeField, Range(0f, 1f)] private float eveningLampLevel = 0.5f;
    [SerializeField, Min(0.1f)] private float lampFadeSeconds = 1.5f;

    [Header("Testing")]
    [Tooltip("Key that cycles Day -> Evening -> Night (Unity Editor and build). None = off.")]
    [SerializeField] private Key cycleKey = Key.N;

    public TimeOfDay Current { get; private set; }

    private readonly List<Renderer> bulbRenderers = new List<Renderer>();
    private readonly List<Renderer> poolRenderers = new List<Renderer>();
    private readonly List<Renderer> washRenderers = new List<Renderer>();
    private readonly List<Renderer> roofRenderers = new List<Renderer>();
    private readonly List<Renderer> volumeRenderers = new List<Renderer>();
    private Material bulbInstance, poolInstance, washInstance, roofInstance;
    private float lampLevel, lampTarget;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        Instance = this;

        // Own copies of the lamp materials, so play mode never changes the material assets.
        if (bulbMaterial != null) bulbInstance = new Material(bulbMaterial);
        if (poolMaterial != null) poolInstance = new Material(poolMaterial);
        if (washMaterial != null) washInstance = new Material(washMaterial);
        if (roofMaterial != null) roofInstance = new Material(roofMaterial);

        if (lampsRoot != null)
        {
            foreach (Renderer r in lampsRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (bulbMaterial != null && r.sharedMaterial == bulbMaterial) { r.sharedMaterial = bulbInstance; bulbRenderers.Add(r); }
                else if (poolMaterial != null && r.sharedMaterial == poolMaterial) { r.sharedMaterial = poolInstance; poolRenderers.Add(r); }
                else if (washMaterial != null && r.sharedMaterial == washMaterial) { r.sharedMaterial = washInstance; washRenderers.Add(r); }
                else if (roofMaterial != null && r.sharedMaterial == roofMaterial) { r.sharedMaterial = roofInstance; roofRenderers.Add(r); }
                else if (volumeMaterial != null && r.sharedMaterial == volumeMaterial) volumeRenderers.Add(r);
            }
        }
    }

    private void Start()
    {
        SetTime(startTime, true);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (bulbInstance != null) Destroy(bulbInstance);
        if (poolInstance != null) Destroy(poolInstance);
        if (washInstance != null) Destroy(washInstance);
        if (roofInstance != null) Destroy(roofInstance);
    }

    private void Update()
    {
        if (cycleKey != Key.None && Keyboard.current != null && Keyboard.current[cycleKey].wasPressedThisFrame
            && !UITextInput.IsTyping)
            SetTime((TimeOfDay)(((int)Current + 1) % 3));

        if (!Mathf.Approximately(lampLevel, lampTarget))
        {
            lampLevel = Mathf.MoveTowards(lampLevel, lampTarget, Time.unscaledDeltaTime / lampFadeSeconds);
            ApplyLamps();
        }
    }

    // ------------------------------------------------------------------ public

    /// <summary>"day" | "evening" (or "dusk") | "night". False for anything else.</summary>
    public bool SetTime(string value)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "day": SetTime(TimeOfDay.Day); return true;
            case "evening": case "dusk": SetTime(TimeOfDay.Evening); return true;
            case "night": SetTime(TimeOfDay.Night); return true;
            default: return false;
        }
    }

    public void SetTime(TimeOfDay time, bool instant = false)
    {
        Current = time;

        Color tint = time == TimeOfDay.Night ? nightTint : time == TimeOfDay.Evening ? eveningTint : Color.white;
        if (ScreenTintOverlay.Instance != null) ScreenTintOverlay.Instance.SetTint("time", tint, instant);

        lampTarget = time == TimeOfDay.Night ? 1f : time == TimeOfDay.Evening ? eveningLampLevel : 0f;
        if (instant) { lampLevel = lampTarget; ApplyLamps(); }

        CommunicationManager.HandleTimeOfDayChanged_Extern(Name(time));
    }

    public static string Name(TimeOfDay time) => time == TimeOfDay.Night ? "night" : time == TimeOfDay.Evening ? "evening" : "day";

    [ContextMenu("Day")] private void ContextDay() => SetTime(TimeOfDay.Day);
    [ContextMenu("Evening")] private void ContextEvening() => SetTime(TimeOfDay.Evening);
    [ContextMenu("Night")] private void ContextNight() => SetTime(TimeOfDay.Night);

    // ------------------------------------------------------------------ lamps

    private void ApplyLamps()
    {
        bool on = lampLevel > 0.001f;

        // Off in daylight: the lamp glow renderers are not drawn at all.
        foreach (Renderer r in bulbRenderers) if (r != null) r.enabled = on;
        foreach (Renderer r in poolRenderers) if (r != null) r.enabled = on;
        foreach (Renderer r in washRenderers) if (r != null) r.enabled = on;
        foreach (Renderer r in roofRenderers) if (r != null) r.enabled = on;
        foreach (Renderer r in volumeRenderers) if (r != null) r.enabled = on;
        if (!on) return;

        if (bulbInstance != null) bulbInstance.SetColor(ColorId, new Color(bulbColor.r, bulbColor.g, bulbColor.b, bulbColor.a * lampLevel));
        if (poolInstance != null) poolInstance.SetColor(ColorId, new Color(poolColor.r, poolColor.g, poolColor.b, poolColor.a * lampLevel));
        if (washInstance != null) washInstance.SetColor(ColorId, new Color(washColor.r, washColor.g, washColor.b, washColor.a * lampLevel));
        if (roofInstance != null) roofInstance.SetColor(ColorId, new Color(roofColor.r, roofColor.g, roofColor.b, roofColor.a * lampLevel));
    }
}
