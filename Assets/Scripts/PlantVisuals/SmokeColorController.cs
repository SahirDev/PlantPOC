using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Chimney smoke indication slider (Main_Scene). Level 0..1 -> smoke colour of every "Smoke loop" particle effect:
///   0.0  no smoke (emission stops, existing smoke fades out)
///   0.2  white / light gray  - normal operation / steam-heavy exhaust (default)
///   0.4  dark gray           - combustion efficiency degrading / elevated particulate load
///   0.6  black               - severe incomplete combustion / high soot
///   0.8  yellow-brown        - possible NOx / SOx abnormal-emission warning
///   1.0  blue-gray           - possible oil / fuel contamination or abnormal combustion
/// In-between values blend between the neighbouring colours. New smoke takes the colour (old puffs fade out),
/// and the colour itself eases over ~1 s, so the change looks natural.
///
/// Installs itself in any scene that contains "Smoke loop" particle effects (that is Main_Scene) - no scene setup.
/// React: SetSmokeLevel_Extern("0".."1") / GetSmokeLevel_Extern -> handleSmokeLevelChanged.
/// Testing in Unity: Play, select "Smoke Color Controller" in the Hierarchy and drag Level.
/// </summary>
public class SmokeColorController : MonoBehaviour
{
    public const float DefaultLevel = 0.2f;
    private const string SmokeNamePrefix = "Smoke loop";

    private static readonly float[] StopLevels = { 0.2f, 0.4f, 0.6f, 0.8f, 1f };
    private static readonly Color[] StopColors =
    {
        new Color(0.86f, 0.86f, 0.86f), // white / light gray
        new Color(0.36f, 0.36f, 0.36f), // dark gray
        new Color(0.06f, 0.06f, 0.06f), // black
        new Color(0.55f, 0.41f, 0.18f), // yellow-brown
        new Color(0.45f, 0.53f, 0.64f)  // blue-gray
    };
    private static readonly string[] StopStatus = { "normal", "degrading", "severe", "nox_sox", "contamination" };
    private static readonly string[] StopIndication =
    {
        "Normal operation / steam-heavy exhaust",
        "Combustion efficiency degrading / elevated particulate load",
        "Severe incomplete combustion / high soot condition",
        "Possible NOx/SOx-related abnormal-emission warning",
        "Possible oil/fuel contamination or abnormal combustion"
    };

    public static SmokeColorController Instance { get; private set; }

    [Tooltip("0 = no smoke, 0.2 white, 0.4 dark gray, 0.6 black, 0.8 yellow-brown, 1 blue-gray.")]
    [SerializeField, Range(0f, 1f)] private float level = DefaultLevel;
    [Tooltip("Seconds for the colour to ease to a new level.")]
    [SerializeField, Min(0f)] private float colorFadeSeconds = 1f;

    private class Smoke
    {
        public ParticleSystem system;
        public float alphaMin, alphaMax;
    }

    private readonly List<Smoke> smokes = new List<Smoke>();
    private Color currentColor, targetColor;
    private float appliedLevel = -1f;

    public float Level => level;

