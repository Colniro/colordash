using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Wird zur Laufzeit auf dem lokalen Spieler ergänzt. Nach dem Rausfliegen löst sie die
// Kamera vom Körper und kreist um einen noch lebenden Mitspieler, statt den Spieler
// bis zum Rundenende in der Lobby warten zu lassen.
[DisallowMultipleComponent]
public class SpectatorCamera : MonoBehaviour
{
    [Header("Kamera")]
    public float distance = 6.5f;
    public float height = 2.2f;
    public float focusHeight = 1.2f;
    public float followSharpness = 6f;
    public float orbitSpeed = 0.14f;
    public float minPitch = -10f;
    public float maxPitch = 60f;

    private Camera cam;
    private Transform camTransform;
    private Transform originalParent;
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;

    private readonly List<PlayerMovement> candidates = new List<PlayerMovement>();
    private PlayerMovement self;
    private PlayerMovement target;
    private float yaw;
    private float pitch = 14f;
    private bool active;

    public bool IsActive => active;

    public string TargetName => target != null ? target.DisplayName : "";

    void Awake()
    {
        self = GetComponent<PlayerMovement>();
    }

    public void Begin(Camera camera)
    {
        if (active || camera == null) return;

        cam = camera;
        camTransform = camera.transform;
        originalParent = camTransform.parent;
        originalLocalPosition = camTransform.localPosition;
        originalLocalRotation = camTransform.localRotation;

        camTransform.SetParent(null, true);
        yaw = transform.eulerAngles.y;
        active = true;

        PickTarget(0);
    }

    public void End()
    {
        if (!active) return;

        active = false;
        target = null;

        if (camTransform != null)
        {
            camTransform.SetParent(originalParent, false);
            camTransform.localPosition = originalLocalPosition;
            camTransform.localRotation = originalLocalRotation;
        }
    }

    void LateUpdate()
    {
        if (!active || camTransform == null) return;

        HandleInput();

        if (target == null || !target.IsAlive) PickTarget(0);

        Vector3 focus = target != null
            ? target.transform.position + Vector3.up * focusHeight
            : transform.position + Vector3.up * focusHeight;

        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desired = focus - orbit * Vector3.forward * distance + Vector3.up * height;

        float lerp = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        camTransform.position = Vector3.Lerp(camTransform.position, desired, lerp);

        Vector3 lookDir = focus - camTransform.position;
        if (lookDir.sqrMagnitude > 0.0001f)
            camTransform.rotation = Quaternion.Slerp(camTransform.rotation, Quaternion.LookRotation(lookDir), lerp);
    }

    private void HandleInput()
    {
        if (PauseMenu.IsOpen) return;

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * orbitSpeed * GameSettings.MouseSensitivity;
            pitch += (GameSettings.InvertY ? -delta.y : delta.y) * orbitSpeed * GameSettings.MouseSensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            if (mouse.leftButton.wasPressedThisFrame) PickTarget(1);
            if (mouse.rightButton.wasPressedThisFrame) PickTarget(-1);
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame) PickTarget(1);
            if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame) PickTarget(-1);
        }
    }

    // step 0 = nächstbestes Ziel suchen, +1/-1 = im Kreis durchschalten.
    private void PickTarget(int step)
    {
        candidates.Clear();
        for (int i = 0; i < PlayerMovement.All.Count; i++)
        {
            PlayerMovement pm = PlayerMovement.All[i];
            if (pm == null || pm == self) continue;
            if (!pm.IsAlive) continue;
            candidates.Add(pm);
        }

        if (candidates.Count == 0)
        {
            target = null;
            return;
        }

        int index = target != null ? candidates.IndexOf(target) : -1;
        if (index < 0) index = 0;
        else index = ((index + step) % candidates.Count + candidates.Count) % candidates.Count;

        target = candidates[index];
    }
}
