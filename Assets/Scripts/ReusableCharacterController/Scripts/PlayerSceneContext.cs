using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class PlayerSceneContext : MonoBehaviour
{
    [SerializeField] private PlayerSceneSettings settings;
    [Tooltip("If empty, this GameObject's transform is the entry pose.")]
    [SerializeField] private Transform spawnPoint;
    [Header("Optional movement bounds for this scene")]
    [SerializeField] private Transform movementBoundsCenter;
    [SerializeField] private Vector3 movementBoundsSize = new Vector3(200f, 100f, 200f);
    [Header("Called after entry settings and suspension have been applied")]
    [SerializeField] private UnityEvent onPlayerReady = new UnityEvent();

    public PlayerSceneSettings Settings => settings;
    public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;
    public Transform MovementBoundsCenter => movementBoundsCenter;
    public Vector3 MovementBoundsSize => movementBoundsSize;
    public UnityEvent OnPlayerReady => onPlayerReady;
    internal void NotifyReady() { onPlayerReady.Invoke(); }
}
