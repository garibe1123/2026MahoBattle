using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PACK / Full Grid의 기본 아이템 시각 정책만 담당합니다.
///
/// 소유권 규칙:
/// - Reward 카드 외형은 BattleRewardCardActionController만 소유합니다.
/// - Mini PACK의 InteractionSelectionFrame은 BattleInventoryInteractionController가 소유합니다.
/// - Full Grid의 UnifiedSelectionFrame은 BattleUnifiedInventoryInspectController가 소유합니다.
/// - 이 클래스는 슬롯 배경, 아이콘 monochrome, 텍스트 톤, 합성 LV 색상만 적용합니다.
/// - RectTransform 위치/크기/Scale, 선택 Frame 활성 상태는 변경하지 않습니다.
///
/// 따라서 선택/호버를 표현하기 위해 다른 컨트롤러의 private field를 Reflection으로 읽지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(42000)]
public sealed class BattleMonochromeItemVisualController : MonoBehaviour
{
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const string MonochromeShaderName = "UI/BattleItemMonochrome";

    [Header("Inventory Base")]
    [SerializeField] private Color black = new(0.018f, 0.020f, 0.024f, 0.99f);
    [SerializeField] private Color darkCell = new(0.065f, 0.068f, 0.075f, 0.98f);
    [SerializeField] private Color emptyCell = new(0.12f, 0.12f, 0.14f, 0.96f);
    [SerializeField] private Color lockedCell = new(0.050f, 0.050f, 0.060f, 0.90f);
    [SerializeField] private Color white = new(0.96f, 0.96f, 0.96f, 1f);
    [SerializeField] private Color muted = new(0.56f, 0.57f, 0.60f, 1f);
    [SerializeField] private Color neutralOutline = new(0.88f, 0.88f, 0.88f, 0.20f);
    [SerializeField] private Color equippedAccent = new(1f, 0.80f, 0.10f, 1f);

    [Header("Merge Tier")]
    [SerializeField] private Color level1Color = Color.white;
    [SerializeField] private Color level2Color = new(0.24f, 0.72f, 1f, 1f);
    [SerializeField] private Color level3Color = new(0.72f, 0.34f, 1f, 1f);

    [Header("References")]
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleInventoryInteractionController inventoryInteraction;
    [SerializeField] private BattleUnifiedInventoryInspectController unifiedInventory;

    private Material monochromeMaterial;
    private RectTransform miniPack;
    private RectTransform fullRoot;
    private CanvasGroup fullGroup;
    private readonly RectTransform[] miniSlots = new RectTransform[SlotCount];
    private readonly RectTransform[] gridSlots = new RectTransform[SlotCount];
    private float nextResolveTime;

