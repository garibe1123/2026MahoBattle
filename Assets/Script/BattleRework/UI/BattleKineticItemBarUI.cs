using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mini PACK의 구조 생성과 아이템 데이터 시각 갱신을 담당합니다.
///
/// 이 클래스는 PACK/Expanded Grid의 최종 위치, 크기, CanvasGroup 표시 상태를 쓰지 않습니다.
/// 해당 레이아웃은 BattleUnifiedInventoryInspectController가 단독 소유합니다.
/// Equipped / Synergy는 작은 CellAccent로만 표시합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30150)]
public sealed class BattleKineticItemBarUI : MonoBehaviour
{
    private const int GridSize = BattleEquipmentSystem.GridSize;
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const int CanvasSortingOrder = 770;

    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleGridSynergyController gridSynergy;

    [Header("Backpack HUD")]
    [SerializeField] private Color inkColor = new(0.030f, 0.028f, 0.045f, 0.98f);
    [SerializeField] private Color paperColor = new(0.92f, 0.94f, 0.97f, 1f);
    [SerializeField] private Color emptyCellColor = new(0.10f, 0.11f, 0.14f, 0.98f);
    [SerializeField] private Color occupiedCellColor = new(0.045f, 0.050f, 0.065f, 0.99f);
    [SerializeField] private Color lockedColor = new(0.075f, 0.080f, 0.10f, 0.92f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color accentCyan = new(0.15f, 0.88f, 0.92f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.52f, 1f);

    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform root;
    private Text capacityText;

    private readonly RectTransform[] slotRects = new RectTransform[SlotCount];
    private readonly Image[] slotBackgrounds = new Image[SlotCount];
    private readonly Image[] slotIcons = new Image[SlotCount];
    private readonly Image[] slotAccents = new Image[SlotCount];
    private readonly Outline[] slotOutlines = new Outline[SlotCount];
    private readonly Text[] slotGrades = new Text[SlotCount];
    private readonly Text[] slotStates = new Text[SlotCount];
    private readonly GameObject[] lockMarks = new GameObject[SlotCount];

    private BattleEquipmentSystem subscribedEquipment;
    private BattleGridSynergyController subscribedGrid;
    private Coroutine bindRoutine;

    public RectTransform Root => root;
    public CanvasGroup Group => group;

    private void Awake()
    {
        ResolveReferences();
        EnsureUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureUi();
        Subscribe();
        Refresh();

        if ((equipmentSystem == null || gridSynergy == null) && bindRoutine == null)
            bindRoutine = StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        if (bindRoutine != null)
            StopCoroutine(bindRoutine);
        bindRoutine = null;
        Unsubscribe();
    }

    private IEnumerator BindWhenReady()
    {
        while (isActiveAndEnabled && (equipmentSystem == null || gridSynergy == null))
        {
            ResolveReferences();
            Subscribe();
            EnsureUi();

            if (equipmentSystem != null && gridSynergy != null)
                break;

            yield return null;
        }

        bindRoutine = null;
        Refresh();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (gridSynergy == null)
            gridSynergy = FindFirstObjectByType<BattleGridSynergyController>();
    }

    private void Subscribe()
    {
        if (subscribedEquipment != equipmentSystem)
        {
            if (subscribedEquipment != null)
            {
                subscribedEquipment.InventoryChanged -= Refresh;
                subscribedEquipment.SlotCapacityChanged -= HandleCapacityChanged;
                subscribedEquipment.EquippedSlotChanged -= HandleEquippedChanged;
            }

            subscribedEquipment = equipmentSystem;
            if (subscribedEquipment != null)
            {
                subscribedEquipment.InventoryChanged += Refresh;
                subscribedEquipment.SlotCapacityChanged += HandleCapacityChanged;
                subscribedEquipment.EquippedSlotChanged += HandleEquippedChanged;
            }
        }

        if (subscribedGrid != gridSynergy)
        {
            if (subscribedGrid != null)
                subscribedGrid.GridSynergiesChanged -= Refresh;

            subscribedGrid = gridSynergy;
            if (subscribedGrid != null)
                subscribedGrid.GridSynergiesChanged += Refresh;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedEquipment != null)
        {
            subscribedEquipment.InventoryChanged -= Refresh;
            subscribedEquipment.SlotCapacityChanged -= HandleCapacityChanged;
            subscribedEquipment.EquippedSlotChanged -= HandleEquippedChanged;
        }

        if (subscribedGrid != null)
            subscribedGrid.GridSynergiesChanged -= Refresh;

        subscribedEquipment = null;
        subscribedGrid = null;
    }

    private void HandleCapacityChanged(int _)
    {
        Refresh();
    }

    private void HandleEquippedChanged(int _)
    {
        Refresh();
    }

    private void EnsureUi()
    {
        if (canvas != null)
            return;

        GameObject canvasObject = new("BattleBackpackGridCanvas");
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = CanvasSortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // 생성 시의 초기값일 뿐이며 이후 레이아웃은 Unified Inventory View가 소유합니다.
        root = CreateRect(canvas.transform, "BackpackMiniGrid", new Vector2(304f, 326f));
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.pivot = Vector2.zero;
        root.anchoredPosition = new Vector2(22f, 22f);
        root.localRotation = Quaternion.Euler(0f, 0f, -1.15f);

        Image back = root.gameObject.AddComponent<Image>();
        back.color = inkColor;
        back.raycastTarget = false;

        Outline outline = root.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(paperColor.r, paperColor.g, paperColor.b, 0.24f);
        outline.effectDistance = new Vector2(2f, -2f);

        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        BuildHeader();
        BuildGrid();
        Refresh();
    }

    private void BuildHeader()
    {
        RectTransform header = CreateRect(root, "PackHeaderTag", new Vector2(270f, 40f));
        header.anchorMin = header.anchorMax = new Vector2(0f, 1f);
        header.pivot = new Vector2(0f, 1f);
        header.anchoredPosition = new Vector2(14f, -6f);

        Text title = CreateText(header, "PACK", 17, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor);
        SetAnchors(title.rectTransform, new Vector2(0f, 0f), new Vector2(0.38f, 1f));

        capacityText = CreateText(header, "3 / 9", 11, FontStyle.Bold, TextAnchor.MiddleRight, accentCyan);
        SetAnchors(capacityText.rectTransform, new Vector2(0.42f, 0f), new Vector2(0.70f, 1f));

        Text hint = CreateText(header, "TAB / LB  OPEN", 9, FontStyle.Bold, TextAnchor.MiddleRight, new Color(0.58f, 0.63f, 0.72f, 1f));
        SetAnchors(hint.rectTransform, new Vector2(0.70f, 0f), new Vector2(1f, 1f));
    }

    private void BuildGrid()
    {
        const float cell = 78f;
        const float gap = 7f;
        const float total = cell * GridSize + gap * (GridSize - 1);

        RectTransform gridRoot = CreateRect(root, "BackpackCells", new Vector2(total, total));
        gridRoot.anchorMin = gridRoot.anchorMax = Vector2.zero;
        gridRoot.pivot = Vector2.zero;
        gridRoot.anchoredPosition = new Vector2(20f, 18f);

        for (int i = 0; i < SlotCount; i++)
        {
            int x = i % GridSize;
            int y = i / GridSize;
            RectTransform slot = CreateRect(gridRoot, $"BackpackCell_{i}", new Vector2(cell, cell));
            slot.anchorMin = slot.anchorMax = Vector2.zero;
            slot.pivot = new Vector2(0.5f, 0.5f);
            slot.anchoredPosition = new Vector2(
                x * (cell + gap) + cell * 0.5f,
                total - (y * (cell + gap) + cell * 0.5f));
            slotRects[i] = slot;

            Image cellImage = slot.gameObject.AddComponent<Image>();
            cellImage.color = emptyCellColor;
            cellImage.raycastTarget = false;
            slotBackgrounds[i] = cellImage;

            Outline cellOutline = slot.gameObject.AddComponent<Outline>();
            cellOutline.effectColor = new Color(0.54f, 0.58f, 0.66f, 0.22f);
            cellOutline.effectDistance = new Vector2(1f, -1f);
            slotOutlines[i] = cellOutline;

            Image icon = CreateImage(slot, "Icon", new Vector2(64f, 64f));
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            icon.rectTransform.anchoredPosition = Vector2.zero;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            slotIcons[i] = icon;

            Text grade = CreateText(slot, string.Empty, 9, FontStyle.Bold, TextAnchor.UpperRight, new Color(0.62f, 0.67f, 0.76f, 1f));
            SetAnchors(grade.rectTransform, new Vector2(0.50f, 0.70f), new Vector2(0.93f, 0.94f));
            slotGrades[i] = grade;

            Text state = CreateText(slot, string.Empty, 8, FontStyle.Bold, TextAnchor.LowerRight, accentCyan);
            SetAnchors(state.rectTransform, new Vector2(0.35f, 0.03f), new Vector2(0.93f, 0.24f));
            slotStates[i] = state;

            RectTransform accent = CreateRect(slot, "CellAccent", new Vector2(4f, cell - 12f));
            accent.anchorMin = accent.anchorMax = new Vector2(0f, 0.5f);
            accent.pivot = new Vector2(0f, 0.5f);
            accent.anchoredPosition = new Vector2(4f, 0f);
            Image accentImage = accent.gameObject.AddComponent<Image>();
            accentImage.raycastTarget = false;
            slotAccents[i] = accentImage;

            GameObject lockMark = new($"LockMark_{i}");
            lockMark.transform.SetParent(slot, false);
            RectTransform lockRect = lockMark.AddComponent<RectTransform>();
            Stretch(lockRect);
            lockMarks[i] = lockMark;

            CreateLockSlash(lockMark.transform, 45f);
            CreateLockSlash(lockMark.transform, -45f);
        }
    }

    private void CreateLockSlash(Transform parent, float angle)
    {
        RectTransform slash = CreateRect(parent, "LockSlash", new Vector2(50f, 5f));
        slash.anchorMin = slash.anchorMax = new Vector2(0.5f, 0.5f);
        slash.anchoredPosition = Vector2.zero;
        slash.localRotation = Quaternion.Euler(0f, 0f, angle);
        Image image = slash.gameObject.AddComponent<Image>();
        image.color = new Color(0.48f, 0.45f, 0.52f, 0.58f);
        image.raycastTarget = false;
    }

    private void Refresh()
    {
        if (equipmentSystem == null || canvas == null)
            return;

        int equipped = equipmentSystem.EquippedSlotIndex;
        if (capacityText != null)
            capacityText.text = $"{equipmentSystem.UnlockedSlotCount} / {SlotCount}";

        for (int i = 0; i < SlotCount; i++)
        {
            bool unlocked = equipmentSystem.IsSlotUnlocked(i);
            BattleEquipmentSlot slot = i < equipmentSystem.Slots.Count ? equipmentSystem.Slots[i] : null;
            BattleEquipmentSO equipment = slot?.equipment;
            bool occupied = equipment != null;
            bool isEquipped = i == equipped;
            bool linked = IsSlotLinked(i);

            if (slotBackgrounds[i] != null)
                slotBackgrounds[i].color = !unlocked ? lockedColor : occupied ? occupiedCellColor : emptyCellColor;

            if (slotOutlines[i] != null)
            {
                slotOutlines[i].effectColor = !unlocked
                    ? new Color(0.40f, 0.43f, 0.50f, 0.16f)
                    : isEquipped
                        ? new Color(accentCyan.r, accentCyan.g, accentCyan.b, 0.68f)
                        : linked
                            ? new Color(accentPink.r, accentPink.g, accentPink.b, 0.52f)
                            : new Color(0.54f, 0.58f, 0.66f, 0.22f);
                slotOutlines[i].effectDistance = isEquipped || linked
                    ? new Vector2(2f, -2f)
                    : new Vector2(1f, -1f);
            }

            if (slotIcons[i] != null)
            {
                slotIcons[i].sprite = occupied ? equipment.icon : null;
                slotIcons[i].enabled = unlocked && occupied && equipment.icon != null;
                slotIcons[i].color = Color.white;
            }

            if (slotGrades[i] != null)
            {
                slotGrades[i].text = unlocked && occupied ? $"G{slot.grade}" : string.Empty;
                slotGrades[i].color = new Color(0.62f, 0.67f, 0.76f, 1f);
            }

            if (slotStates[i] != null)
            {
                slotStates[i].text = !unlocked
                    ? "LOCK"
                    : isEquipped
                        ? "EQP"
                        : linked ? "LINK" : string.Empty;
                slotStates[i].color = isEquipped
                    ? accentCyan
                    : linked
                        ? accentPink
                        : new Color(0.50f, 0.54f, 0.62f, 1f);
            }

            if (slotAccents[i] != null)
            {
                slotAccents[i].enabled = unlocked && occupied && (isEquipped || linked);
                slotAccents[i].color = isEquipped
                    ? accentCyan
                    : accentPink;
            }

            if (lockMarks[i] != null)
                lockMarks[i].SetActive(!unlocked);
        }
    }

    private bool IsSlotLinked(int slotIndex)
    {
        if (gridSynergy == null)
            return false;

        IReadOnlyList<BattleGridSynergyLink> links = gridSynergy.ActiveLinks;
        for (int i = 0; i < links.Count; i++)
        {
            BattleGridSynergyLink link = links[i];
            if (link != null && (link.slotA == slotIndex || link.slotB == slotIndex))
                return true;
        }
        return false;
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return rect;
    }

    private static Image CreateImage(Transform parent, string name, Vector2 size)
    {
        RectTransform rect = CreateRect(parent, name, size);
        return rect.gameObject.AddComponent<Image>();
    }

    private static Text CreateText(Transform parent, string value, int fontSize, FontStyle style, TextAnchor alignment, Color color)
    {
        RectTransform rect = CreateRect(parent, "Text", Vector2.zero);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
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
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
