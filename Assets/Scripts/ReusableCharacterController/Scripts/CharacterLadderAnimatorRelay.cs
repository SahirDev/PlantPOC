using UnityEngine;

/// <summary>
/// Added automatically to the assigned Animator by CharacterLadderController.
/// Keeps the Inspector's Apply Root Motion value unchanged.
/// Do not combine with another OnAnimatorMove root-motion handler on that Animator.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class CharacterLadderAnimatorRelay : MonoBehaviour
{
    private CharacterLadderController ladder;
    private Animator targetAnimator;
    private Player playerOwner;
    private CharacterViewStateMachine viewModes;

    internal void Configure(CharacterLadderController controller, Animator animator)
    {
        ladder = controller;
        targetAnimator = animator;
        playerOwner = controller.GetComponent<Player>();
        viewModes = controller.GetComponent<CharacterViewStateMachine>();
    }

    private void OnAnimatorMove()
    {
        if (targetAnimator == null) targetAnimator = GetComponent<Animator>();
        // Camera modes and suspension own the player pose; animation must not
        // add motion to it. Do not change the Inspector's Apply Root Motion value.
        if (playerOwner != null && playerOwner.IsControlBlocked) return;
        if (viewModes != null &&
            (viewModes.CurrentMode == CharacterViewStateMachine.ViewMode.FlyCam ||
             viewModes.CurrentMode == CharacterViewStateMachine.ViewMode.FocusCam)) return;
        if (ladder != null && ladder.IsClimbing)
            ladder.RestoreMotorPoseAfterAnimation();
        else if (targetAnimator.applyRootMotion)
            targetAnimator.ApplyBuiltinRootMotion();
    }
}
