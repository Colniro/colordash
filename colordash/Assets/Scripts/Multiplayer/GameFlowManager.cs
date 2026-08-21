using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
using UnityEngine.UI;

// Main menu (Singleplayer / Host via Relay / Join via code) + server-authoritative lobby/round flow:
// connect -> spawn in lobby -> ready up -> teleport to field & start ColorDash ->
// each player who falls is sent back to the lobby -> once everyone has fallen, reset and wait for ready again.
public class GameFlowManager : NetworkBehaviour
{
    public static GameFlowManager Instance { get; private set; }

    [Header("Spawn Points")]
    public Transform[] lobbySpawnPoints;
    public Transform[] fieldSpawnPoints;

    [Header("References")]
    public ColorDashManager colorDashManager;
    public UnityTransport transport;

    private const int RelayTimeoutSeconds = 15;

    private readonly NetworkList<ulong> readyClientIds = new NetworkList<ulong>();
    private readonly NetworkList<ulong> fallenClientIds = new NetworkList<ulong>();
    private readonly Dictionary<ulong, int> clientSlots = new Dictionary<ulong, int>();
    private int nextSlot = 0;
    private bool singleplayerMode = false;
    private int RequiredPlayers => singleplayerMode ? 1 : 2;

    // UI
    private GameObject menuCanvasGO;
    private GameObject hudGO;
    private Text hudText;
    private Text statusText;
    private InputField codeInputField;
    private Button singleplayerButton;
    private Button hostButton;
    private Button joinButton;

    private bool servicesReady = false;
    private bool isBusy = false;
    private string hostJoinCode = "";

