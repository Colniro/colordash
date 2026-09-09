using TMPro;
using UnityEngine;

// Schwebendes Namensschild über jedem Spieler. Bewusst KEIN Kind des Spielers:
// das Spieler-Root hat eine ungleichmäßige Skalierung (0.6/1/0.6), die eine
// World-Space-Canvas verzerren würde. Stattdessen folgt das Schild dem Ziel.
public class PlayerNametag : MonoBehaviour
{
    [Header("Platzierung")]
    public float heightOffset = 1.45f;
    public float worldScale = 0.0055f;
    public float maxVisibleDistance = 45f;

    private PlayerMovement target;
    private TextMeshProUGUI nameLabel;
    private TextMeshProUGUI emoteLabel;
    private Canvas canvas;
    private float emoteTimer;

    public static PlayerNametag Create(PlayerMovement owner)
    {
        GameObject go = new GameObject($"Nametag_{owner.OwnerClientId}");
        PlayerNametag tag = go.AddComponent<PlayerNametag>();
        tag.Bind(owner);
        return tag;
    }

    private void Bind(PlayerMovement owner)
    {
        target = owner;

        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(600f, 200f);
        canvasRect.localScale = Vector3.one * worldScale;

        nameLabel = UIFactory.CreateText(canvas.transform, "Name", owner.DisplayName, 46f,
            FontStyles.Bold, Color.white, TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.5f), new Vector2(0, -34), new Vector2(600, 70));
        UIFactory.SetOutline(nameLabel, 0.28f, Color.black);

        emoteLabel = UIFactory.CreateText(canvas.transform, "Emote", "", 54f,
            FontStyles.Bold, UIFactory.Accent, TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(600, 80));
        UIFactory.SetOutline(emoteLabel, 0.3f, Color.black);
    }

    public void ShowEmote(string text, float duration)
    {
        if (emoteLabel == null) return;

        emoteLabel.text = text;
        emoteTimer = duration;
    }

    void LateUpdate()
    {
        // Spieler weg (disconnect / Szenenwechsel) -> Schild mit aufräumen.
        if (target == null)
        {
            Destroy(gameObject);
            return;
        }

        if (emoteTimer > 0f)
        {
            emoteTimer -= Time.deltaTime;
            if (emoteTimer <= 0f && emoteLabel != null) emoteLabel.text = "";
        }

        Camera viewer = Camera.main;
        if (viewer == null)
        {
            SetVisible(false);
            return;
        }

        // Das eigene Schild würde in der Ego-Perspektive direkt vor der Kamera hängen.
        // Beim Zuschauen sieht man sich selbst von außen, da darf es sichtbar sein.
        bool isSelf = target == PlayerMovement.LocalPlayer;
        if (isSelf && !target.IsSpectating)
        {
            SetVisible(false);
            return;
        }

        Vector3 position = target.transform.position + Vector3.up * heightOffset;
        float distance = Vector3.Distance(viewer.transform.position, position);
        if (distance > maxVisibleDistance)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        transform.position = position;
        transform.rotation = viewer.transform.rotation;

        if (nameLabel != null)
        {
            nameLabel.text = target.DisplayName;
            nameLabel.color = target.IsAlive ? Color.white : new Color(0.65f, 0.65f, 0.7f);
        }
    }

    private void SetVisible(bool visible)
    {
        if (canvas != null && canvas.enabled != visible) canvas.enabled = visible;
    }
}
