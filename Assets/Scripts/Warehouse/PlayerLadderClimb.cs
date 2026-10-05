using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerLadderClimb : MonoBehaviour
{
    [Header("Climb Settings")]
    [SerializeField] private float climbSpeed = 5f;
    [SerializeField] private string ladderTag = "Ladder";

    [SerializeField] private InputActionReference MovementInputAction;
    [SerializeField] private InputActionReference JumpInputAction;

    private CharacterController controller;
    private bool isInsideLadderZone = false;
    private bool isClimbing = false;

    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (MovementInputAction == null || MovementInputAction.action == null) return;

        if (isInsideLadderZone)
        {
            float verticalInput = MovementInputAction.action.ReadValue<Vector2>().y;

            // If the player presses up/down while in the zone, initiate climbing
            if (Mathf.Abs(verticalInput) > 0.1f)
            {
                isClimbing = true;
            }
        }

        if (isClimbing)
        {
            ClimbLogic();
        }
    }

    void ClimbLogic()
    {
        if (MovementInputAction == null || MovementInputAction.action == null) return;

        float verticalInput = MovementInputAction.action.ReadValue<Vector2>().y;

        // Move the character strictly vertically up or down
        Vector3 moveDirection = new Vector3(0, verticalInput * climbSpeed, 0);

        // Use CharacterController to move without accumulating default gravity
        if (controller != null)
        {
            controller.Move(moveDirection * Time.deltaTime);
        }

        // Optional: Exit climbing if the player jumps off
        if (JumpInputAction != null && JumpInputAction.action != null && JumpInputAction.action.WasPerformedThisFrame())
        {
            ExitLadder();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(ladderTag))
        {
            isInsideLadderZone = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(ladderTag))
        {
            ExitLadder();
        }
    }

    private void ExitLadder()
    {
        isInsideLadderZone = false;
        isClimbing = false;
    }
}
