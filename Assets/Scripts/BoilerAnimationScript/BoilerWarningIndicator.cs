using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One warning lamp. Every lamp in the scene registers itself; BoilerFluidController switches them all
/// together with SetAllWarnings, so all lamps blink in sync. Only one alarm sound plays at a time.
/// </summary>
public class BoilerWarningIndicator : MonoBehaviour
{
    public static BoilerWarningIndicator Instance { get; private set; }

    private static readonly List<BoilerWarningIndicator> all = new List<BoilerWarningIndicator>();

    /// <summary>Switches every warning lamp in the scene on / off. The alarm sound plays on one lamp only.</summary>
    public static void SetAllWarnings(bool active)
    {
        bool soundPlaying = false;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            BoilerWarningIndicator indicator = all[i];
            if (indicator == null) { all.RemoveAt(i); continue; }

            bool withSound = active && !soundPlaying && indicator.alarmAudioSource != null;
            indicator.SetWarning(active, withSound);
            if (withSound) soundPlaying = true;
        }
    }

    /// <summary>Number of lamps found (for checks).</summary>
    public static int Count => all.Count;

    [Header("Warning Light & Beacon")]
    [SerializeField] private Light warningLight;
    [SerializeField] private Renderer lampRenderer;
    [SerializeField] private Color warningColor = new Color(1f, 0.15f, 0.05f);
    [SerializeField] private float minIntensity = 0.3f;
    [SerializeField] private float maxIntensity = 3.5f;
    [SerializeField] private float pulseFrequency = 6.0f;

    [Header("Alarm Audio")]
    [SerializeField] private AudioSource alarmAudioSource;
    [SerializeField] private AudioClip alarmClip;
    [SerializeField, Range(0f, 1f)] private float alarmVolume = 0.45f;

    private Material lampMaterial;
    private bool isWarningActive = false;
    private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");
    private const string EmissionKeyword = "_EMISSION";

    public bool IsWarningActive => isWarningActive;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        if (!all.Contains(this)) all.Add(this);

        if (lampRenderer != null)
        {
            lampMaterial = lampRenderer.material;
            lampMaterial.EnableKeyword(EmissionKeyword);
            lampMaterial.SetColor(EmissionColorProp, Color.black);
        }

        if (warningLight != null)
        {
            warningLight.enabled = false;
            warningLight.color = warningColor;
            warningLight.intensity = 0f;
        }

        if (alarmAudioSource != null)
        {
            alarmAudioSource.playOnAwake = false;
            alarmAudioSource.loop = true;
            alarmAudioSource.volume = alarmVolume;
            if (alarmClip != null)
                alarmAudioSource.clip = alarmClip;
        }
    }

    private void Update()
    {
        if (!isWarningActive)
            return;

        // Realistic industrial fluttering emergency beacon
        float pulse = (Mathf.Sin(Time.time * pulseFrequency) + 1f) * 0.5f;
        float flutter = (Mathf.PerlinNoise(Time.time * 24f, 0.5f) - 0.5f) * 0.35f;
        float currentIntensity = Mathf.Clamp(Mathf.Lerp(minIntensity, maxIntensity, pulse) + flutter, 0f, 5f);

        if (warningLight != null)
        {
            warningLight.intensity = currentIntensity;
        }

        if (lampMaterial != null)
        {
            lampMaterial.SetColor(EmissionColorProp, warningColor * (currentIntensity * 1.5f));
        }
    }

    public void SetWarning(bool active) => SetWarning(active, true);

    public void SetWarning(bool active, bool withSound)
    {
        if (isWarningActive == active)
            return;

        isWarningActive = active;

        if (isWarningActive)
        {
            if (warningLight != null)
            {
                warningLight.enabled = true;
            }

            if (alarmAudioSource != null && withSound)
            {
                if (alarmClip != null && alarmAudioSource.clip == null)
                    alarmAudioSource.clip = alarmClip;

                if (!alarmAudioSource.isPlaying)
                    alarmAudioSource.Play();
            }
        }
        else
        {
            if (warningLight != null)
            {
                warningLight.intensity = 0f;
                warningLight.enabled = false;
            }

            if (lampMaterial != null)
            {
                lampMaterial.SetColor(EmissionColorProp, Color.black);
            }

            if (alarmAudioSource != null && alarmAudioSource.isPlaying)
            {
                alarmAudioSource.Stop();
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        all.Remove(this);

        if (alarmAudioSource != null && alarmAudioSource.isPlaying)
            alarmAudioSource.Stop();

        if (lampMaterial != null)
        {
            Destroy(lampMaterial);
            lampMaterial = null;
        }
    }
}
