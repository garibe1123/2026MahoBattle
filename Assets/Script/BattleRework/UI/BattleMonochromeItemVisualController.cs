using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 아이템 UI의 최종 시각 정책입니다.
///
/// 기본 규칙:
/// - UI와 아이템 아이콘은 선택되지 않았을 때 흑백입니다.
/// - Reward 후보 / PACK / 확장 Grid에서 Hover 또는 실제 선택된 한 칸만 컬러가 살아납니다.
/// - Hover는 위치나 sibling 순서를 절대 바꾸지 않고 제자리 Scale만 키웁니다.
/// - 아이템 합성 단계는 기존 BattleEquipmentSlot.grade를 그대로 사용합니다.
///   LV.1 = White, LV.2 = Blue, LV.3 = Violet. 동일 아이템 판별은 기존 BattleEquipmentSO 참조 기준입니다.
/// - Reward 설명 바는 Hover/선택 아이템이 있을 때만 보입니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(42000)]
public sealed class BattleMonochromeItemVisualController : MonoBehaviour
{
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string MonochromeShaderName = "UI/BattleItemMonochrome";

    [Header("Monochrome")]
    [SerializeField] private Color black = new(0.018f, 0.020f, 0.024f, 0.99f);
    [SerializeField] private Color darkCell = new(0.065f, 0.068f, 0.075f, 0.98f);
    [SerializeField] private Color white = new(0.96f, 0.96f, 0.96f, 1f);
    [SerializeField] private Color muted = new(0.56f, 0.57f, 0.60f, 1f);
    [SerializeField] private Color neutralOutline = new(0.88f, 0.88f, 0.88f, 0.24f);

    [Header("Focus Only")]
    [SerializeField] private Color hoverAccent = new(0.12f, 0.88f, 0.92f, 1f);
    [SerializeField] private Color selectedAccent = new(1f, 0.78f, 0.08f, 1f);
    [SerializeField] private Color pickedAccent = new(1f, 0.16f, 0.50f, 1f);
    [SerializeField, Range(1.01f, 1.18f)] private float hoverScale = 1.075f;
    [SerializeField, Min(1f)] private float hoverSharpness = 18f;

    [Header("Merge Tier")]
    [SerializeField] private Color level1Color = Color.white;
    [SerializeField] private Color level2Color = new(0.24f, 0.72f, 1f, 1f);
    [SerializeField] private Color level3Color = new(0.72f, 0.34f, 1f, 1f);

    private BattleRunManager runManager;
    private BattleEquipmentSystem equipmentSystem;
    private BattleInventoryInteractionController inventoryInteraction;
    private BattleKineticLoadoutUI kineticLoadout;
    private BattleEquipmentDetailPanelController detailPanel;
    private BattleHUD battleHud;

    private FieldInfo pendingRewardIndexField;
    private FieldInfo selectedRewardSlotField;
    private FieldInfo padSelectedSlotField;
    private FieldInfo padPickedSlotField;
    private FieldInfo padModeActiveField;
    private FieldInfo loadoutSelectedIndexField;
    private FieldInfo loadoutSwitchHeldField;
    private FieldInfo loadoutBoardShownField;
    private FieldInfo detailManualSelectedSlotField;

    private Material monochromeMaterial;

    private RectTransform prizeChoices;
    private RectTransform rewardDescriptionBar;
    private CanvasGroup rewardDescriptionGroup;
    private RectTransform miniPack;
    private RectTransform fullRoot;
    private CanvasGroup fullGroup;

    private readonly List<RewardCardState> rewardCards = new();
    private readonly RectTransform[] miniSlots = new RectTransform[SlotCount];
    private readonly RectTransform[] gridSlots = new RectTransform[SlotCount];
    private readonly float[] miniHoverBlend = new float[SlotCount];
    private readonly float[] gridHoverBlend = new float[SlotCount];

    private RectTransform cachedPrizeChoices;
    private int cachedRewardCount = -1;
    private int hoveredReward = -1;
    private int hoveredMiniSlot = -1;
    private int hoveredGridSlot = -1;
    private float nextResolveTime;

    private sealed class RewardCardState
    {
        public RectTransform root;
        public RewardPrizeDrag drag;
        public Image background;
        public Image icon;
        public Outline outline;
        public Text[] texts;
        public float hoverBlend;
    }

    private void Awake()
    {
        ResolveReferences();
        CacheFields();
        EnsureMaterial();
        ResolveUi(true);
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheFields();
        EnsureMaterial();
        nextResolveTime = 0f;
    }

