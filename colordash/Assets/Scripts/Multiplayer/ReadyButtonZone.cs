using UnityEngine;
using UnityEngine.InputSystem;

// Plain (non-networked) trigger volume placed on the lobby platform.
// Both players walk into it and press E; each client only reports for its own local player.
public class ReadyButtonZone : MonoBehaviour
{
    private bool playerInside = false;

    void OnTriggerEnter(Collider other)
    {
        PlayerMovement pm = other.GetComponentInParent<PlayerMovement>();
        if (pm != null && pm.IsOwner) playerInside = true;
    }

    void OnTriggerExit(Collider other)
    {
        PlayerMovement pm = other.GetComponentInParent<PlayerMovement>();
        if (pm != null && pm.IsOwner) playerInside = false;
    }

    void Update()
    {
        if (!playerInside) return;
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            GameFlowManager.Instance?.SetReadyServerRpc();
        }
    }
}
