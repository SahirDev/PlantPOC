using UnityEngine;

public class BoilerWarningIndicator : MonoBehaviour
{
    public static BoilerWarningIndicator Instance { get; private set; }

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

    public void SetWarning(bool active)
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

            if (alarmAudioSource != null)
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

        if (alarmAudioSource != null && alarmAudioSource.isPlaying)
            alarmAudioSource.Stop();

        if (lampMaterial != null)
        {
            Destroy(lampMaterial);
            lampMaterial = null;
        }
    }
}
