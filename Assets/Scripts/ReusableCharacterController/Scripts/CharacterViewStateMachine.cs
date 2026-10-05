using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class CharacterViewStateMachine : MonoBehaviour
{
    public enum ViewMode { TPP, FPP, FlyCam, FocusCam }

    [Header("Initial State")]
    [SerializeField] private ViewMode initialMode = ViewMode.TPP;

    [Header("Input Actions")]
    [SerializeField] private InputActionReference switchToTPPAction;
    [SerializeField] private InputActionReference switchToFPPAction;
    [SerializeField] private InputActionReference switchToFlyCamAction;

    [Header("Runtime State")]
    [SerializeField] private ViewMode currentMode;

    private Player playerOwner;
    private InputAction tppAction, fppAction, flyAction;

    public ViewMode CurrentMode => currentMode;
    public ViewMode InitialMode => initialMode == ViewMode.FocusCam ? ViewMode.TPP : initialMode;
    public event Action<ViewMode, ViewMode> OnViewModeChanging;
    public event Action<ViewMode> OnViewModeChanged;

    private bool ControlBlocked => playerOwner != null && playerOwner.IsControlBlocked;

    private void Awake()
    {
        playerOwner = GetComponent<Player>();
        CreateActions();
        ChangeMode(InitialMode, true);
    }

    private void OnEnable()
    {
        SetActionsEnabled(true);
    }

    private void OnDisable()
    {
        SetActionsEnabled(false);
    }

    private void OnDestroy()
    {
        tppAction?.Dispose();
        fppAction?.Dispose();
        flyAction?.Dispose();
    }

    private void CreateActions()
    {
        tppAction = CreateAction(switchToTPPAction, ctx => SwitchToTPP());
        fppAction = CreateAction(switchToFPPAction, ctx => SwitchToFPP());
        flyAction = CreateAction(switchToFlyCamAction, ctx => SwitchToFlyCam());
    }

    private InputAction CreateAction(InputActionReference reference, Action<InputAction.CallbackContext> callback)
    {
        if (reference == null) return null;
        InputAction action = reference.action.Clone();
        action.performed += callback;
        return action;
    }

    private void SetActionsEnabled(bool enabled)
    {
        SetActionEnabled(tppAction, enabled);
        SetActionEnabled(fppAction, enabled);
        SetActionEnabled(flyAction, enabled);
    }

    private void SetActionEnabled(InputAction action, bool enabled)
    {
        if (action == null) return;
        if (enabled) action.Enable(); else action.Disable();
    }

    internal void SetInputSuspended(bool suspended)
    {
        SetActionsEnabled(!suspended);
    }

    public void SwitchToTPP() => ChangeMode(ViewMode.TPP);
    public void SwitchToFPP() => ChangeMode(ViewMode.FPP);
    public void SwitchToFlyCam() => ChangeMode(ViewMode.FlyCam);
    public void SwitchToMode(ViewMode mode) => ChangeMode(mode);

    internal void SetSceneMode(ViewMode mode)
    {
        currentMode = mode == ViewMode.FocusCam ? InitialMode : mode;
    }

    private void ChangeMode(ViewMode newMode, bool force = false)
    {
        if (!force && (ControlBlocked || currentMode == newMode)) return;

        if (newMode == ViewMode.FocusCam)
        {
            CharacterFocusController focus = GetComponent<CharacterFocusController>();

            if (focus == null || !focus.HasFocusSession)
            {
                Debug.LogWarning("Enter Focus Cam through CharacterFocusController.Focus(target).", this);
                return;
            }
        }

        OnViewModeChanging?.Invoke(currentMode, newMode);
        currentMode = newMode;
        OnViewModeChanged?.Invoke(currentMode);
    }
}