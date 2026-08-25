using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Main menu (Singleplayer / Host via Relay / Join via code) + server-authoritative lobby/round flow:
// connect -> spawn in lobby -> ready up -> teleport to field & start ColorDash ->
// wer fällt, wird Zuschauer -> der letzte Überlebende gewinnt -> zurück in die Lobby.
public class GameFlowManager : NetworkBehaviour
{
    public static GameFlowManager Instance { get; private set; }

    [Header("Spawn Points")]
    public Transform[] lobbySpawnPoints;
    public Transform[] fieldSpawnPoints;

    [Header("Spieleranzahl")]
    [Tooltip("Obergrenze für eine Lobby. Relay wird passend dazu allokiert.")]
    public int maxPlayers = 8;
    [Tooltip("So viele Spieler müssen im Mehrspieler mindestens da sein, damit eine Runde startet.")]
    public int minPlayersToStart = 2;
    [Tooltip("Radius, in dem zusätzliche Spieler verteilt werden, wenn zu wenige Spawnpunkte existieren.")]
    public float extraSpawnRadius = 3f;

    [Header("References")]
    public ColorDashManager colorDashManager;
    public UnityTransport transport;

    private const int RelayTimeoutSeconds = 15;

    private readonly NetworkList<ulong> readyClientIds = new NetworkList<ulong>();
    private readonly NetworkList<ulong> fallenClientIds = new NetworkList<ulong>();
    private readonly NetworkVariable<bool> netRoundInProgress = new NetworkVariable<bool>(false);

    private readonly Dictionary<ulong, int> clientSlots = new Dictionary<ulong, int>();
    private bool singleplayerMode = false;
    private static bool isLeaving = false;

    private int RequiredPlayers => singleplayerMode ? 1 : Mathf.Clamp(minPlayersToStart, 1, Mathf.Max(1, maxPlayers));

    public bool RoundInProgress => netRoundInProgress.Value;

    // UI
    private GameObject menuCanvasGO;
    private GameObject hudGO;
    private TextMeshProUGUI hudText;
    private TextMeshProUGUI statusText;
    private TMP_InputField codeInputField;
    private TMP_InputField nameInputField;
    private Button singleplayerButton;
    private Button hostButton;
    private Button joinButton;

    private bool servicesReady = false;
    private bool isBusy = false;
    private string hostJoinCode = "";
    private readonly StringBuilder hudBuilder = new StringBuilder();

