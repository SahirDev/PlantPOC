using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class ProximityAudioSource : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioClip audioClip;

    [Header("Player")]
    [SerializeField] private Transform player;

    [Header("Proximity Settings")]
    [SerializeField] private float playRange = 5f;

    [Header("Audio Settings")]
    [Range(0f, 1f)]
    [SerializeField] private float volume = 1f;

    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();

        // Configure AudioSource
        audioSource.clip = audioClip;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f; // 3D sound
        audioSource.volume = volume;
    }

    private void Start()
    {
        // Automatically find Player if not assigned
        if (player == null)
        {
            GameObject playerObject = GameObject.Find("Worker");

            if (playerObject != null)
            {
                player = playerObject.transform;
            }
            else
            {
                Debug.LogWarning($"No Player found for {gameObject.name}. " );
            }
        }
    }

    private void Update()
    {
        if (player == null || audioClip == null)
            return;

        float distance = Vector3.Distance(  player.position, transform.position );

        // Player is inside range
        if (distance <= playRange)
        {
            if (!audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }
        // Player is outside range
        else
        {
            if (audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, playRange);
    }
}