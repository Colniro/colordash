using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : NetworkBehaviour
{
    public static PlayerMovement LocalPlayer { get; private set; }

    public Camera playerCamera;
    public float walkSpeed = 6f;
    public float runSpeed = 12f;
    public float jumpPower = 7f;
    public float gravity = 10f;
    public float lookSpeed = 2f;
    public float lookXLimit = 45f;
    public float defaultHeight = 2f;
    public float crouchHeight = 1f;
    public float crouchSpeed = 3f;

    [Header("Slippery Floor")]
    public float groundAcceleration = 30f;
    public float airAcceleration = 10f;

    [Header("Fail-safe")]
    public float fallRespawnHeight = -10f;

    private Vector3 moveDirection = Vector3.zero;
    private float rotationX = 0;
    private CharacterController characterController;
    private bool canMove = true;
    private Keyboard keyboard;
    private Mouse mouse;
    private bool hasReportedFall = false;

    void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            LocalPlayer = this;
            keyboard = Keyboard.current;
            mouse = Mouse.current;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(false);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && LocalPlayer == this) LocalPlayer = null;
    }

    void Update()
    {
        if (!IsOwner) return;

        if (keyboard == null) keyboard = Keyboard.current;
        if (mouse == null) mouse = Mouse.current;
        if (keyboard == null || mouse == null) return;

        Vector3 forward = transform.TransformDirection(Vector3.forward);
        Vector3 right = transform.TransformDirection(Vector3.right);

        // Read keyboard input directly
        bool isRunning = keyboard.leftShiftKey.isPressed;
        float moveY = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
        float moveX = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
        bool jumpPressed = keyboard.spaceKey.wasPressedThisFrame;
        bool crouchPressed = keyboard.rKey.isPressed;

        // Read mouse input
        Vector2 mouseDelta = mouse.delta.ReadValue();

        float curSpeedX = canMove ? (isRunning ? runSpeed : walkSpeed) * moveY : 0;
        float curSpeedY = canMove ? (isRunning ? runSpeed : walkSpeed) * moveX : 0;
        float movementDirectionY = moveDirection.y;

        // Slippery floor: ease horizontal velocity towards the target instead of snapping to it,
        // so momentum carries the player past where they meant to stop.
        Vector3 targetHorizontalMove = (forward * curSpeedX) + (right * curSpeedY);
        Vector3 currentHorizontalMove = new Vector3(moveDirection.x, 0f, moveDirection.z);
        float accel = characterController.isGrounded ? groundAcceleration : airAcceleration;
        currentHorizontalMove = Vector3.MoveTowards(currentHorizontalMove, targetHorizontalMove, accel * Time.deltaTime);

        moveDirection = currentHorizontalMove;

        if (jumpPressed && canMove && characterController.isGrounded)
        {
            moveDirection.y = jumpPower;
        }
        else
        {
            moveDirection.y = movementDirectionY;
        }

        if (!characterController.isGrounded)
        {
            moveDirection.y -= gravity * Time.deltaTime;
        }

        if (crouchPressed && canMove)
        {
            characterController.height = crouchHeight;
            walkSpeed = crouchSpeed;
            runSpeed = crouchSpeed;
        }
        else
        {
            characterController.height = defaultHeight;
            walkSpeed = 6f;
            runSpeed = 12f;
        }

        characterController.Move(moveDirection * Time.deltaTime);

        if (canMove)
        {
            rotationX += -mouseDelta.y * lookSpeed;
            rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);
            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
            transform.rotation *= Quaternion.Euler(0, mouseDelta.x * lookSpeed, 0);
        }

        if (transform.position.y < fallRespawnHeight)
        {
            if (!hasReportedFall)
            {
                hasReportedFall = true;
                GameFlowManager.Instance?.ReportFellServerRpc();
            }
        }
        else
        {
            hasReportedFall = false;
        }
    }

    [ClientRpc]
    public void TeleportClientRpc(Vector3 position)
    {
        if (!IsOwner) return;

        characterController.enabled = false;
        transform.position = position;
        characterController.enabled = true;
        moveDirection = Vector3.zero;
        hasReportedFall = false;

        ClientNetworkTransform cnt = GetComponent<ClientNetworkTransform>();
        if (cnt != null) cnt.Teleport(position, transform.rotation, transform.lossyScale);
    }
}
