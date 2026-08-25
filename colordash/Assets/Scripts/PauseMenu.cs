using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Escape-Menü: Cursor freigeben, Einstellungen ändern, Partie verlassen, Spiel beenden.
// Wird von GameFlowManager zur Laufzeit erzeugt.
public class PauseMenu : MonoBehaviour
{
    public static PauseMenu Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.isOpen;

    private GameObject root;
    private GameObject mainPanel;
    private GameObject settingsPanel;
    private TextMeshProUGUI sensitivityValue;
    private bool isOpen;

    public static PauseMenu Ensure()
    {
        if (Instance != null) return Instance;

        GameObject go = new GameObject("PauseMenu");
        return go.AddComponent<PauseMenu>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Build();
        SetOpen(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;

        // Im Hauptmenü (noch nicht verbunden) ist der Cursor ohnehin frei - dort nichts tun.
        NetworkManager nm = NetworkManager.Singleton;
        bool connected = nm != null && (nm.IsClient || nm.IsServer);
        if (!connected && !isOpen) return;

        if (isOpen && settingsPanel.activeSelf)
        {
            ShowSettings(false);
            return;
        }

        SetOpen(!isOpen);
    }

    public void SetOpen(bool open)
    {
        isOpen = open;
        if (root != null) root.SetActive(open);
        if (open) ShowSettings(false);

        ApplyCursorState();
    }

    // Einzige Stelle, die Cursor-Zustand setzt - sonst kämpfen Pausenmenü und Spieler dagegen.
    public static void ApplyCursorState()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool connected = nm != null && (nm.IsClient || nm.IsServer);
        bool freeCursor = IsOpen || !connected;

        Cursor.lockState = freeCursor ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = freeCursor;
    }

    private void ShowSettings(bool show)
    {
        if (mainPanel != null) mainPanel.SetActive(!show);
        if (settingsPanel != null) settingsPanel.SetActive(show);
    }

    private void Build()
    {
        Canvas canvas = UIFactory.CreateCanvas("PauseCanvas", 200);
        root = canvas.gameObject;
        root.transform.SetParent(transform, false);

        UIFactory.CreateFullscreenDim(root.transform, 0.72f);

        mainPanel = BuildPanel("PausePanel", new Vector2(520, 420));
        settingsPanel = BuildPanel("SettingsPanel", new Vector2(560, 470));

        BuildMainPanel();
        BuildSettingsPanel();
    }

    private GameObject BuildPanel(string name, Vector2 size)
    {
        Image panel = UIFactory.CreateImage(root.transform, name, UIFactory.PanelColor,
            new Vector2(0.5f, 0.5f), Vector2.zero, size);
        return panel.gameObject;
    }

    private void BuildMainPanel()
    {
        UIFactory.CreateText(mainPanel.transform, "Title", "Pause", 44f, FontStyles.Bold, UIFactory.Accent,
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(420, 60));

        Button resume = UIFactory.CreateButton(mainPanel.transform, "Weiter", new Color(0.20f, 0.55f, 0.25f),
            new Vector2(0, 70), new Vector2(340, 56));
        resume.onClick.AddListener(() => { GameAudio.Instance?.PlayUi(); SetOpen(false); });

        Button settings = UIFactory.CreateButton(mainPanel.transform, "Einstellungen", new Color(0.18f, 0.35f, 0.65f),
            new Vector2(0, 0), new Vector2(340, 56));
        settings.onClick.AddListener(() => { GameAudio.Instance?.PlayUi(); ShowSettings(true); });

        Button leave = UIFactory.CreateButton(mainPanel.transform, "Partie verlassen", new Color(0.65f, 0.42f, 0.12f),
            new Vector2(0, -70), new Vector2(340, 56));
        leave.onClick.AddListener(() =>
        {
            GameAudio.Instance?.PlayUi();
            SetOpen(false);
            GameFlowManager.LeaveGame();
        });

        Button quit = UIFactory.CreateButton(mainPanel.transform, "Spiel beenden", new Color(0.55f, 0.18f, 0.18f),
            new Vector2(0, -140), new Vector2(340, 56));
        quit.onClick.AddListener(QuitApplication);
    }

    private void BuildSettingsPanel()
    {
        UIFactory.CreateText(settingsPanel.transform, "Title", "Einstellungen", 36f, FontStyles.Bold, UIFactory.Accent,
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0, -34), new Vector2(460, 50));

        // Maus-Empfindlichkeit
        UIFactory.CreateText(settingsPanel.transform, "SensLabel", "Maus-Empfindlichkeit", 20f, FontStyles.Normal,
            Color.white, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(40, -100), new Vector2(300, 30));

        sensitivityValue = UIFactory.CreateText(settingsPanel.transform, "SensValue",
            GameSettings.MouseSensitivity.ToString("0.00"), 20f, FontStyles.Bold, UIFactory.Accent,
            TextAlignmentOptions.Right, new Vector2(1f, 1f), new Vector2(-40, -100), new Vector2(120, 30));

        Slider sensitivity = UIFactory.CreateSlider(settingsPanel.transform, new Vector2(0, 78), new Vector2(460, 22),
            GameSettings.MinSensitivity, GameSettings.MaxSensitivity, GameSettings.MouseSensitivity);
        sensitivity.onValueChanged.AddListener(v =>
        {
            GameSettings.MouseSensitivity = v;
            if (sensitivityValue != null) sensitivityValue.text = GameSettings.MouseSensitivity.ToString("0.00");
        });

        Toggle invert = UIFactory.CreateToggle(settingsPanel.transform, "Y-Achse invertieren", GameSettings.InvertY,
            new Vector2(-115, 30), new Vector2(320, 30));
        invert.onValueChanged.AddListener(v => GameSettings.InvertY = v);

        // Lautstärke
        UIFactory.CreateText(settingsPanel.transform, "MasterLabel", "Gesamtlautstärke", 20f, FontStyles.Normal,
            Color.white, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(40, -20), new Vector2(300, 30));

        Slider master = UIFactory.CreateSlider(settingsPanel.transform, new Vector2(0, -48), new Vector2(460, 22),
            0f, 1f, GameSettings.MasterVolume);
        master.onValueChanged.AddListener(v => GameSettings.MasterVolume = v);

        UIFactory.CreateText(settingsPanel.transform, "SfxLabel", "Effekte", 20f, FontStyles.Normal,
            Color.white, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(40, -90), new Vector2(300, 30));

        Slider sfx = UIFactory.CreateSlider(settingsPanel.transform, new Vector2(0, -118), new Vector2(460, 22),
            0f, 1f, GameSettings.SfxVolume);
        sfx.onValueChanged.AddListener(v =>
        {
            GameSettings.SfxVolume = v;
            GameAudio.Instance?.PlayUi();
        });

        Button back = UIFactory.CreateButton(settingsPanel.transform, "Zurück", new Color(0.30f, 0.32f, 0.40f),
            new Vector2(0, -186), new Vector2(300, 52));
        back.onClick.AddListener(() => { GameAudio.Instance?.PlayUi(); ShowSettings(false); });
    }

    private static void QuitApplication()
    {
        GameAudio.Instance?.PlayUi();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
