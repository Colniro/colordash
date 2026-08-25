using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// Plain (non-networked) trigger volume placed on the lobby platform.
// Alle Spieler laufen hinein und drücken E; jeder Client meldet nur seinen eigenen Spieler.
// E schaltet den Bereit-Status um, solange keine Runde läuft.
public class ReadyButtonZone : MonoBehaviour
{
    private bool playerInside = false;
    private TextMeshProUGUI prompt;

    void Awake()
    {
        Canvas canvas = UIFactory.CreateCanvas("ReadyPromptCanvas", 80);
        canvas.transform.SetParent(transform, false);

        prompt = UIFactory.CreateText(canvas.transform, "ReadyPrompt", "", 26f,
            FontStyles.Bold, Color.white, TextAlignmentOptions.Center,
            new Vector2(0.5f, 0f), new Vector2(0, 110), new Vector2(700, 40));
        UIFactory.SetOutline(prompt, 0.2f, Color.black);
    }

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
        GameFlowManager flow = GameFlowManager.Instance;
        bool roundRunning = flow != null && flow.RoundInProgress;
        bool canReady = playerInside && !roundRunning && !PauseMenu.IsOpen;

        if (prompt != null)
            prompt.text = canReady ? "[E] Bereit / Nicht bereit" : "";

        if (!canReady) return;

        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            GameAudio.Instance?.PlayReady(true);
            flow?.ToggleReadyServerRpc();
        }
    }
}
