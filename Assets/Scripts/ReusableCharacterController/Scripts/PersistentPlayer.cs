using UnityEngine;

[DefaultExecutionOrder(-32000)]
[DisallowMultipleComponent]
public sealed class PersistentPlayer : MonoBehaviour
{
    public static PersistentPlayer Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInstance() { Instance = null; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Deactivate immediately: Destroy itself is deferred until the end of the frame.
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }
        if (transform.parent != null) transform.SetParent(null, true);
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
