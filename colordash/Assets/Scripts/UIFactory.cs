using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Zentrale Bau-Helfer für die komplette Laufzeit-UI (TextMeshPro statt Legacy-Text).
// Ersetzt die früher in ColorDashManager und GameFlowManager duplizierten UI-Blöcke.
public static class UIFactory
{
    public static readonly Color Accent = new Color(1f, 0.85f, 0.2f);
    public static readonly Color PanelColor = new Color(0.09f, 0.10f, 0.14f, 0.96f);
    public static readonly Color Muted = new Color(0.75f, 0.75f, 0.8f);

    private static Sprite whiteSprite;
    private static TMP_FontAsset font;

    // Einfaches Weiss-Sprite; nötig, weil Image.fillAmount ohne Sprite nicht funktioniert.
    public static Sprite WhiteSprite
    {
        get
        {
            if (whiteSprite == null)
            {
                Texture2D tex = Texture2D.whiteTexture;
                whiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                whiteSprite.name = "ColorDashWhite";
            }
            return whiteSprite;
        }
    }

    public static TMP_FontAsset Font
    {
        get
        {
            if (font != null) return font;

            font = TMP_Settings.defaultFontAsset;
            if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (font == null)
            {
                TMP_FontAsset[] all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (all.Length > 0) font = all[0];
            }
            if (font == null)
                Debug.LogError("[ColorDash] Kein TMP-Font gefunden. Bitte Window > TextMeshPro > Import TMP Essential Resources ausfuehren.");

            return font;
        }
    }

    public static Canvas CreateCanvas(string name, int sortingOrder)
    {
        GameObject go = new GameObject(name);
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    public static RectTransform Stretch(RectTransform rt, float padding = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
        return rt;
    }

    public static TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize,
        FontStyles style, Color color, TextAlignmentOptions alignment,
        Vector2 anchor, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.font = Font;
        t.text = text;
        t.fontSize = fontSize;
        t.fontStyle = style;
        t.color = color;
        t.alignment = alignment;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;

        return t;
    }

    // TMP zeichnet Umrandungen über das Material, nicht über eine Outline-Komponente.
    public static void SetOutline(TextMeshProUGUI text, float width, Color color)
    {
        if (text == null) return;

        Material mat = text.fontMaterial;
        if (mat == null) return;

        mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
        mat.SetColor(ShaderUtilities.ID_OutlineColor, color);
        text.UpdateMeshPadding();
    }

    public static Image CreateImage(Transform parent, string name, Color color,
        Vector2 anchor, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        img.sprite = WhiteSprite;
        img.type = Image.Type.Simple;
        img.color = color;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;

        return img;
    }

    public static Image CreateFullscreenDim(Transform parent, float alpha)
    {
        GameObject go = new GameObject("Dim");
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.sprite = WhiteSprite;
        img.color = new Color(0f, 0f, 0f, alpha);
        Stretch(go.GetComponent<RectTransform>());
        return img;
    }

    // Fortschritts-/Countdown-Balken. Rückgabewert ist das Füll-Image (fillAmount 0..1).
    public static Image CreateFillBar(Transform parent, string name, Color background, Color fill,
        Vector2 anchor, Vector2 anchoredPosition, Vector2 size, float border = 3f)
    {
        Image bg = CreateImage(parent, name, background, anchor, anchoredPosition, size);
        bg.raycastTarget = false;

        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(bg.transform, false);
        Image fillImg = fillGO.AddComponent<Image>();
        fillImg.sprite = WhiteSprite;
        fillImg.color = fill;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount = 1f;
        fillImg.raycastTarget = false;
        Stretch(fillGO.GetComponent<RectTransform>(), border);

        return fillImg;
    }

    public static Button CreateButton(Transform parent, string label, Color color,
        Vector2 anchoredPosition, Vector2 size, float fontSize = 24f)
    {
        GameObject go = new GameObject("Button_" + label);
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        img.sprite = WhiteSprite;
        img.color = color;

        Button btn = go.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
        btn.colors = colors;
        btn.targetGraphic = img;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;

        GameObject textGO = new GameObject("Label");
        textGO.transform.SetParent(go.transform, false);
        TextMeshProUGUI t = textGO.AddComponent<TextMeshProUGUI>();
        t.font = Font;
        t.text = label;
        t.fontSize = fontSize;
        t.fontStyle = FontStyles.Bold;
        t.color = Color.white;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        Stretch(textGO.GetComponent<RectTransform>(), 6f);

        return btn;
    }

