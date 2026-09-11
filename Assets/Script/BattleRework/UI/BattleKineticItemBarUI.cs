using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

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
    [SerializeField] private Color paperColor = new(0.92f, 0.88f, 0.74f, 1f);
    [SerializeField] private Color emptyCellColor = new(0.19f, 0.18f, 0.22f, 0.96f);
    [SerializeField] private Color occupiedCellColor = new(0.060f, 0.055f, 0.080f, 0.99f);
    [SerializeField] private Color lockedColor = new(0.065f, 0.060f, 0.080f, 0.92f);
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
    private readonly GameObject[] lockMarks = new GameObject[SlotCount];

    private bool equipmentSubscribed;
    private bool gridSubscribed;

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
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        ResolveReferences();
        Subscribe();
        EnsureUi();
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
        if (!equipmentSubscribed && equipmentSystem != null)
        {
            equipmentSystem.InventoryChanged += Refresh;
            equipmentSystem.SlotCapacityChanged += HandleCapacityChanged;
            equipmentSystem.EquippedSlotChanged += HandleEquippedChanged;
            equipmentSubscribed = true;
        }

        if (!gridSubscribed && gridSynergy != null)
        {
            gridSynergy.GridSynergiesChanged += Refresh;
            gridSubscribed = true;
        }
    }

    private void Unsubscribe()
    {
        if (equipmentSubscribed && equipmentSystem != null)
        {
            equipmentSystem.InventoryChanged -= Refresh;
            equipmentSystem.SlotCapacityChanged -= HandleCapacityChanged;
            equipmentSystem.EquippedSlotChanged -= HandleEquippedChanged;
        }
        if (gridSubscribed && gridSynergy != null)
            gridSynergy.GridSynergiesChanged -= Refresh;

        equipmentSubscribed = false;
        gridSubscribed = false;
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
        outline.effectColor = accentPink;
        outline.effectDistance = new Vector2(4f, -4f);

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
        RectTransform tag = CreateRect(root, "PackHeaderTag", new Vector2(126f, 40f));
        tag.anchorMin = tag.anchorMax = new Vector2(0f, 1f);
        tag.pivot = new Vector2(0f, 1f);
        tag.anchoredPosition = new Vector2(10f, -5f);
        tag.localRotation = Quaternion.Euler(0f, 0f, -4f);
        Image tagImage = tag.gameObject.AddComponent<Image>();
        tagImage.color = accentYellow;
        tagImage.raycastTarget = false;

        Text title = CreateText(tag, "PACK", 18, FontStyle.Bold, TextAnchor.MiddleCenter, inkColor);
        Stretch(title.rectTransform);

        capacityText = CreateText(root, "3 / 9", 12, FontStyle.Bold, TextAnchor.MiddleRight, paperColor);
        capacityText.rectTransform.anchorMin = capacityText.rectTransform.anchorMax = new Vector2(1f, 1f);
        capacityText.rectTransform.pivot = new Vector2(1f, 1f);
        capacityText.rectTransform.sizeDelta = new Vector2(72f, 24f);
        capacityText.rectTransform.anchoredPosition = new Vector2(-12f, -8f);

        Text hint = CreateText(root, "TAB / LB", 9, FontStyle.Bold, TextAnchor.MiddleRight, accentCyan);
        hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(1f, 1f);
        hint.rectTransform.pivot = new Vector2(1f, 1f);
        hint.rectTransform.sizeDelta = new Vector2(72f, 20f);
        hint.rectTransform.anchoredPosition = new Vector2(-12f, -27f);
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
            cellOutline.effectColor = Color.black;
            cellOutline.effectDistance = new Vector2(3f, -3f);
            slotOutlines[i] = cellOutline;

            Image icon = CreateImage(slot, "Icon", new Vector2(64f, 64f));
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            icon.rectTransform.anchoredPosition = Vector2.zero;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            slotIcons[i] = icon;

            Text grade = CreateText(slot, string.Empty, 10, FontStyle.Bold, TextAnchor.UpperRight, accentYellow);
            SetAnchors(grade.rectTransform, new Vector2(0.56f, 0.68f), new Vector2(0.93f, 0.94f));
            slotGrades[i] = grade;

            RectTransform accent = CreateRect(slot, "CellAccent", new Vector2(6f, cell - 10f));
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
                    ? new Color(0f, 0f, 0f, 0.65f)
                    : new Color(0f, 0f, 0f, 0.92f);
                slotOutlines[i].effectDistance = new Vector2(3f, -3f);
            }

            if (slotRects[i] != null)
                slotRects[i].localScale = Vector3.one;

            if (slotIcons[i] != null)
            {
                slotIcons[i].sprite = occupied ? equipment.icon : null;
                slotIcons[i].enabled = unlocked && occupied && equipment.icon != null;
                slotIcons[i].color = Color.white;
            }

            if (slotGrades[i] != null)
            {
                slotGrades[i].text = unlocked && occupied ? $"G{slot.grade}" : string.Empty;
                slotGrades[i].color = paperColor;
            }

            if (slotAccents[i] != null)
            {
                slotAccents[i].enabled = unlocked && occupied;
                slotAccents[i].color = isEquipped
                    ? accentYellow
                    : linked
                        ? accentCyan
                        : new Color(accentPink.r, accentPink.g, accentPink.b, 0.48f);
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

public static class BattleKineticItemBarAutoInstaller
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
        EditorApplication.delayCall += EnsureEditorComponents;
    }

    private static void EnsureEditorComponents()
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
            if (manager.GetComponent<BattleKineticItemBarUI>() != null)
                continue;

            Undo.AddComponent<BattleKineticItemBarUI>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponents()
    {
        BattleSceneManager[] managers = Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.GetComponent<BattleKineticItemBarUI>() == null)
                manager.gameObject.AddComponent<BattleKineticItemBarUI>();
        }
    }
}
