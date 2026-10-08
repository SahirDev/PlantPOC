#if UNITY_EDITOR
// Editor-only test buttons for the Boiler / Turbine rooms. This whole file is compiled ONLY in the Unity Editor:
// it is not in any build (React / WebGL / standalone). Delete it any time - nothing depends on it.
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Small panel (top-left) in Play mode while the worker is at a boiler or turbine, or in the explosion view:
/// Explode All, Collapse All, Start / Stop Operation (+ Show / Hide Info and the burner power slider for the boiler).
/// The buttons call the same CommunicationManager functions React uses, so this tests the real path.
/// F1 hides / shows the panel.
/// </summary>
public class EditorTestPanel : MonoBehaviour
{
    private bool hidden;
    private float burnerSlider = -1f; // 0..100, like React's burner slider
    private GUIStyle box, title, label;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var go = new GameObject("[Editor] Test Panel") { hideFlags = HideFlags.DontSave };
        DontDestroyOnLoad(go);
        go.AddComponent<EditorTestPanel>();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) hidden = !hidden;
    }

    private void OnGUI()
    {
        if (hidden || !HUDController.HasInstance || !CommunicationManager.HasInstance) return;

        HUDController hud = HUDController.Instance;
        EquipmentType type = hud.CurrentEquipmentType;
        bool turbine = type == EquipmentType.Turbine, boiler = type == EquipmentType.Boiler;
        if (!turbine && !boiler && !hud.IsExplodedView) return;

        EnsureStyles();
        CommunicationManager bridge = CommunicationManager.Instance;

        GUILayout.BeginArea(new Rect(12f, 12f, 230f, boiler ? 320f : 260f), box);
        GUILayout.Label("TEST (Editor only)", title);
        GUILayout.Label($"{type}: {hud.CurrentEquipmentName}", label);
        GUILayout.Label($"Exploded: {(hud.IsExplodedView ? "yes" : "no")}   Operating: {(hud.IsOperatingEquipment ? "yes" : "no")}", label);
        GUILayout.Space(6f);

        if (GUILayout.Button("Explode All"))
        {
            if (boiler) bridge.BoilerExplodeAll_Extern(); else bridge.TurbineExplodeAll_Extern();
        }

        if (GUILayout.Button("Collapse All"))
        {
            if (boiler) bridge.BoilerCollapseAll_Extern(); else bridge.TurbineCollapseAll_Extern();
        }

        if (GUILayout.Button(hud.IsOperatingEquipment ? "Stop Operation" : "Start Operation"))
        {
            if (boiler)
            {
                if (hud.IsOperatingEquipment) bridge.BoilerStopOperation_Extern(); else bridge.BoilerStartOperation_Extern();
            }
            else
            {
                if (hud.IsOperatingEquipment) bridge.TurbineStopOperation_Extern(); else bridge.TurbineStartOperation_Extern();
            }
        }

        if (boiler && GUILayout.Button(hud.IsBoilerInfoOpen ? "Hide Info" : "Show Info"))
        {
            if (hud.IsBoilerInfoOpen) bridge.BoilerHideInfo_Extern(); else bridge.BoilerShowInfo_Extern();
        }

        if (boiler)
        {
            // Same call as React's burner slider: flame, water level and boiling sound follow it.
            BoilerFluidController fluid = BoilerFluidController.instance;
            if (burnerSlider < 0f) burnerSlider = fluid != null ? fluid.BurnerPower * 100f : 45f;
            GUILayout.Space(4f);
            GUILayout.Label($"Burner power: {burnerSlider:F0} %", label);
            float value = Mathf.Round(GUILayout.HorizontalSlider(burnerSlider, 0f, 100f));
            if (!Mathf.Approximately(value, burnerSlider))
            {
                burnerSlider = value;
                bridge.SetBoilerBurnerPower_Extern(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        GUILayout.Space(4f);
        GUILayout.Label("F1: hide / show this panel", label);
        GUILayout.EndArea();
    }

    private void EnsureStyles()
    {
        if (box != null) return;

        var background = new Texture2D(1, 1);
        background.SetPixel(0, 0, new Color(0.05f, 0.07f, 0.13f, 0.88f));
        background.Apply();

        box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 10) };
        box.normal.background = background;
        title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 12 };
        title.normal.textColor = new Color(1f, 0.62f, 0.25f);
        label = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
        label.normal.textColor = new Color(0.85f, 0.88f, 0.95f);
    }
}
#endif