    private void Awake()
    {
        ResolveReferences();
        EnsureMaterial();
        ResolveUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureMaterial();
        ResolveUi();
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
        EnsureMaterial();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.18f;
            ResolveUi();
        }
    }

    private void LateUpdate()
    {
        ApplyStaticInventoryTheme();
        ApplyMiniPackVisuals();
        ApplyExpandedGridVisuals();
    }

    private void ResolveReferences()
    {
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);
        if (unifiedInventory == null)
            unifiedInventory = FindFirstObjectByType<BattleUnifiedInventoryInspectController>(FindObjectsInactive.Include);
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

    private void ResolveUi()
    {
        if (miniPack == null)
            miniPack = FindRect("BackpackMiniGrid");

        if (fullRoot == null)
        {
            fullRoot = FindRect("LoadoutSwitchFull");
            fullGroup = fullRoot != null ? fullRoot.GetComponent<CanvasGroup>() : null;
        }
        else if (fullGroup == null)
        {
            fullGroup = fullRoot.GetComponent<CanvasGroup>();
        }

        RectTransform board = fullRoot != null
            ? FindChildRect(fullRoot, "GridBoard")
            : null;

        for (int i = 0; i < SlotCount; i++)
        {
            if (miniSlots[i] == null && miniPack != null)
                miniSlots[i] = miniPack.Find($"BackpackCells/BackpackCell_{i}") as RectTransform;

            if (gridSlots[i] == null && board != null)
                gridSlots[i] = FindChildRect(board, $"GridSlot_{i}");
        }
    }

    private void ApplyStaticInventoryTheme()
    {
        if (miniPack != null)
        {
            Image packBack = miniPack.GetComponent<Image>();
            if (packBack != null)
                packBack.color = black;

            Outline packOutline = miniPack.GetComponent<Outline>();
            if (packOutline != null)
            {
                packOutline.effectColor = new Color(white.r, white.g, white.b, 0.55f);
                packOutline.effectDistance = new Vector2(3f, -3f);
            }

            SetImageColor(miniPack.Find("PackHeaderTag"), white);

            Text[] texts = miniPack.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                Text text = texts[i];
                if (text == null)
                    continue;

                string value = text.text ?? string.Empty;
                if (value == "PACK")
                    text.color = black;
                else if (value.Contains("TAB") || value.Contains("LB"))
                    text.color = muted;
            }
        }

        if (fullRoot != null)
        {
            SetImageColor(fullRoot.Find("YellowWedge"), new Color(0.90f, 0.90f, 0.90f, 0.93f));
            SetImageColor(fullRoot.Find("GridBoard/BoardBack"), new Color(0.90f, 0.90f, 0.90f, 0.96f));
        }
    }

    private void ApplyMiniPackVisuals()
    {
        if (equipmentSystem == null)
            return;

        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform slotRect = miniSlots[i];
            if (slotRect == null)
                continue;

            bool unlocked = equipmentSystem.IsSlotUnlocked(i);
            BattleEquipmentSlot runtimeSlot = GetRuntimeSlot(i);
            bool occupied = unlocked && runtimeSlot != null && runtimeSlot.equipment != null;
            bool focused = IsMiniSlotFocused(slotRect, i);

            Image background = slotRect.GetComponent<Image>();
            if (background != null)
                background.color = !unlocked ? lockedCell : occupied ? darkCell : emptyCell;

            Outline baseOutline = slotRect.GetComponent<Outline>();
            if (baseOutline != null)
            {
                baseOutline.effectColor = unlocked
                    ? neutralOutline
                    : new Color(0f, 0f, 0f, 0.60f);
                baseOutline.effectDistance = new Vector2(3f, -3f);
            }

            ApplyIconVisual(slotRect, occupied, focused);
            ApplyLevelLabel(slotRect, runtimeSlot);
        }
    }

    private void ApplyExpandedGridVisuals()
    {
        if (equipmentSystem == null)
            return;

        bool gridVisible = fullGroup != null && fullGroup.alpha > 0.05f;
        int focusedIndex = unifiedInventory != null ? unifiedInventory.ActiveInspectSlot : -1;

        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform slotRect = gridSlots[i];
            if (slotRect == null)
                continue;

            bool unlocked = equipmentSystem.IsSlotUnlocked(i);
            BattleEquipmentSlot runtimeSlot = GetRuntimeSlot(i);
            bool occupied = unlocked && runtimeSlot != null && runtimeSlot.equipment != null;
            bool picked = inventoryInteraction != null &&
                          inventoryInteraction.IsRewardPackEditing &&
                          inventoryInteraction.PadPickedSlot == i;
            bool focused = gridVisible && occupied && (focusedIndex == i || picked);

            Image background = slotRect.GetComponent<Image>();
            if (background != null)
                background.color = !unlocked ? lockedCell : darkCell;

            Outline baseOutline = slotRect.GetComponent<Outline>();
            if (baseOutline != null)
            {
                baseOutline.effectColor = unlocked
                    ? neutralOutline
                    : new Color(0f, 0f, 0f, 0.60f);
                baseOutline.effectDistance = new Vector2(3f, -3f);
            }

            ApplyIconVisual(slotRect, occupied, focused);
            ApplyLevelLabel(slotRect, runtimeSlot);
            ApplyGridTextTone(slotRect, runtimeSlot, focused, i == equipmentSystem.EquippedSlotIndex);
        }
    }

    private void ApplyIconVisual(RectTransform slotRect, bool occupied, bool focused)
    {
        Image icon = slotRect != null ? slotRect.Find("Icon")?.GetComponent<Image>() : null;
        if (icon == null)
            return;

        if (!occupied)
        {
            icon.material = null;
            return;
        }

        icon.material = focused ? null : monochromeMaterial;
        icon.color = focused
            ? Color.white
            : new Color(0.82f, 0.82f, 0.82f, 0.76f);
    }

    private void ApplyLevelLabel(RectTransform slotRect, BattleEquipmentSlot runtimeSlot)
    {
        if (slotRect == null)
            return;

        Text[] texts = slotRect.GetComponentsInChildren<Text>(true);
        if (runtimeSlot == null || runtimeSlot.equipment == null)
            return;

        Color tier = ResolveLevelColor(runtimeSlot.grade);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null)
                continue;

            string value = text.text ?? string.Empty;
            if (!value.StartsWith("G") && !value.StartsWith("LV."))
                continue;

            text.text = $"LV.{Mathf.Clamp(runtimeSlot.grade, 1, 3)}";
            text.color = tier;
            return;
        }
    }

    private void ApplyGridTextTone(
        RectTransform slotRect,
        BattleEquipmentSlot runtimeSlot,
        bool focused,
        bool equipped)
    {
        if (slotRect == null)
            return;

        Text[] texts = slotRect.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null)
                continue;

            string value = text.text ?? string.Empty;
            if (value.StartsWith("LV."))
                continue;

            if (value.Contains("EQUIPPED"))
                text.color = equipped ? equippedAccent : muted;
            else
                text.color = focused ? white : muted;
        }
    }

    private bool IsMiniSlotFocused(RectTransform slotRect, int index)
    {
        if (slotRect == null)
            return false;

        Transform frame = slotRect.Find("InteractionSelectionFrame");
        if (frame != null && frame.gameObject.activeSelf)
            return true;

        return inventoryInteraction != null &&
               inventoryInteraction.IsRewardPackEditing &&
               inventoryInteraction.HoveredSlot == index;
    }

    private BattleEquipmentSlot GetRuntimeSlot(int index)
    {
        if (equipmentSystem == null || index < 0 || index >= equipmentSystem.Slots.Count)
            return null;
        return equipmentSystem.Slots[index];
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

    private static RectTransform FindChildRect(Transform root, string objectName)
    {
        if (root == null)
            return null;

        RectTransform[] rects = root.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }
        return null;
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
