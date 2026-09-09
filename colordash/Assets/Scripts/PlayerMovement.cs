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

    [Header("Schubsen")]
    public float pushRange = 2.8f;
    public float pushForce = 15f;
    public float pushUpForce = 3.5f;
    public float pushCooldown = 0.9f;
    [Tooltip("Wie genau man zielen muss: 1 = exakt nach vorn, 0 = auch seitlich.")]
    public float pushAimTolerance = 0.25f;

    [Header("Rundenmodifikatoren")]
    public float slipperyAccelerationFactor = 0.18f;
    public float lowGravityFactor = 0.45f;
    public float lowGravityJumpFactor = 1.2f;

    // Emote-Texte fuer die Tasten 1-4.
    public static readonly string[] Emotes = { "Hi!", "GG!", "Ups!", "Los!" };
    private const float EmoteDuration = 2.5f;

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
    private PlayerNametag nametag;
    private float pushCooldownTimer;
    private float emoteCooldownTimer;
    private double serverPushReadyTime;

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

        // Namensschild fuer JEDEN Spieler auf jedem Client - das eigene blendet sich selbst aus.
        nametag = PlayerNametag.Create(this);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        netIsSpectating.OnValueChanged -= OnSpectatingChanged;

        // Die Zuschauerkamera hängt die Kamera vom Spieler ab - vor dem Zerstören zurückhängen,
        // sonst bleibt ein verwaistes Kameraobjekt in der Szene stehen.
        if (spectator != null) spectator.End();
        if (nametag != null) Destroy(nametag.gameObject);

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

        pushCooldownTimer -= Time.deltaTime;
        emoteCooldownTimer -= Time.deltaTime;

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

        ColorDashManager.RoundModifier modifier = ColorDashManager.Instance != null
            ? ColorDashManager.Instance.CurrentModifier
            : ColorDashManager.RoundModifier.None;

        if (!inputBlocked) HandleInteractionInput();

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
        float accelFactor = modifier == ColorDashManager.RoundModifier.SlipperyFloor ? slipperyAccelerationFactor : 1f;
        float accel = (characterController.isGrounded ? groundAcceleration : airAcceleration) * accelFactor;
        currentHorizontalMove = Vector3.MoveTowards(currentHorizontalMove, targetHorizontalMove, accel * Time.deltaTime);

        moveDirection = currentHorizontalMove;

        if (jumpPressed && canMove && characterController.isGrounded)
        {
            moveDirection.y = modifier == ColorDashManager.RoundModifier.LowGravity
                ? jumpPower * lowGravityJumpFactor
                : jumpPower;
            GameAudio.Instance?.PlayJump();
        }
        else
        {
            moveDirection.y = movementDirectionY;
        }

        if (!characterController.isGrounded)
        {
            float gravityFactor = modifier == ColorDashManager.RoundModifier.LowGravity ? lowGravityFactor : 1f;
            moveDirection.y -= gravity * gravityFactor * Time.deltaTime;
        }

        float verticalBeforeMove = moveDirection.y;
        characterController.Move(moveDirection * Time.deltaTime);

        if (!wasGrounded && characterController.isGrounded && verticalBeforeMove < -4f)
            GameAudio.Instance?.PlayLand();
        wasGrounded = characterController.isGrounded;

        if (canMove && !inputBlocked)
        {
            float sensitivity = GameSettings.MouseSensitivity;
            // Der Modifikator dreht beide Achsen um - deutlich fieser als nur Y.
            float invert = modifier == ColorDashManager.RoundModifier.InvertedCamera ? -1f : 1f;
            float verticalDelta = (GameSettings.InvertY ? mouseDelta.y : -mouseDelta.y) * invert;

            rotationX += verticalDelta * sensitivity;
            rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);
            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
            transform.rotation *= Quaternion.Euler(0, mouseDelta.x * sensitivity * invert, 0);
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

    // Schubsen (F) und Emotes (1-4) - beide laufen ueber den Server, damit niemand
    // die Position anderer Spieler direkt manipulieren kann.
    private void HandleInteractionInput()
    {
        if (keyboard.fKey.wasPressedThisFrame && pushCooldownTimer <= 0f)
        {
            pushCooldownTimer = pushCooldown;
            RequestPushServerRpc();
        }

        if (emoteCooldownTimer > 0f) return;

        for (int i = 0; i < Emotes.Length; i++)
        {
            if (!WasEmoteKeyPressed(i)) continue;

            emoteCooldownTimer = 1f;
            GameAudio.Instance?.PlayUi();
            RequestEmoteServerRpc(i);
            break;
        }
    }

    private bool WasEmoteKeyPressed(int index)
    {
        switch (index)
        {
            case 0: return keyboard.digit1Key.wasPressedThisFrame;
            case 1: return keyboard.digit2Key.wasPressedThisFrame;
            case 2: return keyboard.digit3Key.wasPressedThisFrame;
            case 3: return keyboard.digit4Key.wasPressedThisFrame;
            default: return false;
        }
    }

    [ServerRpc]
    private void RequestPushServerRpc()
    {
        // Cooldown auch serverseitig, sonst laesst er sich clientseitig einfach aushebeln.
        double now = NetworkManager.ServerTime.Time;
        if (now < serverPushReadyTime) return;
        serverPushReadyTime = now + pushCooldown;

        if (!IsAlive) return;

        Vector3 origin = transform.position;
        Vector3 facing = transform.forward;

        for (int i = 0; i < All.Count; i++)
        {
            PlayerMovement other = All[i];
            if (other == null || other == this || !other.IsAlive) continue;

            Vector3 delta = other.transform.position - origin;
            delta.y = 0f;

            float distance = delta.magnitude;
            if (distance > pushRange || distance < 0.01f) continue;

            Vector3 direction = delta / distance;
            if (Vector3.Dot(facing, direction) < pushAimTolerance) continue;

            other.ApplyKnockbackClientRpc(direction * pushForce + Vector3.up * pushUpForce);
        }

        PushPerformedClientRpc();
    }

    [ClientRpc]
    private void ApplyKnockbackClientRpc(Vector3 impulse)
    {
        // Bewegung ist client-autoritativ - nur der Besitzer darf seinen Koerper bewegen.
        if (!IsOwner) return;

        moveDirection.x += impulse.x;
        moveDirection.z += impulse.z;
        moveDirection.y = Mathf.Max(moveDirection.y, impulse.y);

        GameAudio.Instance?.PlayLand();
    }

    [ClientRpc]
    private void PushPerformedClientRpc()
    {
        GameAudio.Instance?.PlayWhoosh();
    }

    [ServerRpc]
    private void RequestEmoteServerRpc(int index)
    {
        if (index < 0 || index >= Emotes.Length) return;
        ShowEmoteClientRpc(index);
    }

    [ClientRpc]
    private void ShowEmoteClientRpc(int index)
    {
        if (nametag == null || index < 0 || index >= Emotes.Length) return;
        nametag.ShowEmote(Emotes[index], EmoteDuration);
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
