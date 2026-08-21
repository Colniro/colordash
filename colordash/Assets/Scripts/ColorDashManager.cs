using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

    private readonly Dictionary<TileColor, List<GameObject>> tilesByColor = new();
    private readonly List<GameObject> allTiles = new();
    private float currentReactionTime;
    private int roundNumber = 0;
    private Text announcementText;
    private Text sequenceText;
    private Text roundText;

    // Server-authoritative state, replicated so every client can render the same thing locally.
    private readonly NetworkVariable<bool> isGameActive = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<int> netRoundNumber = new NetworkVariable<int>(0);
    private readonly NetworkVariable<FixedString128Bytes> netSequenceCsv = new NetworkVariable<FixedString128Bytes>("");
    private readonly NetworkVariable<int> netCurrentStepIndex = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<bool> netTilesHidden = new NetworkVariable<bool>(false);

    void Awake()
    {
        if (tilesParent == null)
        {
            GameObject tiles = GameObject.Find("Tiles");
            if (tiles != null) tilesParent = tiles.transform;
        }

        currentReactionTime = startReactionTime;
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

        foreach (Transform child in tilesParent)
        {
            Renderer rend = child.GetComponent<Renderer>();
            if (rend == null || rend.sharedMaterial == null) continue;

            allTiles.Add(child.gameObject);

            string matName = rend.sharedMaterial.name;
            if (System.Enum.TryParse(matName, true, out TileColor color))
                tilesByColor[color].Add(child.gameObject);
        }
    }

    void BuildUI()
    {
        GameObject canvasGO = new GameObject("ColorDashCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        GameObject textGO = new GameObject("AnnouncementText");
        textGO.transform.SetParent(canvasGO.transform, false);

        announcementText = textGO.AddComponent<Text>();
        announcementText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        announcementText.fontSize = 64;
        announcementText.alignment = TextAnchor.MiddleCenter;
        announcementText.fontStyle = FontStyle.Bold;
        announcementText.color = Color.white;

        Outline outline = textGO.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2, -2);

        RectTransform rt = textGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, -60);
        rt.sizeDelta = new Vector2(900, 120);

        GameObject seqGO = new GameObject("SequencePreviewText");
        seqGO.transform.SetParent(canvasGO.transform, false);
        sequenceText = seqGO.AddComponent<Text>();
        sequenceText.font = announcementText.font;
        sequenceText.fontSize = 32;
        sequenceText.alignment = TextAnchor.MiddleCenter;
        sequenceText.color = Color.white;
        Outline seqOutline = seqGO.AddComponent<Outline>();
        seqOutline.effectColor = Color.black;
        seqOutline.effectDistance = new Vector2(1.5f, -1.5f);
        RectTransform seqRt = seqGO.GetComponent<RectTransform>();
        seqRt.anchorMin = new Vector2(0.5f, 1f);
        seqRt.anchorMax = new Vector2(0.5f, 1f);
        seqRt.pivot = new Vector2(0.5f, 1f);
        seqRt.anchoredPosition = new Vector2(0, -150);
        seqRt.sizeDelta = new Vector2(1100, 60);

        GameObject roundGO = new GameObject("RoundText");
        roundGO.transform.SetParent(canvasGO.transform, false);
        roundText = roundGO.AddComponent<Text>();
        roundText.font = announcementText.font;
        roundText.fontSize = 28;
        roundText.alignment = TextAnchor.UpperRight;
        roundText.color = Color.white;
        Outline roundOutline = roundGO.AddComponent<Outline>();
        roundOutline.effectColor = Color.black;
        roundOutline.effectDistance = new Vector2(1.5f, -1.5f);
        RectTransform roundRt = roundGO.GetComponent<RectTransform>();
        roundRt.anchorMin = new Vector2(1f, 1f);
        roundRt.anchorMax = new Vector2(1f, 1f);
        roundRt.pivot = new Vector2(1f, 1f);
        roundRt.anchoredPosition = new Vector2(-30, -30);
        roundRt.sizeDelta = new Vector2(300, 60);
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

                yield return new WaitForSeconds(currentReactionTime);

                if (!isGameActive.Value) break;

                netTilesHidden.Value = true;

                yield return new WaitForSeconds(hiddenDuration);
            }

            netCurrentStepIndex.Value = -1;
            netTilesHidden.Value = false;

            if (isGameActive.Value)
                currentReactionTime = Mathf.Max(minReactionTime, currentReactionTime - reactionTimeStep);
        }
    }

    // Runs on every peer whenever the replicated state changes - this is what actually
    // shows/hides tiles and updates the UI text, for the server/host as well as clients.
    void ApplyVisuals()
    {
        if (roundText != null) roundText.text = isGameActive.Value ? $"Runde {netRoundNumber.Value}" : "";

        List<TileColor> seq = ParseSequence(netSequenceCsv.Value.ToString());
        int step = netCurrentStepIndex.Value;

        if (!isGameActive.Value || seq.Count == 0 || step < 0 || step >= seq.Count)
        {
            ShowAllTiles();
            if (announcementText != null) announcementText.text = "";
            if (sequenceText != null) sequenceText.text = "";
            return;
        }

        TileColor chosen = seq[step];

        if (netTilesHidden.Value) HideOtherTiles(chosen);
        else ShowAllTiles();

        if (announcementText != null)
        {
            announcementText.text = $"Steh auf: {chosen}!";
            announcementText.color = GetDisplayColor(chosen);
        }
        if (sequenceText != null) sequenceText.text = BuildSequencePreview(seq, step);
    }

    string BuildSequencePreview(List<TileColor> sequence, int currentIndex)
    {
        if (sequence.Count <= 1) return string.Empty;

        List<string> parts = new List<string>();
        for (int i = 0; i < sequence.Count; i++)
        {
            if (i < currentIndex) parts.Add($"{sequence[i]} ✓");
            else if (i == currentIndex) parts.Add($"[{sequence[i]}]");
            else parts.Add(sequence[i].ToString());
        }
        return string.Join("  →  ", parts);
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

    void ShowAllTiles()
    {
        foreach (GameObject tile in allTiles)
            if (tile != null) tile.SetActive(true);
    }

    void HideOtherTiles(TileColor chosen)
    {
        HashSet<GameObject> keep = new HashSet<GameObject>(tilesByColor[chosen]);
        foreach (GameObject tile in allTiles)
        {
            if (tile == null) continue;
            tile.SetActive(keep.Contains(tile));
        }
    }

    Color GetDisplayColor(TileColor c)
    {
        switch (c)
        {
            case TileColor.Red: return Color.red;
            case TileColor.Green: return Color.green;
            case TileColor.Blue: return Color.blue;
            case TileColor.Yellow: return Color.yellow;
            case TileColor.Orange: return new Color(1f, 0.5f, 0f);
            case TileColor.Violet: return new Color(0.6f, 0f, 1f);
            case TileColor.Black: return Color.white;
            case TileColor.White: return Color.white;
            default: return Color.white;
        }
    }
}
