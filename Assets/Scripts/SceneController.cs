using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads scenes. Persistent singleton (one for the whole session).
///
/// Scenes marked Addressable (Main_Scene and the rooms) are downloaded on demand, then cached by the
/// browser. Scenes in Build Settings (Bootstrap) load normally. Callers don't need to know which is which:
///     SceneController.Instance.ChangeScene("BoilerRoom");
///
/// Sends to React (through CommunicationManager): handleSceneLoading, handleSceneDownloadProgress,
/// handleSceneLoaded (from CommunicationManager), handleSceneLoadFailed, handleScenePreloaded,
/// handleSceneDownloadSize.
/// </summary>
public class SceneController : SingletonMono<SceneController>
{
    [Header("Background download")]
    [Tooltip("After the first scene has loaded, quietly download these rooms one by one so they open instantly later.")]
    [SerializeField] private bool preloadInBackground = true;
    [SerializeField] private string[] backgroundPreloadScenes = { "BoilerRoom", "TurbineRoom", "Control_Room" };
    [Tooltip("Seconds to wait after the first scene has loaded before background downloads start.")]
    [SerializeField, Min(0f)] private float preloadDelay = 5f;

    [Header("React")]
    [Tooltip("Seconds between \"scene.downloadProgress\" events.")]
    [SerializeField, Min(0.05f)] private float progressInterval = 0.1f;

    protected override bool PersistAcrossScenes => true;

    private const float BytesPerMB = 1024f * 1024f;

    private bool isChangingScene;
    private bool addressablesReady;
    private bool backgroundPreloadStarted;
    private AsyncOperationHandle<SceneInstance> currentSceneHandle;

    public bool IsChangingScene => isChangingScene;

    // ================================================================== public API (unchanged for callers)

    /// <summary>Downloads (if needed) and opens a scene. Ignored while another scene is loading.</summary>
    public void ChangeScene(string sceneName, Action callback = null)
    {
        if (isChangingScene) return;

        if (string.IsNullOrEmpty(sceneName))
        {
            CommunicationManager.HandleError_Extern(nameof(ChangeScene), "Scene name is empty.");
            return;
        }

        StartCoroutine(LoadSceneRoutine(sceneName, callback));
    }

