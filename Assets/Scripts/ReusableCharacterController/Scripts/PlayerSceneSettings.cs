using UnityEngine;

[CreateAssetMenu(fileName = "PlayerSceneSettings", menuName = "Character Controller/Scene Settings")]
public sealed class PlayerSceneSettings : ScriptableObject
{
    [Tooltip("Start with the player hidden and inactive so a scene's own camera can take control.")]
    public bool startSuspended;
    [Tooltip("Left click on an Interactable object turns the worker camera into an orbit camera around it. " +
             "Off in this project: the orbit camera is only used in the explosion view.")]
    public bool allowClickToFocus = false;
    [Tooltip("FocusCam needs a target; selecting it here falls back to TPP.")]
    public CharacterViewStateMachine.ViewMode entryMode = CharacterViewStateMachine.ViewMode.TPP;
    [Tooltip("Restore this scene's last player pose and non-focus mode when returning during this play session.")]
    public bool restoreLastVisit = true;
    public bool lockCursorOnEntry = true;
}
