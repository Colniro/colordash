using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class ColorDashManager : NetworkBehaviour
{
    public enum TileColor { Red, Green, Blue, Yellow, Orange, Violet, Black, White }

    [Header("Setup")]
    public Transform tilesParent;

    [Header("Timing")]
    public float startReactionTime = 4f;
    public float minReactionTime = 1f;
    public float reactionTimeStep = 0.2f;
    public float hiddenDuration = 4f;

    [Header("Difficulty Progression")]
    public int multiAnnounceStartRound = 10;
    public int multiAnnounceRoundStep = 10;
    public int maxAnnounceCount = 4;

    [Header("Feedback")]
    [Tooltip("Sekunden vor dem Verschwinden, ab denen die betroffenen Tiles wackeln.")]
    public float wobbleLeadTime = 1.2f;
    [Tooltip("Restzeit, ab der das Ticken hektisch wird.")]
    public float urgentTickTime = 1.5f;
    public float dropCascadeSpread = 0.14f;

    private readonly Dictionary<TileColor, List<GameObject>> tilesByColor = new();
    private readonly List<GameObject> allTiles = new();
    private readonly Dictionary<GameObject, TileAnimator> animators = new();
    private Vector3 arenaCenter;
    private float arenaRadius = 1f;

    private float currentReactionTime;
    private int roundNumber = 0;

    private TextMeshProUGUI announcementText;
    private TextMeshProUGUI sequenceText;
    private TextMeshProUGUI roundText;
    private TextMeshProUGUI bannerText;
    private TextMeshProUGUI spectatorText;
    private Image countdownBar;
    private GameObject countdownRoot;

    private float bannerTimer;

    // Lokaler Renderzustand, damit Übergänge nur einmal ausgelöst werden.
    private bool tilesDropped;
    private TileColor droppedKeepColor;
    private bool tilesWobbling;
    private TileColor wobbleKeepColor;
    private int lastTickSecond = -1;
    private int lastRoundSound = 0;

    // Server-authoritative state, replicated so every client can render the same thing locally.
    private readonly NetworkVariable<bool> isGameActive = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<int> netRoundNumber = new NetworkVariable<int>(0);
    private readonly NetworkVariable<FixedString128Bytes> netSequenceCsv = new NetworkVariable<FixedString128Bytes>("");
    private readonly NetworkVariable<int> netCurrentStepIndex = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<bool> netTilesHidden = new NetworkVariable<bool>(false);

    // Ende der aktuellen Phase auf der synchronisierten Serverzeit - Basis für den Countdown-Balken.
    private readonly NetworkVariable<double> netPhaseEndTime = new NetworkVariable<double>(0d);
    private readonly NetworkVariable<float> netPhaseDuration = new NetworkVariable<float>(0f);

    public bool IsRoundRunning => isGameActive.Value;
    public int CurrentRound => netRoundNumber.Value;

    void Awake()
    {
        if (tilesParent == null)
        {
            GameObject tiles = GameObject.Find("Tiles");
            if (tiles != null) tilesParent = tiles.transform;
        }

        currentReactionTime = startReactionTime;
        GameAudio.Ensure();
        BuildUI();
        CollectTiles();
    }

    public override void OnNetworkSpawn()
    {
        isGameActive.OnValueChanged += (_, __) => ApplyVisuals();
        netRoundNumber.OnValueChanged += (_, __) => ApplyVisuals();
        netSequenceCsv.OnValueChanged += (_, __) => ApplyVisuals();
        netCurrentStepIndex.OnValueChanged += (_, __) => ApplyVisuals();
        netTilesHidden.OnValueChanged += (_, __) => ApplyVisuals();

        ApplyVisuals();

        if (IsServer) StartCoroutine(GameLoop());
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) StopAllCoroutines();
    }

    // Called by GameFlowManager (server-only) to start/stop a round session.
    public void SetGameActive(bool active)
    {
        if (!IsServer) return;

        isGameActive.Value = active;
        if (active)
        {
            roundNumber = 0;
            currentReactionTime = startReactionTime;
        }
        else
        {
            netCurrentStepIndex.Value = -1;
            netTilesHidden.Value = false;
            netPhaseDuration.Value = 0f;
            netPhaseEndTime.Value = 0d;
        }
    }

    void CollectTiles()
    {
        foreach (TileColor c in System.Enum.GetValues(typeof(TileColor)))
            tilesByColor[c] = new List<GameObject>();

        if (tilesParent == null)
        {
            Debug.LogWarning("ColorDashManager: no 'Tiles' parent found in the scene.");
            return;
        }

        Vector3 sum = Vector3.zero;

        foreach (Transform child in tilesParent)
        {
            Renderer rend = child.GetComponent<Renderer>();
            if (rend == null || rend.sharedMaterial == null) continue;

            allTiles.Add(child.gameObject);
            sum += child.position;

            TileAnimator animator = child.GetComponent<TileAnimator>();
            if (animator == null) animator = child.gameObject.AddComponent<TileAnimator>();
            animators[child.gameObject] = animator;

            string matName = rend.sharedMaterial.name;
            if (System.Enum.TryParse(matName, true, out TileColor color))
                tilesByColor[color].Add(child.gameObject);
        }

        if (allTiles.Count > 0)
        {
            arenaCenter = sum / allTiles.Count;
            arenaRadius = 1f;
            foreach (GameObject tile in allTiles)
                arenaRadius = Mathf.Max(arenaRadius, Vector3.Distance(tile.transform.position, arenaCenter));
        }
    }

    void BuildUI()
    {
        Canvas canvas = UIFactory.CreateCanvas("ColorDashCanvas", 10);

        announcementText = UIFactory.CreateText(canvas.transform, "AnnouncementText", "", 64f,
            FontStyles.Bold, Color.white, TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(900, 90));
        UIFactory.SetOutline(announcementText, 0.22f, Color.black);

        countdownBar = UIFactory.CreateFillBar(canvas.transform, "CountdownBar",
            new Color(0f, 0f, 0f, 0.55f), Color.white,
            new Vector2(0.5f, 1f), new Vector2(0, -142), new Vector2(520, 22));
        countdownRoot = countdownBar.transform.parent.gameObject;
        countdownRoot.SetActive(false);

        sequenceText = UIFactory.CreateText(canvas.transform, "SequencePreviewText", "", 30f,
            FontStyles.Normal, Color.white, TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0, -172), new Vector2(1100, 50));
        UIFactory.SetOutline(sequenceText, 0.18f, Color.black);

        roundText = UIFactory.CreateText(canvas.transform, "RoundText", "", 28f,
            FontStyles.Bold, Color.white, TextAlignmentOptions.TopRight,
            new Vector2(1f, 1f), new Vector2(-30, -30), new Vector2(300, 50));
        UIFactory.SetOutline(roundText, 0.18f, Color.black);

        bannerText = UIFactory.CreateText(canvas.transform, "BannerText", "", 56f,
            FontStyles.Bold, UIFactory.Accent, TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1100, 90));
        UIFactory.SetOutline(bannerText, 0.25f, Color.black);

        spectatorText = UIFactory.CreateText(canvas.transform, "SpectatorText", "", 24f,
            FontStyles.Normal, Color.white, TextAlignmentOptions.Bottom,
            new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(900, 40));
        UIFactory.SetOutline(spectatorText, 0.18f, Color.black);
    }

    // Wird vom GameFlowManager für Rundenergebnis / Gewinner genutzt.
    public void ShowBanner(string message, Color color, float duration)
    {
        if (bannerText == null) return;

        bannerText.text = message;
        bannerText.color = color;
        UIFactory.SetOutline(bannerText, 0.25f, GetOutlineColor(color));
        bannerTimer = duration;
    }

    int GetAnnounceCount(int round)
    {
        if (round < multiAnnounceStartRound) return 1;
        int extra = (round - multiAnnounceStartRound) / multiAnnounceRoundStep;
        return Mathf.Clamp(2 + extra, 2, maxAnnounceCount);
    }

    List<TileColor> PickColorSequence(int count, List<TileColor> available)
    {
        List<TileColor> sequence = new List<TileColor>();
        TileColor last = default;
        bool hasLast = false;
        for (int i = 0; i < count; i++)
        {
            TileColor pick;
            int guard = 0;
            do
            {
                pick = available[Random.Range(0, available.Count)];
                guard++;
            }
            while (hasLast && pick == last && available.Count > 1 && guard < 10);

            sequence.Add(pick);
            last = pick;
            hasLast = true;
        }
        return sequence;
    }

    // Server-only: mutates the replicated state. Visuals are applied reactively by ApplyVisuals()
    // on every peer (including the host itself), so there is a single code path for rendering.
    IEnumerator GameLoop()
    {
        while (true)
        {
            if (!isGameActive.Value)
            {
                yield return null;
                continue;
            }

            List<TileColor> available = tilesByColor
                .Where(kv => kv.Value.Count > 0)
                .Select(kv => kv.Key)
                .ToList();

            if (available.Count == 0)
            {
                yield return null;
                continue;
            }

            roundNumber++;
            netRoundNumber.Value = roundNumber;

            int announceCount = GetAnnounceCount(roundNumber);
            List<TileColor> sequence = PickColorSequence(announceCount, available);
            netSequenceCsv.Value = ToCsv(sequence);

            for (int i = 0; i < sequence.Count; i++)
            {
                if (!isGameActive.Value) break;

                netCurrentStepIndex.Value = i;
                netTilesHidden.Value = false;
                SetPhase(currentReactionTime);

                yield return new WaitForSeconds(currentReactionTime);

                if (!isGameActive.Value) break;

                netTilesHidden.Value = true;
                SetPhase(hiddenDuration);

                yield return new WaitForSeconds(hiddenDuration);
            }

            netCurrentStepIndex.Value = -1;
            netTilesHidden.Value = false;
            SetPhase(0f);

            if (isGameActive.Value)
                currentReactionTime = Mathf.Max(minReactionTime, currentReactionTime - reactionTimeStep);
        }
    }

    private void SetPhase(float duration)
    {
        netPhaseDuration.Value = duration;
        netPhaseEndTime.Value = duration > 0f ? NetworkManager.ServerTime.Time + duration : 0d;
    }

    // Runs on every peer whenever the replicated state changes - this is what actually
    // shows/hides tiles and updates the UI text, for the server/host as well as clients.
    void ApplyVisuals()
    {
        if (roundText != null) roundText.text = isGameActive.Value ? $"Runde {netRoundNumber.Value}" : "";

        List<TileColor> seq = ParseSequence(netSequenceCsv.Value.ToString());
        int step = netCurrentStepIndex.Value;
        bool stepValid = isGameActive.Value && seq.Count > 0 && step >= 0 && step < seq.Count;

        if (!stepValid)
        {
            SetWobble(false, default);
            RestoreTiles();
            if (announcementText != null) announcementText.text = "";
            if (sequenceText != null) sequenceText.text = "";
            if (countdownRoot != null) countdownRoot.SetActive(false);
            return;
        }

        TileColor chosen = seq[step];

        if (netTilesHidden.Value)
        {
            SetWobble(false, default);
            DropTiles(chosen);
        }
        else
        {
            RestoreTiles();
        }

        if (announcementText != null)
        {
            Color color = GetDisplayColor(chosen);
            announcementText.text = $"Steh auf: {GetColorName(chosen)}!";
            announcementText.color = color;
            UIFactory.SetOutline(announcementText, 0.24f, GetOutlineColor(color));
        }
        if (sequenceText != null) sequenceText.text = BuildSequencePreview(seq, step);
    }

    void Update()
    {
        UpdateBanner();
        UpdateSpectatorHint();
        UpdateRoundSound();
        UpdateCountdown();
    }

    // Rundenstart-Sound genau einmal pro Runde - bewusst außerhalb von UpdateCountdown,
    // das zwischen den Runden früh aussteigt.
    private void UpdateRoundSound()
    {
        if (netRoundNumber.Value == lastRoundSound) return;

        lastRoundSound = netRoundNumber.Value;
        if (lastRoundSound > 0 && isGameActive.Value) GameAudio.Instance?.PlayRoundStart();
    }

    private void UpdateBanner()
    {
        if (bannerText == null) return;

        if (bannerTimer > 0f)
        {
            bannerTimer -= Time.deltaTime;
            if (bannerTimer <= 0f) bannerText.text = "";
        }
    }

    private void UpdateSpectatorHint()
    {
        if (spectatorText == null) return;

        PlayerMovement local = PlayerMovement.LocalPlayer;
        if (local == null || !local.IsSpectating)
        {
            if (spectatorText.text.Length > 0) spectatorText.text = "";
            return;
        }

        SpectatorCamera spectator = local.Spectator;
        string watching = spectator != null && !string.IsNullOrEmpty(spectator.TargetName)
            ? $"Du siehst {spectator.TargetName} zu"
            : "Niemand mehr im Spiel";

        spectatorText.text = $"Zuschauer-Modus - {watching}   |   [A]/[D] Spieler wechseln";
    }

    private void UpdateCountdown()
    {
        if (countdownRoot == null || countdownBar == null) return;

        bool active = isGameActive.Value && netPhaseDuration.Value > 0f && netCurrentStepIndex.Value >= 0;
        if (!active)
        {
            if (countdownRoot.activeSelf) countdownRoot.SetActive(false);
            lastTickSecond = -1;
            return;
        }

        if (!countdownRoot.activeSelf) countdownRoot.SetActive(true);

        double now = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0d;
        float remaining = Mathf.Max(0f, (float)(netPhaseEndTime.Value - now));
        float fraction = Mathf.Clamp01(remaining / Mathf.Max(0.01f, netPhaseDuration.Value));

        countdownBar.fillAmount = fraction;

        bool reactionPhase = !netTilesHidden.Value;
        if (reactionPhase)
        {
            // Grün -> Rot, je knapper die Zeit.
            countdownBar.color = Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.3f, 0.85f, 0.35f), fraction);
            HandleTicks(remaining);
            UpdateWobble(remaining);
        }
        else
        {
            countdownBar.color = new Color(0.35f, 0.6f, 1f);
            lastTickSecond = -1;
        }
    }

    private void HandleTicks(float remaining)
    {
        bool urgent = remaining <= urgentTickTime;
        float interval = urgent ? 0.5f : 1f;
        int slot = Mathf.FloorToInt(remaining / interval);

        if (slot != lastTickSecond)
        {
            if (lastTickSecond >= 0 && remaining > 0.05f) GameAudio.Instance?.PlayTick(urgent);
            lastTickSecond = slot;
        }
    }

    private void UpdateWobble(float remaining)
    {
        List<TileColor> seq = ParseSequence(netSequenceCsv.Value.ToString());
        int step = netCurrentStepIndex.Value;
        if (seq.Count == 0 || step < 0 || step >= seq.Count) return;

        SetWobble(remaining <= wobbleLeadTime, seq[step]);
    }

    private void SetWobble(bool on, TileColor keep)
    {
        if (on && tilesWobbling && keep == wobbleKeepColor) return;
        if (!on && !tilesWobbling) return;

        foreach (GameObject tile in allTiles)
        {
            if (tile == null || !animators.TryGetValue(tile, out TileAnimator animator)) continue;
            bool safe = on && tilesByColor[keep].Contains(tile);
            animator.SetWobble(on && !safe);
        }

        tilesWobbling = on;
        wobbleKeepColor = keep;
    }

    private void DropTiles(TileColor keep)
    {
        if (tilesDropped && keep == droppedKeepColor) return;

        HashSet<GameObject> safe = new HashSet<GameObject>(tilesByColor[keep]);
        foreach (GameObject tile in allTiles)
        {
            if (tile == null || !animators.TryGetValue(tile, out TileAnimator animator)) continue;

            if (safe.Contains(tile))
            {
                animator.Restore();
                continue;
            }

            // Kaskade von der Mitte nach außen - reine Optik, die Collider gehen sofort aus.
            float distance = Vector3.Distance(tile.transform.position, arenaCenter);
            animator.Drop(distance / arenaRadius * dropCascadeSpread);
        }

        tilesDropped = true;
        droppedKeepColor = keep;
        GameAudio.Instance?.PlayWhoosh();
    }

    private void RestoreTiles()
    {
        if (!tilesDropped) return;

        foreach (GameObject tile in allTiles)
        {
            if (tile == null || !animators.TryGetValue(tile, out TileAnimator animator)) continue;
            animator.Restore();
        }

        tilesDropped = false;
        tilesWobbling = false;
    }

    string BuildSequencePreview(List<TileColor> sequence, int currentIndex)
    {
        if (sequence.Count <= 1) return string.Empty;

        List<string> parts = new List<string>();
        for (int i = 0; i < sequence.Count; i++)
        {
            string name = GetColorName(sequence[i]);
            if (i < currentIndex) parts.Add($"{name} (ok)");
            else if (i == currentIndex) parts.Add($"[{name}]");
            else parts.Add(name);
        }
        return string.Join("  ->  ", parts);
    }

    static string ToCsv(List<TileColor> sequence)
    {
        return string.Join(",", sequence.Select(c => ((int)c).ToString()));
    }

    static List<TileColor> ParseSequence(string csv)
    {
        List<TileColor> result = new List<TileColor>();
        if (string.IsNullOrEmpty(csv)) return result;
        foreach (string part in csv.Split(','))
        {
            if (int.TryParse(part, out int v)) result.Add((TileColor)v);
        }
        return result;
    }

    public static string GetColorName(TileColor c)
    {
        switch (c)
        {
            case TileColor.Red: return "Rot";
            case TileColor.Green: return "Grün";
            case TileColor.Blue: return "Blau";
            case TileColor.Yellow: return "Gelb";
            case TileColor.Orange: return "Orange";
            case TileColor.Violet: return "Violett";
            case TileColor.Black: return "Schwarz";
            case TileColor.White: return "Weiß";
            default: return c.ToString();
        }
    }

    // Schwarz war früher weiß eingefärbt und damit nicht von Weiß zu unterscheiden.
    // Jetzt echte Farbe + kontrastierende Umrandung (siehe GetOutlineColor).
    public static Color GetDisplayColor(TileColor c)
    {
        switch (c)
        {
            case TileColor.Red: return new Color(1f, 0.25f, 0.22f);
            case TileColor.Green: return new Color(0.3f, 0.9f, 0.35f);
            case TileColor.Blue: return new Color(0.35f, 0.55f, 1f);
            case TileColor.Yellow: return new Color(1f, 0.92f, 0.25f);
            case TileColor.Orange: return new Color(1f, 0.55f, 0.1f);
            case TileColor.Violet: return new Color(0.72f, 0.35f, 1f);
            case TileColor.Black: return new Color(0.1f, 0.1f, 0.12f);
            case TileColor.White: return Color.white;
            default: return Color.white;
        }
    }

    private static Color GetOutlineColor(Color c)
    {
        float luminance = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        return luminance < 0.45f ? Color.white : Color.black;
    }
}
