using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class CharacterInputReader : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference crouchAction;
    [SerializeField] private InputActionReference runAction;

    [Header("Camera")]
    [SerializeField] private InputActionReference orbitAction;
    [SerializeField] private InputActionReference panAction;
    [SerializeField] private InputActionReference zoomAction;
    [SerializeField] private InputActionReference mouseInteractAction;

    private Player playerOwner;
    private InputAction move, look, jump, crouch, run;
    private bool suspended;

    private bool ControlBlocked => suspended || (playerOwner != null && playerOwner.IsControlBlocked);

    public Vector2 MoveInput => !ControlBlocked && move != null ? move.ReadValue<Vector2>() : Vector2.zero;
    public Vector2 LookInput => !ControlBlocked && look != null ? look.ReadValue<Vector2>() : Vector2.zero;
    public bool RunHeld => !ControlBlocked && run != null && run.IsPressed();
    public bool JumpHeld => !ControlBlocked && jump != null && jump.IsPressed();
    public bool JumpPressed => !ControlBlocked && jump != null && jump.WasPressedThisFrame();
    public bool CrouchHeld => !ControlBlocked && crouch != null && crouch.IsPressed();

    // Fly mode: Q / Space = up, E / Z = down.
    public bool FlyUpHeld => !ControlBlocked && ((jump != null && jump.IsPressed()) || KeyHeld(Key.Q));
    public bool FlyDownHeld => !ControlBlocked && ((crouch != null && crouch.IsPressed()) || KeyHeld(Key.E));

    private static bool KeyHeld(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[key].isPressed;
    }

    public bool IsOrbiting => !ControlBlocked && orbitAction != null && orbitAction.action.IsPressed();
    public bool IsPanning => !ControlBlocked && panAction != null && panAction.action.IsPressed();
    public Vector2 ZoomInput => !ControlBlocked && zoomAction != null ? zoomAction.action.ReadValue<Vector2>() : Vector2.zero;
    public bool IsMouseInteracting => !ControlBlocked && mouseInteractAction != null && mouseInteractAction.action.IsPressed();

    private void Awake()
    {
        playerOwner = GetComponent<Player>();
        move = Clone(moveAction);
        look = Clone(lookAction);
        jump = Clone(jumpAction);
        crouch = Clone(crouchAction);
        run = Clone(runAction);
    }

    private void OnEnable()
    {
        SetRuntimeActions(true);
        SetReferenceActions(true);
    }

    private void OnDisable()
    {
        SetRuntimeActions(false);
        SetReferenceActions(false);
    }

    private void OnDestroy()
    {
        move?.Dispose();
        look?.Dispose();
        jump?.Dispose();
        crouch?.Dispose();
        run?.Dispose();
    }

    private InputAction Clone(InputActionReference reference)
    {
        return reference != null ? reference.action.Clone() : null;
    }

    private void SetRuntimeActions(bool enabled)
    {
        SetAction(move, enabled);
        SetAction(look, enabled);
        SetAction(jump, enabled);
        SetAction(crouch, enabled);
        SetAction(run, enabled);
    }

    private void SetReferenceActions(bool enabled)
    {
        SetAction(orbitAction?.action, enabled);
        SetAction(panAction?.action, enabled);
        SetAction(zoomAction?.action, enabled);
        SetAction(mouseInteractAction?.action, enabled);
    }

    private void SetAction(InputAction action, bool enabled)
    {
        if (action == null) return;
        if (enabled) action.Enable(); else action.Disable();
    }

    internal void SetInputSuspended(bool value)
    {
        suspended = value;
        SetRuntimeActions(!value && isActiveAndEnabled);
    }
}