    public void ChangeScene(int sceneIndex)
    {
        if (sceneIndex < 0 || sceneIndex >= SceneManager.sceneCountInBuildSettings) return;

        string path = SceneUtility.GetScenePathByBuildIndex(sceneIndex);
        ChangeScene(System.IO.Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>Answers with handleSceneDownloadSize { name, exists, cached, sizeMB }.</summary>
    public void RequestDownloadSize(string sceneName)
    {
        StartCoroutine(GetDownloadSizeRoutine(sceneName));
    }

    /// <summary>Downloads a scene quietly without opening it. Done: handleScenePreloaded.</summary>
    public void Preload(string sceneName)
    {
        StartCoroutine(PreloadRoutine(sceneName, true));
    }

    // ================================================================== loading

    private IEnumerator LoadSceneRoutine(string sceneName, Action onDone)
    {
        if (isChangingScene) yield break;
        isChangingScene = true;

        CommunicationManager.HandleSceneLoading_Extern(sceneName);

        string error = null;
        yield return EnsureAddressablesReady();

        bool isAddressable = false;
        yield return IsAddressableScene(sceneName, result => isAddressable = result);

        if (isAddressable)
        {
            // 1. Download (only if not cached yet), with progress for React.
            yield return DownloadRoutine(sceneName, false, e => error = e);

            // 2. Load. Single mode unloads the previous scene.
            if (error == null)
            {
                AsyncOperationHandle<SceneInstance> previous = currentSceneHandle;
                AsyncOperationHandle<SceneInstance> load = Addressables.LoadSceneAsync(sceneName, LoadSceneMode.Single, true, 100);
                yield return load;

                if (load.Status == AsyncOperationStatus.Succeeded)
                {
                    currentSceneHandle = load;
                    if (previous.IsValid()) Addressables.Release(previous);
                }
                else
                {
                    error = $"Could not load scene '{sceneName}': {load.OperationException?.Message}";
                    if (load.IsValid()) Addressables.Release(load);
                }
            }
        }
        else if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            yield return load;

            if (currentSceneHandle.IsValid()) Addressables.Release(currentSceneHandle);
            currentSceneHandle = default;
        }
        else
        {
            error = $"Scene '{sceneName}' is neither Addressable nor in Build Settings.";
        }

        if (error == null)
        {
            // Free textures/meshes of the room we just left (WebGL memory is limited).
            yield return Resources.UnloadUnusedAssets();

            if (HUDController.HasInstance) HUDController.Instance.HideEquipmentButtons();
        }

        isChangingScene = false;

        if (error != null)
        {
            Debug.LogError("[SceneController] " + error);
            CommunicationManager.HandleSceneLoadFailed_Extern(sceneName, error);
            yield break;
        }

        onDone?.Invoke();
        StartBackgroundPreloadOnce();
    }

    // Downloads the scene's bundles into the browser cache. Nothing happens if already cached.
    private IEnumerator DownloadRoutine(string sceneName, bool background, Action<string> onError)
    {
        AsyncOperationHandle<long> sizeHandle = Addressables.GetDownloadSizeAsync(sceneName);
        yield return sizeHandle;

        long totalBytes = sizeHandle.Status == AsyncOperationStatus.Succeeded ? sizeHandle.Result : 0;
        Addressables.Release(sizeHandle);

        if (totalBytes <= 0) yield break; // cached (or nothing to download)

        AsyncOperationHandle download = Addressables.DownloadDependenciesAsync(sceneName, false);
        float nextReport = 0f;

        while (!download.IsDone)
        {
            if (Time.unscaledTime >= nextReport)
            {
                nextReport = Time.unscaledTime + progressInterval;
                EmitProgress(sceneName, download, totalBytes, background);
            }

            yield return null;
        }

        bool succeeded = download.Status == AsyncOperationStatus.Succeeded;
        string message = download.OperationException?.Message;
        Addressables.Release(download);

        if (!succeeded)
        {
            onError?.Invoke($"Download of '{sceneName}' failed: {message}");
            yield break;
        }

        CommunicationManager.HandleSceneDownloadProgress_Extern(new SceneDownloadProgressPayload
        {
            name = sceneName,
            progress = 1f,
            downloadedMB = totalBytes / BytesPerMB,
            totalMB = totalBytes / BytesPerMB,
            background = background
        });
    }

    private static void EmitProgress(string sceneName, AsyncOperationHandle download, long totalBytes, bool background)
    {
        DownloadStatus status = download.GetDownloadStatus();
        long total = status.TotalBytes > 0 ? status.TotalBytes : totalBytes;
        float progress = status.TotalBytes > 0 ? status.Percent : download.PercentComplete;

        CommunicationManager.HandleSceneDownloadProgress_Extern(new SceneDownloadProgressPayload
        {
            name = sceneName,
            progress = Mathf.Clamp01(progress),
            downloadedMB = status.DownloadedBytes / BytesPerMB,
            totalMB = total / BytesPerMB,
            background = background
        });
    }

    // ================================================================== size / preload

    private IEnumerator GetDownloadSizeRoutine(string sceneName)
    {
        yield return EnsureAddressablesReady();

        bool isAddressable = false;
        yield return IsAddressableScene(sceneName, result => isAddressable = result);

        if (!isAddressable)
        {
            bool inBuild = !string.IsNullOrEmpty(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName);
            CommunicationManager.HandleSceneDownloadSize_Extern(new SceneDownloadSizePayload { name = sceneName, exists = inBuild, cached = inBuild, sizeMB = 0f });
            yield break;
        }

        AsyncOperationHandle<long> sizeHandle = Addressables.GetDownloadSizeAsync(sceneName);
        yield return sizeHandle;
        long bytes = sizeHandle.Status == AsyncOperationStatus.Succeeded ? sizeHandle.Result : 0;
        Addressables.Release(sizeHandle);

        CommunicationManager.HandleSceneDownloadSize_Extern(new SceneDownloadSizePayload { name = sceneName, exists = true, cached = bytes <= 0, sizeMB = bytes / BytesPerMB });
    }

    private IEnumerator PreloadRoutine(string sceneName, bool reportErrors)
    {
        yield return EnsureAddressablesReady();

        bool isAddressable = false;
        yield return IsAddressableScene(sceneName, result => isAddressable = result);

        if (!isAddressable)
        {
            if (reportErrors) CommunicationManager.HandleError_Extern(nameof(Preload), $"Scene '{sceneName}' is not Addressable.");
            yield break;
        }

        string error = null;
        yield return DownloadRoutine(sceneName, true, e => error = e);

        if (error != null)
        {
            if (reportErrors) CommunicationManager.HandleError_Extern(nameof(Preload), error);
            yield break;
        }

        CommunicationManager.HandleScenePreloaded_Extern(sceneName);
    }

    private void StartBackgroundPreloadOnce()
    {
        if (!preloadInBackground || backgroundPreloadStarted || backgroundPreloadScenes == null) return;

        backgroundPreloadStarted = true;
        StartCoroutine(BackgroundPreloadRoutine());
    }

    private IEnumerator BackgroundPreloadRoutine()
    {
        yield return new WaitForSecondsRealtime(preloadDelay);

        foreach (string sceneName in backgroundPreloadScenes)
        {
            if (string.IsNullOrEmpty(sceneName)) continue;

            // Never compete with a scene the user is waiting for.
            while (isChangingScene) yield return null;

            if (sceneName == SceneManager.GetActiveScene().name) continue;

            yield return PreloadRoutine(sceneName, false);
        }
    }

    // ================================================================== helpers

    private IEnumerator EnsureAddressablesReady()
    {
        if (addressablesReady) yield break;

        var init = Addressables.InitializeAsync(false);
        yield return init;

        if (init.Status != AsyncOperationStatus.Succeeded)
            Debug.LogError("[SceneController] Addressables failed to initialize: " + init.OperationException?.Message);

        addressablesReady = true;
        if (init.IsValid()) Addressables.Release(init);
    }

    private static IEnumerator IsAddressableScene(string sceneName, Action<bool> result)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            result(false);
            yield break;
        }

        var locations = Addressables.LoadResourceLocationsAsync(sceneName, typeof(SceneInstance));
        yield return locations;

        result(locations.Status == AsyncOperationStatus.Succeeded && locations.Result != null && locations.Result.Count > 0);
        Addressables.Release(locations);
    }
}