    async void Awake()
    {
        Instance = this;
        isLeaving = false;

        GameAudio.Ensure();
        EnsureEventSystem();
        BuildMenuUI();
        PauseMenu.Ensure();
        PauseMenu.ApplyCursorState();
        SetStatus("Verbinde mit Unity Services...");

        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            servicesReady = true;
            SetStatus("");
            SetMultiplayerButtonsInteractable(true);
        }
        catch (Exception e)
        {
            Debug.LogError("[ColorDash] Unity Services Initialisierung fehlgeschlagen: " + e);
            SetStatus("Unity Services nicht verfügbar (Mehrspieler geht nicht) - Singleplayer geht trotzdem.");
        }
    }

    void Start()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback += OnAnyClientDisconnected;
    }

    public override void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnAnyClientDisconnected;

        if (Instance == this) Instance = null;

        base.OnDestroy();
    }

    void Update()
    {
        if (NetworkManager.Singleton == null) return;

        bool connected = NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer;
        if (hudGO != null && hudGO.activeSelf != connected) hudGO.SetActive(connected);

        if (connected && hudText != null && IsSpawned) hudText.text = BuildHudText();
    }

    private string BuildHudText()
    {
        hudBuilder.Clear();

        if (!string.IsNullOrEmpty(hostJoinCode))
            hudBuilder.Append("Dein Code: ").Append(hostJoinCode).Append('\n');

        int connectedCount = PlayerMovement.All.Count;
        int readyCount = readyClientIds.Count;

        if (netRoundInProgress.Value)
        {
            int alive = 0;
            for (int i = 0; i < PlayerMovement.All.Count; i++)
                if (PlayerMovement.All[i] != null && PlayerMovement.All[i].IsAlive) alive++;

            hudBuilder.Append("Runde läuft - noch im Spiel: ").Append(alive).Append('/').Append(connectedCount);
        }
        else
        {
            hudBuilder.Append("Bereit: ").Append(readyCount).Append('/').Append(connectedCount)
                      .Append("  |  Mindestens ").Append(RequiredPlayers).Append(" Spieler");
        }

        for (int i = 0; i < PlayerMovement.All.Count; i++)
        {
            PlayerMovement pm = PlayerMovement.All[i];
            if (pm == null) continue;

            hudBuilder.Append('\n').Append("  ").Append(pm.DisplayName);

            if (netRoundInProgress.Value)
                hudBuilder.Append(pm.IsAlive ? "  - im Spiel" : "  - raus");
            else if (readyClientIds.Contains(pm.OwnerClientId))
                hudBuilder.Append("  - BEREIT");
        }

        return hudBuilder.ToString();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnServerSawClientDisconnect;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnServerSawClientDisconnect;
        }
    }

    public void RegisterConnectionApproval()
    {
        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        int slot = AcquireSlot(request.ClientNetworkId);
        if (slot < 0)
        {
            response.Approved = false;
            response.Reason = "Lobby ist voll.";
            return;
        }

        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Position = GetSpawnPosition(lobbySpawnPoints, slot);
        response.Rotation = Quaternion.identity;
    }

    // Freien Slot suchen statt hochzuzählen - sonst gilt die Lobby nach ein paar
    // Join/Leave-Zyklen als voll, obwohl niemand mehr verbunden ist.
    private int AcquireSlot(ulong clientId)
    {
        if (clientSlots.TryGetValue(clientId, out int existing)) return existing;

        HashSet<int> used = new HashSet<int>(clientSlots.Values);
        for (int i = 0; i < Mathf.Max(1, maxPlayers); i++)
        {
            if (used.Contains(i)) continue;
            clientSlots[clientId] = i;
            return i;
        }
        return -1;
    }

    // Mehr Spieler als Spawnpunkte: die überzähligen im Kreis um den ersten Punkt verteilen.
    private Vector3 GetSpawnPosition(Transform[] spawnPoints, int slot)
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return Vector3.zero;
        if (slot < spawnPoints.Length) return spawnPoints[slot].position;

        int extra = slot - spawnPoints.Length;
        int extraCount = Mathf.Max(1, Mathf.Max(1, maxPlayers) - spawnPoints.Length);
        float angle = extra * Mathf.PI * 2f / extraCount;

        return spawnPoints[0].position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * extraSpawnRadius;
    }

    // Läuft auf jedem Peer: der lokale Client verliert die Verbindung -> zurück ins Hauptmenü.
    private void OnAnyClientDisconnected(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.IsServer) return;
        if (clientId != nm.LocalClientId) return;

        Debug.Log("[ColorDash] Verbindung zum Host verloren.");
        LeaveGame();
    }

    // Nur auf dem Server: Aufräumen und prüfen, ob die Runde dadurch entschieden ist.
    // Der abgemeldete Client wird explizit ignoriert, weil er je nach Timing noch
    // in ConnectedClientsIds stehen kann - sonst wartet die Runde ewig auf einen Geist.
    private void OnServerSawClientDisconnect(ulong clientId)
    {
        clientSlots.Remove(clientId);
        if (readyClientIds.Contains(clientId)) readyClientIds.Remove(clientId);
        if (fallenClientIds.Contains(clientId)) fallenClientIds.Remove(clientId);

        if (netRoundInProgress.Value) EvaluateRoundEnd(true, clientId);
        else TryStartRound(true, clientId);
    }

    [ServerRpc(RequireOwnership = false)]
    public void ToggleReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        // Während einer laufenden Runde darf niemand die nächste starten.
        if (netRoundInProgress.Value) return;

        ulong senderId = rpcParams.Receive.SenderClientId;
        if (readyClientIds.Contains(senderId)) readyClientIds.Remove(senderId);
        else readyClientIds.Add(senderId);

        TryStartRound();
    }

    // Startet, sobald genug Spieler da sind UND alle Verbundenen bereit sind.
    private void TryStartRound(bool hasIgnored = false, ulong ignoredClientId = 0)
    {
        if (netRoundInProgress.Value) return;

        int connected = 0;
        int ready = 0;
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (hasIgnored && clientId == ignoredClientId) continue;
            connected++;
            if (readyClientIds.Contains(clientId)) ready++;
        }

        if (connected >= RequiredPlayers && ready >= connected) StartRound();
    }

    [ServerRpc(RequireOwnership = false)]
    public void ReportFellServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        // Außerhalb einer Runde (z.B. von der Lobbyplattform gefallen) einfach zurücksetzen.
        if (!netRoundInProgress.Value)
        {
            TeleportClientToSpawn(senderId, lobbySpawnPoints);
            return;
        }

        if (fallenClientIds.Contains(senderId)) return;
        fallenClientIds.Add(senderId);

        TeleportClientToSpawn(senderId, lobbySpawnPoints);
        SetSpectating(senderId, true);

        EvaluateRoundEnd();
    }

    private void StartRound()
    {
        fallenClientIds.Clear();
        netRoundInProgress.Value = true;

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            SetSpectating(clientId, false);
            TeleportClientToSpawn(clientId, fieldSpawnPoints);
        }

        if (colorDashManager != null) colorDashManager.SetGameActive(true);
    }

    // Prüft, ob nur noch einer (bzw. im Singleplayer keiner) übrig ist.
    private void EvaluateRoundEnd(bool hasIgnored = false, ulong ignoredClientId = 0)
    {
        if (!netRoundInProgress.Value) return;

        int participants = 0;
        List<ulong> alive = new List<ulong>();
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (hasIgnored && clientId == ignoredClientId) continue;
            participants++;
            if (!fallenClientIds.Contains(clientId)) alive.Add(clientId);
        }

        bool solo = participants <= 1;

        if (solo)
        {
            if (alive.Count == 0) EndRound(false, 0, true);
            return;
        }

        if (alive.Count == 1) EndRound(true, alive[0], false);
        else if (alive.Count == 0) EndRound(false, 0, false);
    }

    private void EndRound(bool hasWinner, ulong winnerId, bool solo)
    {
        int roundsSurvived = 0;
        if (colorDashManager != null) roundsSurvived = colorDashManager.CurrentRound;

        netRoundInProgress.Value = false;
        if (colorDashManager != null) colorDashManager.SetGameActive(false);

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            SetSpectating(clientId, false);
            TeleportClientToSpawn(clientId, lobbySpawnPoints);
        }

        readyClientIds.Clear();
        fallenClientIds.Clear();

        FixedString64Bytes winnerName = hasWinner ? NameOf(winnerId) : "";

        AnnounceRoundEndClientRpc(winnerName, winnerId, hasWinner, solo, roundsSurvived);
    }

    private string NameOf(ulong clientId)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
            && client.PlayerObject != null)
        {
            PlayerMovement pm = client.PlayerObject.GetComponent<PlayerMovement>();
            if (pm != null) return pm.DisplayName;
        }
        return $"Spieler {clientId + 1}";
    }

    private void SetSpectating(ulong clientId, bool spectating)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return;
        if (client.PlayerObject == null) return;

        PlayerMovement pm = client.PlayerObject.GetComponent<PlayerMovement>();
        if (pm != null) pm.SetSpectatingServerSide(spectating);
    }

    [ClientRpc]
    private void AnnounceRoundEndClientRpc(FixedString64Bytes winnerName, ulong winnerId, bool hasWinner, bool solo, int rounds)
    {
        if (colorDashManager == null) return;

        bool isLocalWinner = hasWinner && NetworkManager.Singleton != null
                             && winnerId == NetworkManager.Singleton.LocalClientId;

        string message;
        Color color;

        if (solo)
        {
            message = $"Geschafft bis Runde {rounds}";
            color = UIFactory.Accent;
        }
        else if (!hasWinner)
        {
            message = $"Niemand hat überlebt - Runde {rounds}";
            color = UIFactory.Muted;
        }
        else if (isLocalWinner)
        {
            message = $"Du hast gewonnen! (Runde {rounds})";
            color = UIFactory.Accent;
        }
        else
        {
            message = $"{winnerName} gewinnt! (Runde {rounds})";
            color = Color.white;
        }

        colorDashManager.ShowBanner(message, color, 5f);

        if (isLocalWinner) GameAudio.Instance?.PlayWin();
        else if (!solo) GameAudio.Instance?.PlayEliminated();
    }

    private void TeleportClientToSpawn(ulong clientId, Transform[] spawnPoints)
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return;
        if (client.PlayerObject == null) return;

        PlayerMovement pm = client.PlayerObject.GetComponent<PlayerMovement>();
        if (pm == null) return;

        int slot = clientSlots.TryGetValue(clientId, out int s) ? s : 0;
        pm.TeleportClientRpc(GetSpawnPosition(spawnPoints, slot));
    }

    // --- Menu actions ---

    // Sauberer Ausstieg: Netzwerk herunterfahren und die Szene neu laden, damit
    // Lobby-, Runden- und Spielerzustand garantiert zurückgesetzt sind.
    public static void LeaveGame()
    {
        if (isLeaving) return;
        isLeaving = true;

        if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void StartSingleplayer()
    {
        if (isBusy) return;
        GameAudio.Instance?.PlayUi();
        singleplayerMode = true;
        HideMenu();
        RegisterConnectionApproval();
        NetworkManager.Singleton.StartHost();
    }

    private async void CreateLobby()
    {
        if (isBusy || !servicesReady) return;
        isBusy = true;
        GameAudio.Instance?.PlayUi();
        SetStatus("Erstelle Lobby...");
        Debug.Log("[ColorDash] Erstelle Relay-Allocation...");

        try
        {
            int relaySlots = Mathf.Max(1, maxPlayers - 1);
            Allocation allocation = await WithTimeout(
                RelayService.Instance.CreateAllocationAsync(relaySlots),
                "Zeitüberschreitung beim Erstellen der Relay-Allocation. Ist der Relay-Dienst im Unity Cloud Dashboard für dieses Projekt aktiviert?");
            Debug.Log($"[ColorDash] Allocation erstellt: {allocation.AllocationId}");

            hostJoinCode = await WithTimeout(
                RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId),
                "Zeitüberschreitung beim Abrufen des Join-Codes.");
            Debug.Log($"[ColorDash] Join-Code: {hostJoinCode}");

            transport.SetRelayServerData(new RelayServerData(allocation, "dtls"));

            singleplayerMode = false;
            RegisterConnectionApproval();
            NetworkManager.Singleton.StartHost();
            HideMenu();
        }
        catch (Exception e)
        {
            Debug.LogError("[ColorDash] Lobby erstellen fehlgeschlagen: " + e);
            SetStatus("Fehler beim Erstellen: " + e.Message);
        }

        isBusy = false;
    }

    private async void JoinLobby(string code)
    {
        if (isBusy || !servicesReady) return;
        if (string.IsNullOrWhiteSpace(code))
        {
            SetStatus("Bitte einen Code eingeben.");
            return;
        }

        isBusy = true;
        GameAudio.Instance?.PlayUi();
        SetStatus("Verbinde...");
        Debug.Log($"[ColorDash] Trete Relay-Session mit Code '{code}' bei...");

        try
        {
            JoinAllocation joinAllocation = await WithTimeout(
                RelayService.Instance.JoinAllocationAsync(code),
                "Zeitüberschreitung beim Beitreten. Code korrekt? Existiert die Lobby noch?");
            Debug.Log("[ColorDash] Join-Allocation erhalten.");

            transport.SetRelayServerData(new RelayServerData(joinAllocation, "dtls"));

            singleplayerMode = false;
            NetworkManager.Singleton.StartClient();
            HideMenu();
        }
        catch (Exception e)
        {
            Debug.LogError("[ColorDash] Beitreten fehlgeschlagen: " + e);
            SetStatus("Fehler beim Beitreten: " + e.Message);
        }

        isBusy = false;
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, string timeoutMessage)
    {
        Task delay = Task.Delay(TimeSpan.FromSeconds(RelayTimeoutSeconds));
        Task finished = await Task.WhenAny(task, delay);
        if (finished == delay)
        {
            throw new TimeoutException(timeoutMessage);
        }
        return await task;
    }

    // --- UI construction ---

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    private void SetMultiplayerButtonsInteractable(bool interactable)
    {
        if (hostButton != null) hostButton.interactable = interactable;
        if (joinButton != null) joinButton.interactable = interactable;
    }

    private void HideMenu()
    {
        if (menuCanvasGO != null) menuCanvasGO.SetActive(false);
        PauseMenu.ApplyCursorState();
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;

        GameObject esGO = new GameObject("EventSystem");
        esGO.AddComponent<EventSystem>();
        esGO.AddComponent<InputSystemUIInputModule>();
    }

    private void BuildMenuUI()
    {
        Canvas menuCanvas = UIFactory.CreateCanvas("MainMenuCanvas", 100);
        menuCanvasGO = menuCanvas.gameObject;

        UIFactory.CreateFullscreenDim(menuCanvasGO.transform, 0.55f);

        Image panel = UIFactory.CreateImage(menuCanvasGO.transform, "MenuPanel", UIFactory.PanelColor,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 560));
        Transform panelT = panel.transform;

        UIFactory.CreateText(panelT, "Title", "ColorDash", 52f, FontStyles.Bold, UIFactory.Accent,
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(500, 70));

        UIFactory.CreateText(panelT, "NameLabel", "Dein Name", 18f, FontStyles.Normal, UIFactory.Muted,
            TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(100, -118), new Vector2(200, 26));

        nameInputField = UIFactory.CreateInputField(panelT, "Name eingeben", new Vector2(0, 165), new Vector2(360, 48), 16);
        nameInputField.text = GameSettings.PlayerName;
        nameInputField.onValueChanged.AddListener(v => GameSettings.PlayerName = v);

        singleplayerButton = UIFactory.CreateButton(panelT, "Einzelspieler", new Color(0.20f, 0.55f, 0.25f),
            new Vector2(0, 95), new Vector2(360, 60));
        singleplayerButton.onClick.AddListener(StartSingleplayer);

        UIFactory.CreateText(panelT, "Divider", "— oder Mehrspieler —", 20f, FontStyles.Italic, UIFactory.Muted,
            TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0, 35), new Vector2(400, 30));

        hostButton = UIFactory.CreateButton(panelT, "Lobby erstellen", new Color(0.18f, 0.35f, 0.65f),
            new Vector2(0, -25), new Vector2(360, 60));
        hostButton.onClick.AddListener(CreateLobby);
        hostButton.interactable = false;

        codeInputField = UIFactory.CreateInputField(panelT, "CODE", new Vector2(-70, -100), new Vector2(220, 55));

        joinButton = UIFactory.CreateButton(panelT, "Beitreten", new Color(0.65f, 0.42f, 0.12f),
            new Vector2(115, -100), new Vector2(130, 55));
        joinButton.onClick.AddListener(() => JoinLobby(codeInputField.text.Trim().ToUpperInvariant()));
        joinButton.interactable = false;

        statusText = UIFactory.CreateText(panelT, "Status", "", 19f, FontStyles.Normal, Color.white,
            TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0, 30), new Vector2(500, 60));
        statusText.textWrappingMode = TextWrappingModes.Normal;

        // Kleines Dauer-HUD (sichtbar sobald verbunden) in eigenem Canvas,
        // damit das Ausblenden des Hauptmenüs es nicht mit versteckt.
        Canvas hudCanvas = UIFactory.CreateCanvas("HudCanvas", 90);

        hudText = UIFactory.CreateText(hudCanvas.transform, "HudText", "", 20f, FontStyles.Normal, Color.white,
            TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(20, -20), new Vector2(460, 260));
        UIFactory.SetOutline(hudText, 0.18f, Color.black);
        hudGO = hudText.gameObject;
        hudGO.SetActive(false);
    }
}