    private void OnDestroy()
    {
        if (monochromeMaterial != null)
            Destroy(monochromeMaterial);
    }

    private void Update()
    {
        ResolveReferences();
        CacheFields();
        EnsureMaterial();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.10f;
            ResolveUi(false);
        }
    }

    private void LateUpdate()
    {
        ResolveUi(false);
        ApplyStaticMonochrome();
        ApplyRewardChoiceVisuals();
        ApplyMiniPackVisuals();
        ApplyExpandedGridVisuals();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>();
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>();
        if (detailPanel == null)
            detailPanel = FindFirstObjectByType<BattleEquipmentDetailPanelController>();
        if (battleHud == null)
            battleHud = FindFirstObjectByType<BattleHUD>();
    }

    private void CacheFields()
    {
        if (battleHud != null && pendingRewardIndexField == null)
            pendingRewardIndexField = typeof(BattleHUD).GetField("pendingRewardIndex", PrivateInstance);

        if (inventoryInteraction != null && selectedRewardSlotField == null)
        {
            System.Type type = typeof(BattleInventoryInteractionController);
            selectedRewardSlotField = type.GetField("selectedRewardSlot", PrivateInstance);
            padSelectedSlotField = type.GetField("padSelectedSlot", PrivateInstance);
            padPickedSlotField = type.GetField("padPickedSlot", PrivateInstance);
            padModeActiveField = type.GetField("padModeActive", PrivateInstance);
        }

        if (kineticLoadout != null && loadoutSelectedIndexField == null)
        {
            System.Type type = typeof(BattleKineticLoadoutUI);
            loadoutSelectedIndexField = type.GetField("selectedIndex", PrivateInstance);
            loadoutSwitchHeldField = type.GetField("switchHeld", PrivateInstance);
            loadoutBoardShownField = type.GetField("boardWasShown", PrivateInstance);
        }

        if (detailPanel != null && detailManualSelectedSlotField == null)
            detailManualSelectedSlotField = typeof(BattleEquipmentDetailPanelController).GetField("manualSelectedSlot", PrivateInstance);
    }

    private void EnsureMaterial()
    {
        if (monochromeMaterial != null)
            return;

        Shader shader = Shader.Find(MonochromeShaderName);
        if (shader == null)
            shader = Resources.Load<Shader>("BattleItemMonochrome");
        if (shader == null)
            return;

        monochromeMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private void ResolveUi(bool forceRewardCache)
    {
        RectTransform resolvedChoices = FindRect("PrizeChoices");
        if (resolvedChoices != prizeChoices)
        {
            prizeChoices = resolvedChoices;
            forceRewardCache = true;
        }

        rewardDescriptionBar = FindRect("RewardActiveDescriptionBar");
        if (rewardDescriptionBar != null)
            rewardDescriptionGroup = rewardDescriptionBar.GetComponent<CanvasGroup>();

        miniPack = FindRect("BackpackMiniGrid");
        fullRoot = FindRect("LoadoutSwitchFull");
        if (fullRoot != null)
            fullGroup = fullRoot.GetComponent<CanvasGroup>();

        for (int i = 0; i < SlotCount; i++)
        {
            miniSlots[i] = miniPack != null
                ? miniPack.Find($"BackpackCells/BackpackCell_{i}") as RectTransform
                : null;
            gridSlots[i] = FindRect($"GridSlot_{i}");

            InstallSlotHover(miniSlots[i], i, false);
            InstallSlotHover(gridSlots[i], i, true);
        }

        int rewardCount = prizeChoices != null ? prizeChoices.childCount : -1;
        if (forceRewardCache || prizeChoices != cachedPrizeChoices || rewardCount != cachedRewardCount)
            RebuildRewardCache();
    }

    private void RebuildRewardCache()
    {
        rewardCards.Clear();
        cachedPrizeChoices = prizeChoices;
        cachedRewardCount = prizeChoices != null ? prizeChoices.childCount : -1;
        hoveredReward = -1;

        if (prizeChoices == null)
            return;

        for (int i = 0; i < prizeChoices.childCount; i++)
        {
            RectTransform root = prizeChoices.GetChild(i) as RectTransform;
            if (root == null)
                continue;

            RewardPrizeDrag drag = root.GetComponent<RewardPrizeDrag>();
            if (drag == null)
                continue;

            BattleMonochromeRewardHoverRelay relay = root.GetComponent<BattleMonochromeRewardHoverRelay>();
            if (relay == null)
                relay = root.gameObject.AddComponent<BattleMonochromeRewardHoverRelay>();
            relay.Configure(this, drag.RewardIndex);

            rewardCards.Add(new RewardCardState
            {
                root = root,
                drag = drag,
                background = root.GetComponent<Image>(),
                icon = root.Find("PrizeIcon")?.GetComponent<Image>(),
                outline = root.GetComponent<Outline>(),
                texts = root.GetComponentsInChildren<Text>(true),
                hoverBlend = 0f
            });
        }
    }

    private void InstallSlotHover(RectTransform slot, int index, bool expanded)
    {
        if (slot == null)
            return;

        BattleMonochromeInventoryHoverRelay relay = slot.GetComponent<BattleMonochromeInventoryHoverRelay>();
        if (relay == null)
            relay = slot.gameObject.AddComponent<BattleMonochromeInventoryHoverRelay>();
        relay.Configure(this, index, expanded);
    }

    internal void SetRewardHover(int rewardIndex, bool entered)
    {
        if (!IsRewardChoicePhase())
            return;

        if (entered)
            hoveredReward = rewardIndex;
        else if (hoveredReward == rewardIndex)
            hoveredReward = -1;
    }

    internal void SetInventoryHover(int slotIndex, bool expanded, bool entered)
    {
        int current = expanded ? hoveredGridSlot : hoveredMiniSlot;
        if (entered)
            current = slotIndex;
        else if (current == slotIndex)
            current = -1;

        if (expanded)
            hoveredGridSlot = current;
        else
            hoveredMiniSlot = current;

        if (!expanded || detailPanel == null || detailManualSelectedSlotField == null)
            return;

        if (entered && HasItem(slotIndex))
        {
            detailManualSelectedSlotField.SetValue(detailPanel, slotIndex);
        }
        else if (!entered)
        {
            int manual = ReadInt(detailManualSelectedSlotField, detailPanel, -1);
            if (manual == slotIndex)
                detailManualSelectedSlotField.SetValue(detailPanel, -1);
        }
    }

    private void ApplyStaticMonochrome()
    {
        if (miniPack != null)
        {
            Image packBack = miniPack.GetComponent<Image>();
            if (packBack != null)
                packBack.color = black;

            Outline packOutline = miniPack.GetComponent<Outline>();
            if (packOutline != null)
                packOutline.effectColor = new Color(white.r, white.g, white.b, 0.72f);

            SetImageColor(miniPack.Find("PackHeaderTag"), white);

            Text[] texts = miniPack.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                Text text = texts[i];
                if (text == null)
                    continue;
                string value = text.text ?? string.Empty;
                text.color = value == "PACK" ? black : (value.Contains("TAB") ? muted : white);
            }
        }

        if (fullRoot != null)
        {
            SetImageColor(fullRoot.Find("YellowWedge"), new Color(0.90f, 0.90f, 0.90f, 0.93f));
            Transform board = fullRoot.Find("GridBoard/BoardBack");
            SetImageColor(board, new Color(0.90f, 0.90f, 0.90f, 0.96f));
        }

        RectTransform rewardAccent = FindRect("RewardKineticAccentLayer");
        if (rewardAccent != null)
        {
            SetImageColor(rewardAccent.Find("YellowHeaderCut"), white);
            SetImageColor(rewardAccent.Find("PinkSlash"), new Color(0.76f, 0.76f, 0.76f, 0.95f));
            SetImageColor(rewardAccent.Find("CyanRule"), new Color(0.88f, 0.88f, 0.88f, 0.85f));
            SetImageColor(rewardAccent.Find("PaperCut"), new Color(1f, 1f, 1f, 0.14f));
        }
    }

    private void ApplyRewardChoiceVisuals()
    {
        bool rewardChoice = IsRewardChoicePhase();
        int pending = rewardChoice ? ReadInt(pendingRewardIndexField, battleHud, -1) : -1;
        int focusIndex = rewardChoice && hoveredReward >= 0 ? hoveredReward : pending;

        float dt = Time.unscaledDeltaTime;
        float blendStep = 1f - Mathf.Exp(-Mathf.Max(1f, hoverSharpness) * dt);

        for (int i = 0; i < rewardCards.Count; i++)
        {
            RewardCardState card = rewardCards[i];
            if (card == null || card.root == null || card.drag == null)
                continue;

            int rewardIndex = card.drag.RewardIndex;
            bool focused = rewardChoice && rewardIndex == focusIndex;
            bool hovered = rewardChoice && rewardIndex == hoveredReward;
            bool selected = rewardChoice && rewardIndex == pending;

            card.hoverBlend = Mathf.Lerp(card.hoverBlend, hovered ? 1f : 0f, blendStep);
            float scale = Mathf.Lerp(1f, hoverScale, card.hoverBlend);
            card.root.localScale = Vector3.one * scale;

            Color stateColor = selected ? selectedAccent : hoverAccent;
            if (card.background != null)
                card.background.color = focused
                    ? Color.Lerp(darkCell, stateColor, 0.14f)
                    : darkCell;

            if (card.outline != null)
            {
                card.outline.enabled = focused;
                card.outline.effectColor = stateColor;
                card.outline.effectDistance = hovered
                    ? new Vector2(6f, -6f)
                    : new Vector2(5f, -5f);
            }

            if (card.icon != null)
            {
                card.icon.material = focused ? null : monochromeMaterial;
                card.icon.color = focused ? Color.white : new Color(0.82f, 0.82f, 0.82f, 0.72f);
            }

            BattleEquipmentSO equipment = GetReward(rewardIndex);
            ApplyRewardTexts(card.texts, equipment, focused, stateColor);
        }

        if (rewardDescriptionGroup != null)
        {
            bool showDescription = rewardChoice && focusIndex >= 0;
            rewardDescriptionGroup.alpha = showDescription ? 1f : 0f;
            rewardDescriptionGroup.blocksRaycasts = false;
            rewardDescriptionGroup.interactable = false;
        }
    }

    private void ApplyRewardTexts(Text[] texts, BattleEquipmentSO equipment, bool focused, Color stateColor)
    {
        if (texts == null)
            return;

        string rarity = equipment != null ? equipment.rarity.ToString().ToUpperInvariant() : string.Empty;
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null)
                continue;

            string value = text.text ?? string.Empty;
            if (!string.IsNullOrEmpty(rarity) && (value == rarity || value.StartsWith("LV.1 / ")))
            {
                text.text = $"LV.1 / {rarity}";
                text.color = focused ? level1Color : muted;
                continue;
            }

            if (value.Contains("CLICK") || value.Contains("DRAG"))
            {
                text.color = focused ? stateColor : muted;
                continue;
            }

            text.color = focused ? white : new Color(muted.r, muted.g, muted.b, 0.82f);
        }
    }

    private void ApplyMiniPackVisuals()
    {
        if (equipmentSystem == null)
            return;

        float step = 1f - Mathf.Exp(-Mathf.Max(1f, hoverSharpness) * Time.unscaledDeltaTime);
        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform slot = miniSlots[i];
            if (slot == null)
                continue;

            bool occupied = HasItem(i);
            bool hovered = occupied && i == hoveredMiniSlot;
            bool selected = IsMiniSlotSelected(i);
            bool focused = hovered || selected;

            miniHoverBlend[i] = Mathf.Lerp(miniHoverBlend[i], hovered ? 1f : 0f, step);
            slot.localScale = Vector3.one * Mathf.Lerp(1f, hoverScale, miniHoverBlend[i]);

            Image back = slot.GetComponent<Image>();
            if (back != null)
                back.color = occupied ? darkCell : new Color(0.12f, 0.12f, 0.14f, 0.96f);

            Outline outline = slot.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = neutralOutline;
                outline.effectDistance = new Vector2(3f, -3f);
            }

            Image icon = slot.Find("Icon")?.GetComponent<Image>();
            if (icon != null)
            {
                icon.material = focused ? null : monochromeMaterial;
                icon.color = focused ? Color.white : new Color(0.84f, 0.84f, 0.84f, 0.78f);
            }

            BattleEquipmentSlot runtimeSlot = GetRuntimeSlot(i);
            Color tier = ResolveLevelColor(runtimeSlot != null ? runtimeSlot.grade : 1);
            Image tierBar = slot.Find("CellAccent")?.GetComponent<Image>();
            if (tierBar != null)
            {
                tierBar.enabled = occupied;
                tierBar.color = tier;
            }
            ApplyLevelLabel(slot, runtimeSlot, tier);
        }
    }

    private void ApplyExpandedGridVisuals()
    {
        if (equipmentSystem == null)
            return;

        int selected = ResolveExpandedSelectedSlot();
        int focus = hoveredGridSlot >= 0 && HasItem(hoveredGridSlot) ? hoveredGridSlot : selected;
        bool visible = IsExpandedGridContext();
        float step = 1f - Mathf.Exp(-Mathf.Max(1f, hoverSharpness) * Time.unscaledDeltaTime);

        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform slot = gridSlots[i];
            if (slot == null)
                continue;

            bool occupied = HasItem(i);
            bool hovered = visible && occupied && i == hoveredGridSlot;
            bool focused = visible && occupied && i == focus;
            bool picked = IsRewardPickedSlot(i);

            gridHoverBlend[i] = Mathf.Lerp(gridHoverBlend[i], hovered ? 1f : 0f, step);
            slot.localScale = Vector3.one * Mathf.Lerp(1f, hoverScale, gridHoverBlend[i]);

            Image back = slot.GetComponent<Image>();
            Color focusColor = picked ? pickedAccent : hovered ? hoverAccent : selectedAccent;
            if (back != null)
                back.color = focused ? Color.Lerp(darkCell, focusColor, 0.14f) : darkCell;

            Outline outline = slot.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = focused ? focusColor : neutralOutline;
                outline.effectDistance = focused ? new Vector2(5f, -5f) : new Vector2(3f, -3f);
            }

            Image icon = slot.Find("Icon")?.GetComponent<Image>();
            if (icon != null)
            {
                icon.material = focused ? null : monochromeMaterial;
                icon.color = focused ? Color.white : new Color(0.84f, 0.84f, 0.84f, 0.78f);
            }

            BattleEquipmentSlot runtimeSlot = GetRuntimeSlot(i);
            Color tier = ResolveLevelColor(runtimeSlot != null ? runtimeSlot.grade : 1);
            ApplyLevelLabel(slot, runtimeSlot, tier);

            Text[] texts = slot.GetComponentsInChildren<Text>(true);
            for (int t = 0; t < texts.Length; t++)
            {
                Text text = texts[t];
                if (text == null)
                    continue;
                string value = text.text ?? string.Empty;
                if (value.StartsWith("LV."))
                    continue;
                text.color = focused ? white : muted;
            }
        }
    }

    private void ApplyLevelLabel(RectTransform slot, BattleEquipmentSlot runtimeSlot, Color tierColor)
    {
        if (slot == null || runtimeSlot == null || runtimeSlot.equipment == null)
            return;

        Text[] texts = slot.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null)
                continue;

            string value = text.text ?? string.Empty;
            if (!value.StartsWith("G") && !value.StartsWith("LV."))
                continue;

            if (value.Length > 1 && char.IsDigit(value[value.Length - 1]))
            {
                text.text = $"LV.{Mathf.Clamp(runtimeSlot.grade, 1, 3)}";
                text.color = tierColor;
                return;
            }
        }
    }

    private bool IsRewardChoicePhase()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Reward &&
               (inventoryInteraction == null || !inventoryInteraction.IsRewardPackEditing);
    }

    private bool IsExpandedGridContext()
    {
        if (runManager == null || !runManager.RunActive || fullGroup == null || fullGroup.alpha <= 0.05f)
            return false;
        return runManager.State == BattleRunState.Combat ||
               (runManager.State == BattleRunState.Reward && inventoryInteraction != null && inventoryInteraction.IsRewardPackEditing);
    }

    private int ResolveExpandedSelectedSlot()
    {
        int manual = ReadInt(detailManualSelectedSlotField, detailPanel, -1);
        if (manual >= 0 && HasItem(manual))
            return manual;

        if (runManager != null && runManager.State == BattleRunState.Reward && inventoryInteraction != null)
        {
            int mouse = ReadInt(selectedRewardSlotField, inventoryInteraction, -1);
            if (mouse >= 0 && HasItem(mouse))
                return mouse;

            bool padMode = ReadBool(padModeActiveField, inventoryInteraction, false);
            int pad = ReadInt(padSelectedSlotField, inventoryInteraction, -1);
            if (padMode && pad >= 0 && HasItem(pad))
                return pad;
            return -1;
        }

        if (runManager != null && runManager.State == BattleRunState.Combat && kineticLoadout != null)
        {
            bool held = ReadBool(loadoutSwitchHeldField, kineticLoadout, false);
            bool shown = ReadBool(loadoutBoardShownField, kineticLoadout, false);
            int index = ReadInt(loadoutSelectedIndexField, kineticLoadout, -1);
            return held && shown && HasItem(index) ? index : -1;
        }

        return -1;
    }

    private bool IsMiniSlotSelected(int index)
    {
        RectTransform slot = index >= 0 && index < miniSlots.Length ? miniSlots[index] : null;
        Transform frame = slot != null ? slot.Find("InteractionSelectionFrame") : null;
        return frame != null && frame.gameObject.activeSelf;
    }

    private bool IsRewardPickedSlot(int index)
    {
        if (inventoryInteraction == null || runManager == null || runManager.State != BattleRunState.Reward)
            return false;
        return ReadInt(padPickedSlotField, inventoryInteraction, -1) == index;
    }

    private BattleEquipmentSO GetReward(int index)
    {
        if (runManager == null || index < 0 || index >= runManager.CurrentRewardChoices.Count)
            return null;
        return runManager.CurrentRewardChoices[index];
    }

    private BattleEquipmentSlot GetRuntimeSlot(int index)
    {
        if (equipmentSystem == null || index < 0 || index >= equipmentSystem.Slots.Count)
            return null;
        return equipmentSystem.Slots[index];
    }

    private bool HasItem(int index)
    {
        BattleEquipmentSlot slot = GetRuntimeSlot(index);
        return slot != null && slot.equipment != null;
    }

    private Color ResolveLevelColor(int level)
    {
        return Mathf.Clamp(level, 1, 3) switch
        {
            1 => level1Color,
            2 => level2Color,
            _ => level3Color
        };
    }

    private static void SetImageColor(Transform target, Color color)
    {
        if (target == null)
            return;
        Image image = target.GetComponent<Image>();
        if (image != null)
            image.color = color;
    }

    private int ReadInt(FieldInfo field, object owner, int fallback)
    {
        if (field == null || owner == null)
            return fallback;
        object value = field.GetValue(owner);
        return value is int number ? number : fallback;
    }

    private bool ReadBool(FieldInfo field, object owner, bool fallback)
    {
        if (field == null || owner == null)
            return fallback;
        object value = field.GetValue(owner);
        return value is bool flag ? flag : fallback;
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = UnityEngine.Object.FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }
        return null;
    }
}

