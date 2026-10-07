using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// True while the user is typing in a Unity text field (e.g. the maintenance sheet in edit mode).
/// Keyboard shortcuts (WASD, Space, Z, C, N, ...) check it so typing does not move the worker or switch views.
/// </summary>
public static class UITextInput
{
    private static int checkedFrame = -1;
    private static bool typing;

    public static bool IsTyping
    {
        get
        {
            if (checkedFrame == Time.frameCount) return typing; // once per frame, many callers
            checkedFrame = Time.frameCount;

            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            typing = selected != null && selected.TryGetComponent(out TMP_InputField field) && field.isFocused;
            return typing;
        }
    }

    // The worker controller lives in its own assembly: hand it the check.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Install() => Player.ExternalInputBlock = () => IsTyping;
}
