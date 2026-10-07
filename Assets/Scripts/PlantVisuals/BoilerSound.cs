using UnityEngine;

/// <summary>
/// Boiling water at the boiler. Burner power (0..1) sets loudness and speed: more power = louder, faster
/// bubbling (pitch up); less = quieter and slower; 0 = silent. Changes ease in, never jump.
/// </summary>
public class BoilerSound : MonoBehaviour
{
    [SerializeField] private AudioClip clip;
    [Tooltip("Loudness at full burner power.")]
    [SerializeField, Range(0f, 1f)] private float maxVolume = 0.8f;
    [Tooltip("Speed (pitch) at no / full burner power.")]
    [SerializeField] private Vector2 pitchRange = new Vector2(0.75f, 1.35f);
    [Tooltip("Loudness factor while the boiler is not operating (still hot water). 1 = same as operating.")]
    [SerializeField, Range(0f, 1f)] private float idleFactor = 0.6f;
    [Tooltip("0 = same everywhere in the room, 1 = fully 3D (from the boiler).")]
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 0.5f;
    [SerializeField, Min(0.1f)] private float easeSeconds = 1.2f;

    private BoilerFluidController boiler;
    private AudioSource source;

    private void Start()
    {
        boiler = GetComponent<BoilerFluidController>();
        if (clip == null) clip = Resources.Load<AudioClip>(PlantSounds.BoilerClipPath);
        if (clip == null) { enabled = false; return; }

        source = PlantSounds.CreateLoop(gameObject, clip, spatialBlend, 6f, 40f);
        source.time = Random.Range(0f, clip.length * 0.9f);
    }

    private void Update()
    {
        if (source == null) return;

        float power = boiler != null ? boiler.BurnerPower : 0.45f;
        float shape = Mathf.Pow(power, 0.8f); // audible early, full at the top
        float targetVolume = maxVolume * shape * (boiler != null && boiler.IsOperating ? 1f : idleFactor);
        float targetPitch = Mathf.Lerp(pitchRange.x, pitchRange.y, power);

        float step = Time.deltaTime / easeSeconds;
        source.volume = Mathf.MoveTowards(source.volume, targetVolume, step * maxVolume);
        source.pitch = Mathf.MoveTowards(source.pitch, targetPitch, step * (pitchRange.y - pitchRange.x));

        if (source.volume > 0.001f) { if (!source.isPlaying) source.Play(); }
        else if (source.isPlaying) source.Pause();
    }
}
