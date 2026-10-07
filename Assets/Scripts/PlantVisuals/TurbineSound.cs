using UnityEngine;

/// <summary>
/// Turbine hum, fully 3D: loud next to the turbine, fading out with distance (silent beyond Max Distance).
/// Louder and higher (spinning up) while the turbine is operating.
/// </summary>
public class TurbineSound : MonoBehaviour
{
    [SerializeField] private AudioClip clip;
    [SerializeField, Range(0f, 1f)] private float standbyVolume = 0.45f;
    [SerializeField, Range(0f, 1f)] private float runningVolume = 0.9f;
    [SerializeField] private float standbyPitch = 0.85f;
    [SerializeField] private float runningPitch = 1.05f;
    [Tooltip("Full loudness within this distance (m).")]
    [SerializeField, Min(0.1f)] private float minDistance = 3f;
    [Tooltip("Silent beyond this distance (m).")]
    [SerializeField, Min(1f)] private float maxDistance = 28f;
    [Tooltip("Seconds to spin up / down.")]
    [SerializeField, Min(0.1f)] private float easeSeconds = 2.5f;

    private TurbineData turbine;
    private AudioSource source;

    private void Start()
    {
        turbine = GetComponent<TurbineData>();
        if (clip == null) clip = Resources.Load<AudioClip>(PlantSounds.TurbineClipPath);
        if (clip == null) { enabled = false; return; }

        // On a child at the turbine's visual centre (the pivot can be at the floor / one end).
        var emitter = new GameObject("TurbineSound");
        emitter.transform.SetParent(transform, false);
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            emitter.transform.position = b.center;
        }

        source = PlantSounds.CreateLoop(emitter, clip, 1f, minDistance, maxDistance);
        source.pitch = standbyPitch;
        source.time = Random.Range(0f, clip.length * 0.9f); // 4 turbines never in phase
        source.Play();
    }

    private void Update()
    {
        if (source == null) return;

        bool running = turbine != null && turbine.IsOperating;
        float step = Time.deltaTime / easeSeconds;
        source.volume = Mathf.MoveTowards(source.volume, running ? runningVolume : standbyVolume, step);
        source.pitch = Mathf.MoveTowards(source.pitch, running ? runningPitch : standbyPitch, step * Mathf.Abs(runningPitch - standbyPitch));
    }
}
