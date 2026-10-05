using System.Collections;
using UnityEngine;

/// <summary>
/// Lives in the tiny Bootstrap scene, the only scene in Build Settings.
/// The WebGL page opens with just engine + code + this scene, then this script downloads and opens
/// the first real scene (Main_Scene) through SceneController, so React can show real progress
/// ("scene.downloadProgress") instead of a blank page.
/// </summary>
public class BootstrapLoader : MonoBehaviour
{
    [Tooltip("Addressable address of the first scene to open.")]
    [SerializeField] private string firstScene = "Main_Scene";

    private IEnumerator Start()
    {
        // One frame so CommunicationManager has sent "unity.ready" and React is listening.
        yield return null;

        if (!SceneController.HasInstance)
        {
            Debug.LogError("[Bootstrap] No SceneController found. Add one to the Bootstrap scene (Tools > Thermal Plant > Setup Addressables does this).", this);
            yield break;
        }

        SceneController.Instance.ChangeScene(firstScene);
    }
}
