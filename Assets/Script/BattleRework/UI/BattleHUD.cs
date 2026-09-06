using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime broadcast HUD used by the battle vertical slice.
/// Replaces the large IMGUI test overlay with a compact TV-show styled HUD.
/// It auto-creates itself when a battle scene has no authored BattleHUD yet.
/// </summary>
public sealed class BattleHUD : MonoBehaviour
{
    private static BattleHUD instance;

    [Header("Runtime Style")]
    [SerializeField] private Color panelColor = new(0.022f, 0.028f, 0.043f, 0.94f);
    [SerializeField] private Color accentColor = new(1f, 0.18f, 0.58f, 1f);
    [SerializeField] private Color goldColor = new(1f, 0.80f, 0.25f, 1f);
    [SerializeField] private Color hpColor = new(0.95f, 0.22f, 0.34f, 1f);
    [SerializeField] private Color staminaColor = new(0.24f, 0.80f, 0.93f, 1f);

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private RunProgressSystem progress;
    private BattleEquipmentSystem equipmentSystem;
    private PlayerController player;

    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private Text stageText;
    private Text enemyText;
    private Text hpText;
    private Text staminaText;
    private Image hpFill;
    private Image staminaFill;
    private Text audienceText;
    private readonly Image[] slotBackgrounds = new Image[BattleEquipmentSystem.MaxSlotCount];
    private readonly Image[] slotIcons = new Image[BattleEquipmentSystem.MaxSlotCount];
    private readonly Text[] slotLabels = new Text[BattleEquipmentSystem.MaxSlotCount];
    private readonly Text[] slotGrades = new Text[BattleEquipmentSystem.MaxSlotCount];
    private float nextSlowRefresh;
    private bool legacyDummyOverlaysDisabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleHUD>() != null)
            return;

        GameObject host = new("BattleBroadcastHUDRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleHUD>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            return;
        }

        instance = this;
        EnsureCanvas();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void OnEnable()
    {
        ResolveSystems();
        SubscribeEquipment();
    }

    private void OnDisable()
    {
        UnsubscribeEquipment();
    }

    private void Update()
    {
        ResolveSystems();
        DisableLegacyDummyOverlays();
        if (canvas == null)
            EnsureCanvas();

        bool active = runManager != null && runManager.RunActive;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = active ? 1f : 0f;
            canvasGroup.blocksRaycasts = active;
            canvasGroup.interactable = active;
        }

        if (!active)
            return;

        RefreshVitalBars();
        if (Time.unscaledTime >= nextSlowRefresh)
        {
            nextSlowRefresh = Time.unscaledTime + 0.12f;
            RefreshStatus();
            RefreshEquipment();
        }
    }

    private void DisableLegacyDummyOverlays()
    {
        if (legacyDummyOverlaysDisabled)
            return;

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            string typeName = behaviour.GetType().Name;
            if (typeName == "BattleDummyUI" || typeName == "BattleDummyLoadoutUI" || typeName == "SynergyDummyUI")
                behaviour.enabled = false;
        }

        legacyDummyOverlaysDisabled = true;
    }

    private void ResolveSystems()
    {
        if (runManager == null) runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null) roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (progress == null) progress = FindFirstObjectByType<RunProgressSystem>();
        if (equipmentSystem == null)
        {
            BattleEquipmentSystem found = FindFirstObjectByType<BattleEquipmentSystem>();
            if (found != null)
            {
                UnsubscribeEquipment();
                equipmentSystem = found;
                SubscribeEquipment();
            }
        }
        if (player == null) player = FindFirstObjectByType<PlayerController>();
    }

    private void SubscribeEquipment()
    {
        if (equipmentSystem == null)
            return;
        equipmentSystem.InventoryChanged -= RefreshEquipment;
        equipmentSystem.InventoryChanged += RefreshEquipment;
        equipmentSystem.SlotCapacityChanged -= HandleSlotCapacityChanged;
        equipmentSystem.SlotCapacityChanged += HandleSlotCapacityChanged;
    }

    private void UnsubscribeEquipment()
    {
        if (equipmentSystem == null)
            return;
        equipmentSystem.InventoryChanged -= RefreshEquipment;
        equipmentSystem.SlotCapacityChanged -= HandleSlotCapacityChanged;
    }

    private void HandleSlotCapacityChanged(int _)
    {
        RefreshEquipment();
    }

    private void EnsureCanvas()
    {
        if (canvas != null)
            return;

        GameObject canvasObject = new("BattleBroadcastHUDCanvas");
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();
        canvasGroup = canvasObject.AddComponent<CanvasGroup>();

        BuildTopStatus();
        BuildEquipmentDock();
        RefreshStatus();
        RefreshEquipment();
    }

    private void BuildTopStatus()
    {
        GameObject panel = CreatePanel(canvas.transform, "BroadcastStatus", new Vector2(430f, 174f), panelColor);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -24f);

        GameObject liveBadge = CreatePanel(panel.transform, "LiveBadge", new Vector2(92f, 30f), new Color(0.42f, 0.035f, 0.09f, 0.98f));
        RectTransform liveRect = liveBadge.GetComponent<RectTransform>();
        liveRect.anchorMin = liveRect.anchorMax = new Vector2(0f, 1f);
        liveRect.pivot = new Vector2(0f, 1f);
        liveRect.anchoredPosition = new Vector2(18f, -14f);
        Text onAirText = CreateText(liveBadge.transform, "●  ON AIR", 12, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(onAirText.rectTransform);

        stageText = CreateText(panel.transform, "WAITING FOR TAKE", 18, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        SetAnchors(stageText.rectTransform, new Vector2(0.28f, 0.74f), new Vector2(0.95f, 0.94f));

        enemyText = CreateText(panel.transform, "ENEMY --", 12, FontStyle.Bold, TextAnchor.MiddleRight, goldColor);
        SetAnchors(enemyText.rectTransform, new Vector2(0.63f, 0.56f), new Vector2(0.94f, 0.70f));

        hpText = CreateText(panel.transform, "HP", 11, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.86f, 0.88f, 0.93f, 1f));
        SetAnchors(hpText.rectTransform, new Vector2(0.05f, 0.45f), new Vector2(0.20f, 0.57f));
        hpFill = CreateBar(panel.transform, "HPBar", new Vector2(0.20f, 0.47f), new Vector2(0.94f, 0.56f), hpColor);

        staminaText = CreateText(panel.transform, "ST", 11, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.86f, 0.88f, 0.93f, 1f));
        SetAnchors(staminaText.rectTransform, new Vector2(0.05f, 0.28f), new Vector2(0.20f, 0.40f));
        staminaFill = CreateBar(panel.transform, "StaminaBar", new Vector2(0.20f, 0.30f), new Vector2(0.94f, 0.39f), staminaColor);

        audienceText = CreateText(panel.transform, "VIEWERS 0   •   FANS 0   •   POP 0", 11, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.68f, 0.72f, 0.80f, 1f));
        SetAnchors(audienceText.rectTransform, new Vector2(0.05f, 0.06f), new Vector2(0.95f, 0.20f));
    }

    private void BuildEquipmentDock()
    {
        GameObject dock = CreatePanel(canvas.transform, "EquipmentDock", new Vector2(858f, 104f), new Color(0.016f, 0.02f, 0.032f, 0.94f));
        RectTransform dockRect = dock.GetComponent<RectTransform>();
        dockRect.anchorMin = dockRect.anchorMax = new Vector2(0.5f, 0f);
        dockRect.pivot = new Vector2(0.5f, 0f);
        dockRect.anchoredPosition = new Vector2(0f, 22f);

        float slotWidth = 84f;
        float spacing = 8f;
        float total = BattleEquipmentSystem.MaxSlotCount * slotWidth + (BattleEquipmentSystem.MaxSlotCount - 1) * spacing;
        float start = -total * 0.5f + slotWidth * 0.5f;

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            GameObject slot = CreatePanel(dock.transform, $"Slot_{i + 1}", new Vector2(slotWidth, 80f), new Color(0.055f, 0.062f, 0.082f, 1f));
            RectTransform slotRect = slot.GetComponent<RectTransform>();
            slotRect.anchorMin = slotRect.anchorMax = new Vector2(0.5f, 0.5f);
            slotRect.anchoredPosition = new Vector2(start + i * (slotWidth + spacing), 0f);
            slotBackgrounds[i] = slot.GetComponent<Image>();

            Button button = slot.AddComponent<Button>();
            button.targetGraphic = slotBackgrounds[i];
            int captured = i;
            button.onClick.AddListener(() => equipmentSystem?.EquipSlot(captured));

            Text number = CreateText(slot.transform, (i + 1).ToString(), 10, FontStyle.Bold, TextAnchor.UpperLeft, new Color(0.62f, 0.66f, 0.74f, 1f));
            SetAnchors(number.rectTransform, new Vector2(0.08f, 0.70f), new Vector2(0.35f, 0.94f));

            GameObject iconGo = new("Icon");
            iconGo.transform.SetParent(slot.transform, false);
            RectTransform iconRect = iconGo.AddComponent<RectTransform>();
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.60f);
            iconRect.sizeDelta = new Vector2(44f, 44f);
            slotIcons[i] = iconGo.AddComponent<Image>();
            slotIcons[i].preserveAspect = true;
            slotIcons[i].raycastTarget = false;

            slotLabels[i] = CreateText(slot.transform, "EMPTY", 9, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.72f, 0.75f, 0.82f, 1f));
            SetAnchors(slotLabels[i].rectTransform, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.30f));

            slotGrades[i] = CreateText(slot.transform, string.Empty, 9, FontStyle.Bold, TextAnchor.UpperRight, goldColor);
            SetAnchors(slotGrades[i].rectTransform, new Vector2(0.55f, 0.70f), new Vector2(0.92f, 0.94f));
        }
    }

    private void RefreshVitalBars()
    {
        if (player == null)
            return;

        float hpMax = Mathf.Max(1f, player.maxHp);
        float stMax = Mathf.Max(1f, player.maxStamina);
        if (hpFill != null) hpFill.fillAmount = Mathf.Clamp01(player.CurrentHp / hpMax);
        if (staminaFill != null) staminaFill.fillAmount = Mathf.Clamp01(player.CurrentStamina / stMax);
        if (hpText != null) hpText.text = $"HP  {player.CurrentHp:0}/{hpMax:0}";
        if (staminaText != null) staminaText.text = $"ST  {player.CurrentStamina:0}/{stMax:0}";
    }

    private void RefreshStatus()
    {
        if (stageText != null)
        {
            if (runManager != null && runManager.CurrentNode != null)
                stageText.text = $"TAKE {runManager.CurrentNode.depth + 1:00}  /  {runManager.CurrentNode.type.ToString().ToUpperInvariant()}";
            else
                stageText.text = "WAITING FOR NEXT TAKE";
        }

        if (enemyText != null)
            enemyText.text = roomManager != null && roomManager.IsRoomActive ? $"ENEMY  {roomManager.AliveMonsterCount:00}" : "ENEMY  --";

        if (audienceText != null && progress != null)
            audienceText.text = $"VIEWERS {progress.Viewers:N0}   •   FANS {progress.FanPoints:N0}   •   POP {progress.Popularity:N0}";
    }

    private void RefreshEquipment()
    {
        if (slotBackgrounds[0] == null)
            return;

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            bool unlocked = equipmentSystem != null && i < equipmentSystem.UnlockedSlotCount;
            BattleEquipmentSlot slot = unlocked && i < equipmentSystem.Slots.Count ? equipmentSystem.Slots[i] : null;
            bool occupied = slot != null && slot.equipment != null;
            bool equipped = occupied && equipmentSystem.IsSlotEquipped(i);

            slotBackgrounds[i].color = !unlocked
                ? new Color(0.028f, 0.032f, 0.043f, 0.72f)
                : equipped
                    ? new Color(0.18f, 0.07f, 0.16f, 1f)
                    : new Color(0.055f, 0.062f, 0.082f, 1f);

            slotIcons[i].enabled = occupied && slot.equipment.icon != null;
            slotIcons[i].sprite = occupied ? slot.equipment.icon : null;
            slotIcons[i].color = Color.white;

            if (!unlocked)
            {
                slotLabels[i].text = "LOCKED";
                slotLabels[i].color = new Color(0.35f, 0.38f, 0.44f, 1f);
                slotGrades[i].text = string.Empty;
            }
            else if (!occupied)
            {
                slotLabels[i].text = "EMPTY";
                slotLabels[i].color = new Color(0.50f, 0.54f, 0.62f, 1f);
                slotGrades[i].text = string.Empty;
            }
            else
            {
                slotLabels[i].text = Shorten(slot.equipment.GetDisplayName(), 12);
                slotLabels[i].color = Color.white;
                slotGrades[i].text = $"G{slot.grade}";
            }
        }
    }

    private static string Shorten(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value ?? string.Empty;
        return value.Substring(0, Mathf.Max(1, max - 1)) + "…";
    }

    private static Image CreateBar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color fillColor)
    {
        GameObject bg = new(name + "_BG");
        bg.transform.SetParent(parent, false);
        RectTransform bgRect = bg.AddComponent<RectTransform>();
        SetAnchors(bgRect, anchorMin, anchorMax);
        Image bgImage = bg.AddComponent<Image>();
        bgImage.sprite = BattleHudSpriteCache.RoundedPanel;
        bgImage.type = Image.Type.Sliced;
        bgImage.color = new Color(0.12f, 0.13f, 0.17f, 1f);
        bgImage.raycastTarget = false;

        GameObject fill = new(name + "_Fill");
        fill.transform.SetParent(bg.transform, false);
        RectTransform fillRect = fill.AddComponent<RectTransform>();
        Stretch(fillRect);
        Image image = fill.AddComponent<Image>();
        image.sprite = BattleHudSpriteCache.RoundedPanel;
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = 0;
        image.fillAmount = 1f;
        image.color = fillColor;
        image.raycastTarget = false;
        return image;
    }

    private static GameObject CreatePanel(Transform parent, string name, Vector2 size, Color color)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        Image image = go.AddComponent<Image>();
        image.sprite = BattleHudSpriteCache.RoundedPanel;
        image.type = Image.Type.Sliced;
        image.color = color;
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.08f);
        outline.effectDistance = new Vector2(1f, -1f);
        return go;
    }

    private static Text CreateText(Transform parent, string content, int size, FontStyle style, TextAnchor alignment, Color color)
    {
        GameObject go = new("Text");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        Text text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}

internal static class BattleHudSpriteCache
{
    private static Sprite roundedPanel;
    public static Sprite RoundedPanel => roundedPanel != null ? roundedPanel : roundedPanel = CreateRoundedPanel();

    private static Sprite CreateRoundedPanel()
    {
        const int pixels = 32;
        const float radius = 7f;
        Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        for (int y = 0; y < pixels; y++)
        {
            for (int x = 0; x < pixels; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x - 15.5f) - (15.5f - radius), 0f);
                float dy = Mathf.Max(Mathf.Abs(y - 15.5f) - (15.5f - radius), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(radius + 0.5f - d);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, pixels, pixels), new Vector2(0.5f, 0.5f), pixels, 0,
            SpriteMeshType.FullRect, new Vector4(8f, 8f, 8f, 8f));
        sprite.name = "RuntimeHudRoundedPanel";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