internal sealed class BattleMonochromeRewardHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private BattleMonochromeItemVisualController owner;
    private int rewardIndex;

    public void Configure(BattleMonochromeItemVisualController controller, int index)
    {
        owner = controller;
        rewardIndex = index;
    }

    public void OnPointerEnter(PointerEventData eventData) => owner?.SetRewardHover(rewardIndex, true);
    public void OnPointerExit(PointerEventData eventData) => owner?.SetRewardHover(rewardIndex, false);
}

internal sealed class BattleMonochromeInventoryHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private BattleMonochromeItemVisualController owner;
    private int slotIndex;
    private bool expanded;

    public void Configure(BattleMonochromeItemVisualController controller, int index, bool isExpanded)
    {
        owner = controller;
        slotIndex = index;
        expanded = isExpanded;
    }

    public void OnPointerEnter(PointerEventData eventData) => owner?.SetInventoryHover(slotIndex, expanded, true);
    public void OnPointerExit(PointerEventData eventData) => owner?.SetInventoryHover(slotIndex, expanded, false);
}

public static class BattleMonochromeItemVisualAutoInstaller
{
#if UNITY_EDITOR
    private static bool installQueued;

    [InitializeOnLoadMethod]
    private static void InitializeEditorInstaller()
    {
        EditorApplication.hierarchyChanged -= QueueInstall;
        EditorApplication.hierarchyChanged += QueueInstall;
        QueueInstall();
    }

    private static void QueueInstall()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || installQueued)
            return;
        installQueued = true;
        EditorApplication.delayCall += EnsureEditorComponent;
    }

    private static void EnsureEditorComponent()
    {
        installQueued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        BattleSceneManager[] managers = Resources.FindObjectsOfTypeAll<BattleSceneManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null || EditorUtility.IsPersistent(manager) ||
                !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded)
                continue;

            if (manager.GetComponent<BattleMonochromeItemVisualController>() != null)
                continue;

            Undo.AddComponent<BattleMonochromeItemVisualController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponent()
    {
        BattleSceneManager[] managers = UnityEngine.Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.GetComponent<BattleMonochromeItemVisualController>() == null)
                manager.gameObject.AddComponent<BattleMonochromeItemVisualController>();
        }
    }
}
