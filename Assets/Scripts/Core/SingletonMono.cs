using System.Collections;
using UnityEngine;

/// <summary>
/// Generic MonoBehaviour singleton.
///
///     public class HUDController : SingletonMono&lt;HUDController&gt;
///     {
///         protected override bool PersistAcrossScenes => true;   // optional (default false)
///
///         protected override void Awake()
///         {
///             base.Awake();                // always call base first
///             if (IsDuplicate) return;     // a second copy is being removed
///             // your Awake code
///         }
///
///         public override void LateStart() { }  // optional: runs one frame after Start,
///                                               // when every other object has started
///     }
///
///     // anywhere:
///     HUDController.Instance.ToggleExplode();
///     if (HUDController.HasInstance) { ... }
///
/// - Instance is found automatically, even before the object's own Awake has run.
/// - If a second copy appears (e.g. the same manager in several scenes), the newer one removes itself.
/// - When overriding Awake / Start / OnDestroy, call base.Awake() / base.Start() / base.OnDestroy().
/// </summary>
public abstract class SingletonMono<T> : MonoBehaviour where T : SingletonMono<T>
{
    private static T instance;

    /// <summary>The active instance. Searched once if it hasn't registered yet; null if none exists.</summary>
    public static T Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<T>();
            return instance;
        }
    }

    /// <summary>True when an instance exists.</summary>
    public static bool HasInstance => Instance != null;

    /// <summary>Keep this object alive when scenes change (DontDestroyOnLoad). Default: false.</summary>
    protected virtual bool PersistAcrossScenes => false;

    /// <summary>
    /// Duplicate handling: true = destroy the duplicate's whole GameObject (right for dedicated manager
    /// objects), false = destroy only the duplicate component.
    /// </summary>
    protected virtual bool DestroyDuplicateGameObject => true;

    /// <summary>True on a duplicate copy that is being removed. Check it after base.Awake().</summary>
    protected bool IsDuplicate { get; private set; }

    protected virtual void Awake()
    {
        if (instance != null && instance != this)
        {
            IsDuplicate = true;

            if (DestroyDuplicateGameObject)
            {
                // Deactivate at once: Destroy only happens at the end of the frame.
                gameObject.SetActive(false);
                Destroy(gameObject);
            }
            else
            {
                enabled = false;
                Destroy(this);
            }

            return;
        }

        instance = (T)this;

        if (PersistAcrossScenes)
        {
            // DontDestroyOnLoad only works on root objects.
            if (transform.parent != null) transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
        }

        OnSingletonAwake();
    }

    protected virtual void Start()
    {
        if (!IsDuplicate) StartCoroutine(LateStartRoutine());
    }

    protected virtual void OnDestroy()
    {
        if (instance != this) return;

        OnSingletonDestroy();
        instance = null;
    }

    private IEnumerator LateStartRoutine()
    {
        yield return null;
        LateStart();
    }

    /// <summary>Runs one frame after Start, when all other objects in the scene have started.</summary>
    public virtual void LateStart() { }

    /// <summary>Called once, only on the real instance. Alternative to overriding Awake.</summary>
    protected virtual void OnSingletonAwake() { }

    /// <summary>Called when the real instance is destroyed. Alternative to overriding OnDestroy.</summary>
    protected virtual void OnSingletonDestroy() { }
}