    async void Awake()
    {
        Instance = this;

        EnsureEventSystem();
        BuildMenuUI();
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

    void Update()
    {
        if (NetworkManager.Singleton == null) return;

        bool connected = NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer;
        if (hudGO != null && hudGO.activeSelf != connected) hudGO.SetActive(connected);

        if (connected && hudText != null)
        {
            string codeLine = !string.IsNullOrEmpty(hostJoinCode) ? $"Dein Code: {hostJoinCode}\n" : "";
            hudText.text = $"{codeLine}Bereit: {readyClientIds.Count}/{RequiredPlayers}  |  Verbunden: {NetworkManager.Singleton.ConnectedClientsIds.Count}/{RequiredPlayers}";
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    public void RegisterConnectionApproval()
    {
        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        if (nextSlot >= lobbySpawnPoints.Length)
        {
            response.Approved = false;
            response.Reason = "Lobby ist voll.";
            return;
        }

        int slot = nextSlot;
        nextSlot++;
        clientSlots[request.ClientNetworkId] = slot;

        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Position = lobbySpawnPoints[slot].position;
        response.Rotation = Quaternion.identity;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        clientSlots.Remove(clientId);
        if (readyClientIds.Contains(clientId)) readyClientIds.Remove(clientId);
        if (fallenClientIds.Contains(clientId)) fallenClientIds.Remove(clientId);

        if (colorDashManager != null) colorDashManager.SetGameActive(false);
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        if (!readyClientIds.Contains(senderId)) readyClientIds.Add(senderId);

        if (readyClientIds.Count >= RequiredPlayers && NetworkManager.Singleton.ConnectedClientsIds.Count >= RequiredPlayers)
        {
            StartRound();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void ReportFellServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        if (!fallenClientIds.Contains(senderId)) fallenClientIds.Add(senderId);

        TeleportClientToSpawn(senderId, lobbySpawnPoints);

        if (fallenClientIds.Count >= NetworkManager.Singleton.ConnectedClientsIds.Count)
        {
            EndRound();
        }
    }

    private void StartRound()
    {
        fallenClientIds.Clear();

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            TeleportClientToSpawn(clientId, fieldSpawnPoints);
        }

        if (colorDashManager != null) colorDashManager.SetGameActive(true);
    }

    private void EndRound()
    {
        readyClientIds.Clear();
        fallenClientIds.Clear();
        if (colorDashManager != null) colorDashManager.SetGameActive(false);
    }

    private void TeleportClientToSpawn(ulong clientId, Transform[] spawnPoints)
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return;
        if (client.PlayerObject == null) return;

        PlayerMovement pm = client.PlayerObject.GetComponent<PlayerMovement>();
        if (pm == null) return;

        int slot = clientSlots.TryGetValue(clientId, out int s) ? s : 0;
        Transform spot = spawnPoints[slot % spawnPoints.Length];
        pm.TeleportClientRpc(spot.position);
    }

    // --- Menu actions ---

    private void StartSingleplayer()
    {
        if (isBusy) return;
        singleplayerMode = true;
        HideMenu();
        RegisterConnectionApproval();
        NetworkManager.Singleton.StartHost();
    }

    private async void CreateLobby()
    {
        if (isBusy || !servicesReady) return;
        isBusy = true;
        SetStatus("Erstelle Lobby...");
        Debug.Log("[ColorDash] Erstelle Relay-Allocation...");

        try
        {
            Allocation allocation = await WithTimeout(
                RelayService.Instance.CreateAllocationAsync(1),
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
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;

        GameObject esGO = new GameObject("EventSystem");
        esGO.AddComponent<EventSystem>();
        esGO.AddComponent<InputSystemUIInputModule>();
    }

    private void BuildMenuUI()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        menuCanvasGO = new GameObject("MainMenuCanvas");
        Canvas canvas = menuCanvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = menuCanvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        menuCanvasGO.AddComponent<GraphicRaycaster>();

        GameObject dim = new GameObject("Dim");
        dim.transform.SetParent(menuCanvasGO.transform, false);
        Image dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.55f);
        RectTransform dimRt = dim.GetComponent<RectTransform>();
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = Vector2.zero;
        dimRt.offsetMax = Vector2.zero;

        GameObject panel = new GameObject("MenuPanel");
        panel.transform.SetParent(menuCanvasGO.transform, false);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.09f, 0.10f, 0.14f, 0.96f);
        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(560, 460);
        panelRt.anchoredPosition = Vector2.zero;

        CreateLabel(panel.transform, font, "ColorDash", 52, FontStyle.Bold, new Color(1f, 0.85f, 0.2f),
            new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(500, 70));

        singleplayerButton = CreateButton(panel.transform, font, "Einzelspieler", new Color(0.20f, 0.55f, 0.25f),
            new Vector2(0, 100), new Vector2(360, 60));
        singleplayerButton.onClick.AddListener(StartSingleplayer);

        CreateLabel(panel.transform, font, "— oder Mehrspieler —", 20, FontStyle.Italic, new Color(0.75f, 0.75f, 0.8f),
            new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(400, 30));

        hostButton = CreateButton(panel.transform, font, "Lobby erstellen", new Color(0.18f, 0.35f, 0.65f),
            new Vector2(0, -20), new Vector2(360, 60));
        hostButton.onClick.AddListener(CreateLobby);
        hostButton.interactable = false;

        codeInputField = CreateInputField(panel.transform, font, new Vector2(-70, -95), new Vector2(220, 55));

        joinButton = CreateButton(panel.transform, font, "Beitreten", new Color(0.65f, 0.42f, 0.12f),
            new Vector2(115, -95), new Vector2(130, 55));
        joinButton.onClick.AddListener(() => JoinLobby(codeInputField.text.Trim().ToUpperInvariant()));
        joinButton.interactable = false;

        statusText = CreateLabel(panel.transform, font, "", 20, FontStyle.Normal, Color.white,
            new Vector2(0.5f, 0f), new Vector2(0, 30), new Vector2(500, 50));

        // Small always-on HUD (shown once connected) for ready/connection status.
        // Lives in its OWN canvas so hiding the main menu doesn't hide this too.
        GameObject hudCanvasGO = new GameObject("HudCanvas");
        Canvas hudCanvas = hudCanvasGO.AddComponent<Canvas>();
        hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hudCanvas.sortingOrder = 90;
        hudCanvasGO.AddComponent<CanvasScaler>();

        hudGO = new GameObject("HudText");
        hudGO.transform.SetParent(hudCanvasGO.transform, false);
        hudText = hudGO.AddComponent<Text>();
        hudText.font = font;
        hudText.fontSize = 22;
        hudText.alignment = TextAnchor.UpperLeft;
        hudText.color = Color.white;
        Outline hudOutline = hudGO.AddComponent<Outline>();
        hudOutline.effectColor = Color.black;
        hudOutline.effectDistance = new Vector2(1.5f, -1.5f);
        RectTransform hudRt = hudGO.GetComponent<RectTransform>();
        hudRt.anchorMin = new Vector2(0f, 1f);
        hudRt.anchorMax = new Vector2(0f, 1f);
        hudRt.pivot = new Vector2(0f, 1f);
        hudRt.anchoredPosition = new Vector2(20, -20);
        hudRt.sizeDelta = new Vector2(500, 70);
        hudGO.SetActive(false);
    }

    private Text CreateLabel(Transform parent, Font font, string text, int fontSize, FontStyle style, Color color,
        Vector2 anchor, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        GameObject go = new GameObject("Label_" + text);
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = fontSize;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;
        return t;
    }

    private Button CreateButton(Transform parent, Font font, string label, Color color, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        GameObject go = new GameObject("Button_" + label);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        Button btn = go.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.highlightedColor = color * 1.15f;
        colors.pressedColor = color * 0.8f;
        colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.6f);
        btn.colors = colors;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(go.transform, false);
        Text t = textGO.AddComponent<Text>();
        t.font = font;
        t.text = label;
        t.fontSize = 24;
        t.fontStyle = FontStyle.Bold;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        RectTransform textRt = textGO.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        return btn;
    }

    private InputField CreateInputField(Transform parent, Font font, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        GameObject go = new GameObject("CodeInput");
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.92f);
        InputField field = go.AddComponent<InputField>();

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(go.transform, false);
        Text text = textGO.AddComponent<Text>();
        text.font = font;
        text.fontSize = 24;
        text.color = Color.black;
        text.alignment = TextAnchor.MiddleCenter;
        text.supportRichText = false;
        RectTransform textRt = textGO.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(10, 2);
        textRt.offsetMax = new Vector2(-10, -2);

        GameObject placeholderGO = new GameObject("Placeholder");
        placeholderGO.transform.SetParent(go.transform, false);
        Text placeholder = placeholderGO.AddComponent<Text>();
        placeholder.font = font;
        placeholder.fontSize = 24;
        placeholder.fontStyle = FontStyle.Italic;
        placeholder.color = new Color(0f, 0f, 0f, 0.4f);
        placeholder.text = "CODE";
        placeholder.alignment = TextAnchor.MiddleCenter;
        RectTransform placeholderRt = placeholderGO.GetComponent<RectTransform>();
        placeholderRt.anchorMin = Vector2.zero;
        placeholderRt.anchorMax = Vector2.one;
        placeholderRt.offsetMin = new Vector2(10, 2);
        placeholderRt.offsetMax = new Vector2(-10, -2);

        field.textComponent = text;
        field.placeholder = placeholder;
        field.characterLimit = 12;

        return field;
    }
}
