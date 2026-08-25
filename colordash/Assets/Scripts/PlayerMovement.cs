using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : NetworkBehaviour
{
    public static PlayerMovement LocalPlayer { get; private set; }

    // Alle gespawnten Spieler - genutzt von der Zuschauerkamera und der Spielerliste im HUD.
    public static readonly List<PlayerMovement> All = new List<PlayerMovement>();

    public Camera playerCamera;
    public float walkSpeed = 6f;
    public float runSpeed = 12f;
    public float jumpPower = 7f;
    public float gravity = 10f;
    public float lookXLimit = 45f;
    public float defaultHeight = 2f;
    public float crouchHeight = 1f;
    public float crouchSpeed = 3f;
    public float crouchLerpSpeed = 12f;

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

    // Basiswerte aus dem Inspector/Prefab, damit das Ducken sie nicht überschreibt.
    private float baseWalkSpeed;
    private float baseRunSpeed;
    private float baseHeight;
    private Vector3 baseCenter;
    private Vector3 cameraBaseLocalPosition;
    private float currentHeight;

    private bool wasGrounded = true;
    private SpectatorCamera spectator;

    private readonly NetworkVariable<bool> netIsSpectating = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<FixedString32Bytes> netName = new NetworkVariable<FixedString32Bytes>(
        "", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool IsAlive => !netIsSpectating.Value;
    public bool IsSpectating => netIsSpectating.Value;
    public SpectatorCamera Spectator => spectator;

    public string DisplayName
    {
        get
        {
            string n = netName.Value.ToString();
            return string.IsNullOrWhiteSpace(n) ? $"Spieler {OwnerClientId}" : n;
        }
    }

    void Awake()
    {
        characterController = GetComponent<CharacterController>();

        baseWalkSpeed = walkSpeed;
        baseRunSpeed = runSpeed;
        baseHeight = characterController.height;
        baseCenter = characterController.center;
        currentHeight = baseHeight;
        defaultHeight = baseHeight;

        if (playerCamera != null) cameraBaseLocalPosition = playerCamera.transform.localPosition;
    }

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        netIsSpectating.OnValueChanged += OnSpectatingChanged;

        if (IsOwner)
        {
            LocalPlayer = this;
            keyboard = Keyboard.current;
            mouse = Mouse.current;

            netName.Value = string.IsNullOrWhiteSpace(GameSettings.PlayerName)
                ? $"Spieler {OwnerClientId + 1}"
                : GameSettings.PlayerName;

            spectator = gameObject.AddComponent<SpectatorCamera>();
            PauseMenu.ApplyCursorState();
        }
        else if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(false);
        }
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        netIsSpectating.OnValueChanged -= OnSpectatingChanged;

        // Die Zuschauerkamera hängt die Kamera vom Spieler ab - vor dem Zerstören zurückhängen,
        // sonst bleibt ein verwaistes Kameraobjekt in der Szene stehen.
        if (spectator != null) spectator.End();

        if (IsOwner && LocalPlayer == this) LocalPlayer = null;
    }

    private void OnSpectatingChanged(bool previous, bool current)
    {
        if (!IsOwner || spectator == null) return;

        if (current) spectator.Begin(playerCamera);
        else spectator.End();

        moveDirection = Vector3.zero;
        hasReportedFall = false;
    }

    void Update()
    {
        if (!IsOwner) return;

        if (keyboard == null) keyboard = Keyboard.current;
        if (mouse == null) mouse = Mouse.current;
        if (keyboard == null || mouse == null) return;

        // Im Zuschauermodus übernimmt SpectatorCamera; der Körper wird vom Server geparkt.
        if (netIsSpectating.Value)
        {
            moveDirection = Vector3.zero;
            return;
        }

        bool inputBlocked = PauseMenu.IsOpen;

        Vector3 forward = transform.TransformDirection(Vector3.forward);
        Vector3 right = transform.TransformDirection(Vector3.right);

        bool isRunning = !inputBlocked && keyboard.leftShiftKey.isPressed;
        float moveY = inputBlocked ? 0f : (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
        float moveX = inputBlocked ? 0f : (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
        bool jumpPressed = !inputBlocked && keyboard.spaceKey.wasPressedThisFrame;
        bool crouchPressed = !inputBlocked && keyboard.rKey.isPressed;

        Vector2 mouseDelta = inputBlocked ? Vector2.zero : mouse.delta.ReadValue();

        // Ducken senkt die Geschwindigkeit, ohne die Inspector-Werte zu zerstören.
        float targetWalk = crouchPressed ? crouchSpeed : baseWalkSpeed;
        float targetRun = crouchPressed ? crouchSpeed : baseRunSpeed;
        walkSpeed = targetWalk;
        runSpeed = targetRun;

        ApplyCrouch(crouchPressed);

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
            GameAudio.Instance?.PlayJump();
        }
        else
        {
            moveDirection.y = movementDirectionY;
        }

        if (!characterController.isGrounded)
        {
            moveDirection.y -= gravity * Time.deltaTime;
        }

        float verticalBeforeMove = moveDirection.y;
        characterController.Move(moveDirection * Time.deltaTime);

        if (!wasGrounded && characterController.isGrounded && verticalBeforeMove < -4f)
            GameAudio.Instance?.PlayLand();
        wasGrounded = characterController.isGrounded;

        if (canMove && !inputBlocked)
        {
            float sensitivity = GameSettings.MouseSensitivity;
            float verticalDelta = GameSettings.InvertY ? mouseDelta.y : -mouseDelta.y;

            rotationX += verticalDelta * sensitivity;
            rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);
            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
            transform.rotation *= Quaternion.Euler(0, mouseDelta.x * sensitivity, 0);
        }

        if (transform.position.y < fallRespawnHeight)
        {
            if (!hasReportedFall)
            {
                hasReportedFall = true;
                GameAudio.Instance?.PlayFall();
                GameFlowManager.Instance?.ReportFellServerRpc();
            }
        }
        else
        {
            hasReportedFall = false;
        }
    }

    // Höhe UND Center anpassen, damit die Füße beim Ducken auf dem Boden bleiben
    // statt halb im Boden zu versinken. Die Kamera wandert um denselben Betrag mit.
    private void ApplyCrouch(bool crouching)
    {
        float targetHeight = crouching ? crouchHeight : baseHeight;
        currentHeight = Mathf.MoveTowards(currentHeight, targetHeight, crouchLerpSpeed * Time.deltaTime);

        float offset = (baseHeight - currentHeight) * 0.5f;
        characterController.height = currentHeight;
        characterController.center = baseCenter - Vector3.up * offset;

        if (playerCamera != null)
            playerCamera.transform.localPosition = cameraBaseLocalPosition - Vector3.up * offset;
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
        wasGrounded = true;

        ClientNetworkTransform cnt = GetComponent<ClientNetworkTransform>();
        if (cnt != null) cnt.Teleport(position, transform.rotation, transform.lossyScale);
    }

    // Server-seitig: markiert den Spieler als ausgeschieden (-> Zuschauermodus) bzw. wieder als lebend.
    public void SetSpectatingServerSide(bool spectating)
    {
        if (!IsServer) return;
        netIsSpectating.Value = spectating;
    }
}
