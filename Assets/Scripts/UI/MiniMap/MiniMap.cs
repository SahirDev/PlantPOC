using UnityEngine;

/// <summary>
/// OLD minimap camera – not used any more. The minimap is now a fixed image (MiniMapArea per scene)
/// shown by MiniMapPanel, with a UI icon for the worker: no camera renders anything for it.
///
/// This class only stays so scenes/prefabs that still have it don't show "Missing script".
/// It switches its camera off. Delete the MiniMapCamera object (e.g. from the Worker prefab).
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
public class MiniMap : MonoBehaviour
{
    private void Awake() => SwitchOff();
    private void OnEnable() => SwitchOff();

    private void SwitchOff()
    {
        Camera cam = GetComponent<Camera>();
        if (cam != null) cam.enabled = false;
    }
}
