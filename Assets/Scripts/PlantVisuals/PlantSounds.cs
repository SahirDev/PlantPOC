using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Room sounds, installed automatically (no scene setup):
///   BoilerRoom  -> BoilerSound on the boiler: water boiling, louder and faster with the burner power.
///   TurbineRoom -> TurbineSound on every turbine: 3D hum, louder as the worker gets closer (and when running).
/// Clips: Assets/Resources/Audio/BoilerBoiling.wav and TurbineHum.wav (replace them to change the sound,
/// or assign another clip on the component). Master volume: SetSoundVolume_Extern / PlantSounds.SetMasterVolume.
/// </summary>
public static class PlantSounds
{
    public const string BoilerClipPath = "Audio/BoilerBoiling";
    public const string TurbineClipPath = "Audio/TurbineHum";

    public static float MasterVolume => AudioListener.volume;

    /// <summary>0..1 for every sound of the app (values above 1 are read as percent).</summary>
    public static void SetMasterVolume(float volume)
    {
        if (volume > 1f) volume /= 100f;
        AudioListener.volume = Mathf.Clamp01(volume);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++) InstallIn(SceneManager.GetSceneAt(i));
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => InstallIn(scene);

    private static void InstallIn(Scene scene)
    {
        if (!scene.isLoaded) return;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (BoilerFluidController boiler in root.GetComponentsInChildren<BoilerFluidController>(true))
                if (boiler.GetComponent<BoilerSound>() == null) boiler.gameObject.AddComponent<BoilerSound>();

            foreach (TurbineData turbine in root.GetComponentsInChildren<TurbineData>(true))
                if (turbine.GetComponent<TurbineSound>() == null) turbine.gameObject.AddComponent<TurbineSound>();
        }
    }

    internal static AudioSource CreateLoop(GameObject owner, AudioClip clip, float spatialBlend, float minDistance, float maxDistance)
    {
        var source = owner.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.volume = 0f;
        source.spatialBlend = spatialBlend;
        source.rolloffMode = AudioRolloffMode.Linear; // fades to silence at maxDistance (Logarithmic never does)
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;
        return source;
    }
}