    // ------------------------------------------------------------------ self-install

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++) TryInstall(SceneManager.GetSceneAt(i));
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryInstall(scene);

    private static void TryInstall(Scene scene)
    {
        if (!scene.isLoaded) return;
        if (Instance != null && Instance.gameObject.scene == scene) return; // already installed

        var systems = new List<ParticleSystem>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
                if (IsSmoke(ps.transform)) systems.Add(ps);
        if (systems.Count == 0) return;

        var go = new GameObject("Smoke Color Controller");
        SceneManager.MoveGameObjectToScene(go, scene); // unloads with the scene
        go.AddComponent<SmokeColorController>().Init(systems);
    }

    // The ParticleSystem itself or one of its parents is called "Smoke loop ...".
    private static bool IsSmoke(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.name.StartsWith(SmokeNamePrefix, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void Init(List<ParticleSystem> systems)
    {
        Instance = this;

        foreach (ParticleSystem ps in systems)
        {
            ParticleSystem.MinMaxGradient start = ps.main.startColor;
            float aMin = start.mode == ParticleSystemGradientMode.TwoColors ? start.colorMin.a : start.color.a;
            float aMax = start.mode == ParticleSystemGradientMode.TwoColors ? start.colorMax.a : start.color.a;
            smokes.Add(new Smoke { system = ps, alphaMin = aMin, alphaMax = Mathf.Max(aMin, aMax) });
        }

        currentColor = targetColor = ColorFor(level);
        ApplyColor(currentColor);
        ApplyLevel(level, true);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ public

    /// <summary>0..1 (values above 1 are read as 0..100 percent).</summary>
    public void SetLevel(float value)
    {
        if (value > 1f) value /= 100f;
        level = Mathf.Clamp01(value);
        ApplyLevel(level, false);
    }

    public SmokeStatusPayload BuildStatus()
    {
        int stop = NearestStop(level);
        Color c = ColorFor(level);
        return new SmokeStatusPayload
        {
            level = level,
            status = level <= 0.001f ? "none" : StopStatus[stop],
            indication = level <= 0.001f ? "No smoke - emission stopped" : StopIndication[stop],
            color = "#" + ColorUtility.ToHtmlStringRGB(c)
        };
    }

    // ------------------------------------------------------------------ apply

    private void OnValidate()
    {
        // Inspector slider while playing.
        if (Application.isPlaying && smokes.Count > 0) ApplyLevel(level, false);
    }

    private void ApplyLevel(float value, bool instant)
    {
        if (Mathf.Approximately(value, appliedLevel)) return;
        appliedLevel = value;

        bool on = value > 0.001f;
        foreach (Smoke s in smokes)
        {
            if (s.system == null) continue;
            if (on && !s.system.isEmitting) s.system.Play(true);
            else if (!on && s.system.isEmitting) s.system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        if (on)
        {
            targetColor = ColorFor(value);
            if (instant) { currentColor = targetColor; ApplyColor(currentColor); }
        }

        CommunicationManager.HandleSmokeLevelChanged_Extern(BuildStatus());
    }

    private void Update()
    {
        if (currentColor == targetColor) return;

        float step = colorFadeSeconds <= 0f ? 1f : Time.deltaTime / colorFadeSeconds;
        currentColor = new Color(Mathf.MoveTowards(currentColor.r, targetColor.r, step),
            Mathf.MoveTowards(currentColor.g, targetColor.g, step), Mathf.MoveTowards(currentColor.b, targetColor.b, step), 1f);
        ApplyColor(currentColor);
    }

    // Two-colour random start colour (slightly darker / lighter puffs), keeping each effect's own transparency.
    private void ApplyColor(Color c)
    {
        Color darker = new Color(c.r * 0.82f, c.g * 0.82f, c.b * 0.82f);
        foreach (Smoke s in smokes)
        {
            if (s.system == null) continue;
            ParticleSystem.MainModule main = s.system.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(darker.r, darker.g, darker.b, s.alphaMin),
                new Color(c.r, c.g, c.b, s.alphaMax));
        }
    }

    private static Color ColorFor(float value)
    {
        if (value <= StopLevels[0]) return StopColors[0];
        for (int i = 1; i < StopLevels.Length; i++)
        {
            if (value <= StopLevels[i])
            {
                float t = Mathf.InverseLerp(StopLevels[i - 1], StopLevels[i], value);
                return Color.Lerp(StopColors[i - 1], StopColors[i], t);
            }
        }
        return StopColors[StopColors.Length - 1];
    }

    private static int NearestStop(float value)
    {
        int best = 0;
        for (int i = 1; i < StopLevels.Length; i++)
            if (Mathf.Abs(StopLevels[i] - value) < Mathf.Abs(StopLevels[best] - value)) best = i;
        return best;
    }
}