    public static Toggle CreateToggle(Transform parent, string label, bool value,
        Vector2 anchoredPosition, Vector2 size)
    {
        GameObject go = new GameObject("Toggle_" + label);
        go.transform.SetParent(parent, false);
        go.SetActive(false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;

        Toggle toggle = go.AddComponent<Toggle>();

        Image box = CreateImage(go.transform, "Box", new Color(1f, 1f, 1f, 0.18f),
            new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(26f, 26f));
        box.rectTransform.pivot = new Vector2(0.5f, 0.5f);

        Image check = CreateImage(box.transform, "Check", Accent,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 16f));

        TextMeshProUGUI text = CreateText(go.transform, "Label", label, 20f, FontStyles.Normal, Color.white,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(size.x - 40f, size.y));
        text.rectTransform.pivot = new Vector2(0f, 0.5f);

        toggle.targetGraphic = box;
        toggle.graphic = check;
        toggle.isOn = value;

        go.SetActive(true);
        return toggle;
    }

    public static Slider CreateSlider(Transform parent, Vector2 anchoredPosition, Vector2 size,
        float min, float max, float value)
    {
        GameObject go = new GameObject("Slider");
        go.transform.SetParent(parent, false);
        go.SetActive(false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;

        Image background = go.AddComponent<Image>();
        background.sprite = WhiteSprite;
        background.color = new Color(1f, 1f, 1f, 0.18f);

        Slider slider = go.AddComponent<Slider>();

        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(go.transform, false);
        RectTransform fillAreaRt = fillArea.AddComponent<RectTransform>();
        Stretch(fillAreaRt);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        Image fillImg = fill.AddComponent<Image>();
        fillImg.sprite = WhiteSprite;
        fillImg.color = Accent;
        RectTransform fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.sizeDelta = new Vector2(10f, 0f);

        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(go.transform, false);
        RectTransform handleAreaRt = handleArea.AddComponent<RectTransform>();
        Stretch(handleAreaRt);
        handleAreaRt.offsetMin = new Vector2(9f, 0f);
        handleAreaRt.offsetMax = new Vector2(-9f, 0f);

        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        Image handleImg = handle.AddComponent<Image>();
        handleImg.sprite = WhiteSprite;
        handleImg.color = Color.white;
        RectTransform handleRt = handle.GetComponent<RectTransform>();
        handleRt.anchorMin = Vector2.zero;
        handleRt.anchorMax = new Vector2(0f, 1f);
        handleRt.sizeDelta = new Vector2(18f, 0f);

        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = Mathf.Clamp(value, min, max);

        go.SetActive(true);
        return slider;
    }

    public static TMP_InputField CreateInputField(Transform parent, string placeholderText,
        Vector2 anchoredPosition, Vector2 size, int characterLimit = 12)
    {
        GameObject go = new GameObject("InputField");
        go.transform.SetParent(parent, false);
        // Inaktiv aufbauen, damit TMP_InputField.Awake erst nach der Verdrahtung laeuft.
        go.SetActive(false);

        Image bg = go.AddComponent<Image>();
        bg.sprite = WhiteSprite;
        bg.color = new Color(1f, 1f, 1f, 0.92f);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;

        TMP_InputField field = go.AddComponent<TMP_InputField>();

        GameObject viewport = new GameObject("Text Area");
        viewport.transform.SetParent(go.transform, false);
        RectTransform viewportRt = viewport.AddComponent<RectTransform>();
        Stretch(viewportRt, 8f);
        viewport.AddComponent<RectMask2D>();

        TextMeshProUGUI placeholder = CreateText(viewport.transform, "Placeholder", placeholderText, 22f,
            FontStyles.Italic, new Color(0f, 0f, 0f, 0.4f), TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.5f), Vector2.zero, size);
        Stretch(placeholder.rectTransform);

        TextMeshProUGUI text = CreateText(viewport.transform, "Text", "", 22f,
            FontStyles.Bold, Color.black, TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.5f), Vector2.zero, size);
        text.richText = false;
        Stretch(text.rectTransform);

        field.textViewport = viewportRt;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.characterLimit = characterLimit;
        field.targetGraphic = bg;
        field.caretColor = Color.black;
        field.customCaretColor = true;

        go.SetActive(true);
        return field;
    }
}
