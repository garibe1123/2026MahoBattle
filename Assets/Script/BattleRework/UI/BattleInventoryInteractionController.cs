using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

internal enum BattleInventorySurface
{
    MiniPack,
    ExpandedGrid,
    RewardPack
}

/// <summary>
/// 3x3 장비 가방의 실제 편집 인터랙션을 담당합니다.
/// - 기존 장비를 다른 3x3 슬롯으로 Drag/Drop해 자리 교환
/// - Reward 카드를 기존 좌하단 PACK 3x3에 직접 Drop해 획득
/// - Reward 중 슬롯 클릭 -> 다른 슬롯 클릭으로 자리 교환
/// - 장비를 TRASH에 Drag/Drop하거나 선택 후 TRASH 클릭으로 폐기
/// - Reward Show에서는 중복 RewardLoadoutStrip 대신 기존 PACK을 표시하고 화면 포커스를 강화
///
/// 장비 SO / Sprite / Scene 직렬화 데이터는 변경하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30750)]
public sealed class BattleInventoryInteractionController : MonoBehaviour
{
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const int OverlaySortingOrder = 1550;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleHUD battleHud;

    [Header("Reward Screen Focus")]
    [SerializeField, Range(0f, 0.8f)] private float rewardFieldDimAlpha = 0.46f;
    [SerializeField, Range(1f, 1.15f)] private float rewardScreenScale = 1.055f;
    [SerializeField] private Vector2 rewardScreenAnchor = new(0.57f, 0.66f);
    [SerializeField] private Vector2 rewardScreenSize = new(1240f, 640f);

    [Header("Theme")]
    [SerializeField] private Color inkColor = new(0.035f, 0.030f, 0.055f, 0.995f);
    [SerializeField] private Color paperColor = new(0.94f, 0.90f, 0.76f, 1f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color accentCyan = new(0.15f, 0.88f, 0.92f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.52f, 1f);

    private Canvas interactionCanvas;
    private RectTransform interactionRoot;
    private RectTransform dragGhostRoot;
    private Image dragGhostIcon;
    private RectTransform trashRoot;
    private Image trashBack;
    private Text trashLabel;

    private RectTransform miniPackRoot;
    private CanvasGroup miniPackGroup;
    private Vector2 miniPackDefaultPosition;
    private Vector3 miniPackDefaultScale = Vector3.one;
    private bool miniPackDefaultsCaptured;

    private readonly GameObject[] miniSelectionFrames = new GameObject[SlotCount];

    private int selectedRewardSlot = -1;
    private int draggingSlot = -1;
    private bool dragConsumed;
    private float nextResolveTime;

    private FieldInfo pendingRewardIndexField;

    private void Awake()
    {
        ResolveReferences();
        EnsureOverlayCanvas();
        CacheHudReflection();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureOverlayCanvas();
        CacheHudReflection();
        nextResolveTime = 0f;
    }

    private void Update()
    {
        ResolveReferences();
        EnsureOverlayCanvas();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.20f;
            ResolveMiniPack();
            InstallSlotTargets();
        }

        bool reward = IsReward();
        if (reward)
        {
            ApplyRewardScreenFocus();
            ShowExistingMiniPackForReward();
        }
        else
        {
            RestoreMiniPackAfterReward();
            selectedRewardSlot = -1;
        }

        UpdateSelectionFrames();
        UpdateTrashVisibility();
    }

    private void OnDisable()
    {
        HideDragVisuals();
        RestoreMiniPackAfterReward();
        selectedRewardSlot = -1;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (battleHud == null)
        {
            battleHud = FindFirstObjectByType<BattleHUD>();
            CacheHudReflection();
        }
    }

    private void CacheHudReflection()
    {
        if (battleHud == null)
            return;
        pendingRewardIndexField ??= typeof(BattleHUD).GetField("pendingRewardIndex", PrivateInstance);
    }

    private bool IsReward()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward;
    }

    private bool IsCombat()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Combat;
    }

    private void ResolveMiniPack()
    {
        if (miniPackRoot == null)
            miniPackRoot = FindRect("BackpackMiniGrid");
        if (miniPackRoot == null)
            return;

        if (!miniPackDefaultsCaptured)
        {
            miniPackDefaultPosition = miniPackRoot.anchoredPosition;
            miniPackDefaultScale = miniPackRoot.localScale;
            miniPackDefaultsCaptured = true;
        }

        if (miniPackGroup == null)
            miniPackGroup = miniPackRoot.GetComponent<CanvasGroup>();

        Canvas canvas = miniPackRoot.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();
    }

    private void InstallSlotTargets()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            InstallSlotTarget(FindRect($"BackpackCell_{i}"), i, BattleInventorySurface.MiniPack);
            InstallSlotTarget(FindRect($"GridSlot_{i}"), i, BattleInventorySurface.ExpandedGrid);
            InstallSlotTarget(FindRect($"RewardLoadoutSlot_{i + 1}"), i, BattleInventorySurface.RewardPack);
        }
    }

    private void InstallSlotTarget(RectTransform rect, int index, BattleInventorySurface surface)
    {
        if (rect == null)
            return;

        Image image = rect.GetComponent<Image>();
        if (image != null)
            image.raycastTarget = true;

        Canvas canvas = rect.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        BattleInventorySlotPointer pointer = rect.GetComponent<BattleInventorySlotPointer>();
        if (pointer == null)
            pointer = rect.gameObject.AddComponent<BattleInventorySlotPointer>();
        pointer.Configure(this, index, surface);

        if (surface == BattleInventorySurface.MiniPack)
            EnsureMiniSelectionFrame(rect, index);
    }

    private void EnsureMiniSelectionFrame(RectTransform slot, int index)
    {
        if (index < 0 || index >= miniSelectionFrames.Length || miniSelectionFrames[index] != null)
            return;

        Transform existing = slot.Find("InteractionSelectionFrame");
        if (existing != null)
        {
            miniSelectionFrames[index] = existing.gameObject;
            return;
        }

        RectTransform frame = CreateRect(slot, "InteractionSelectionFrame", Vector2.zero);
        Stretch(frame);
        Image image = frame.gameObject.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = false;
        Outline outline = frame.gameObject.AddComponent<Outline>();
        outline.effectColor = accentPink;
        outline.effectDistance = new Vector2(5f, -5f);
        frame.SetAsLastSibling();
        frame.gameObject.SetActive(false);
        miniSelectionFrames[index] = frame.gameObject;
    }

    internal bool BeginSlotDrag(int slotIndex, BattleInventorySurface surface, PointerEventData eventData)
    {
        if (equipmentSystem == null || !equipmentSystem.IsSlotUnlocked(slotIndex))
            return false;
        if (slotIndex < 0 || slotIndex >= equipmentSystem.Slots.Count)
            return false;

        BattleEquipmentSlot slot = equipmentSystem.Slots[slotIndex];
        if (slot == null || slot.equipment == null)
            return false;

        selectedRewardSlot = IsReward() ? slotIndex : selectedRewardSlot;
        draggingSlot = slotIndex;
        dragConsumed = false;

        EnsureOverlayCanvas();
        if (dragGhostRoot != null)
        {
            dragGhostRoot.gameObject.SetActive(true);
            dragGhostRoot.position = eventData.position;
        }
        if (dragGhostIcon != null)
        {
            dragGhostIcon.sprite = slot.equipment.icon;
            dragGhostIcon.enabled = slot.equipment.icon != null;
        }

        UpdateTrashVisibility();
        return true;
    }

    internal void UpdateSlotDrag(PointerEventData eventData)
    {
        if (draggingSlot < 0 || dragGhostRoot == null || eventData == null)
            return;
        dragGhostRoot.position = eventData.position;
    }

    internal void EndSlotDrag()
    {
        draggingSlot = -1;
        dragConsumed = false;
        if (dragGhostRoot != null)
            dragGhostRoot.gameObject.SetActive(false);
        UpdateTrashVisibility();
    }

    internal void HandleSlotDrop(int targetIndex, PointerEventData eventData)
    {
        if (equipmentSystem == null || eventData == null || !equipmentSystem.IsSlotUnlocked(targetIndex))
            return;

        // Reward 카드 -> 기존 좌하단 PACK 3x3 직접 Drop.
        RewardPrizeDrag rewardDrag = eventData.pointerDrag != null
            ? eventData.pointerDrag.GetComponent<RewardPrizeDrag>()
            : null;
        if (rewardDrag != null && IsReward())
        {
            if (runManager != null && runManager.PlaceRewardIntoSlot(rewardDrag.RewardIndex, targetIndex))
            {
                selectedRewardSlot = -1;
                dragConsumed = true;
            }
            return;
        }

        BattleInventorySlotPointer source = eventData.pointerDrag != null
            ? eventData.pointerDrag.GetComponent<BattleInventorySlotPointer>()
            : null;
        if (source == null || !source.IsDragging)
            return;

        int sourceIndex = source.SlotIndex;
        if (!equipmentSystem.IsSlotUnlocked(sourceIndex))
            return;

        if (sourceIndex != targetIndex && equipmentSystem.SwapSlots(sourceIndex, targetIndex))
        {
            selectedRewardSlot = IsReward() ? targetIndex : -1;
            dragConsumed = true;
        }
    }

    internal void HandleSlotClick(int slotIndex, BattleInventorySurface surface, PointerEventData.InputButton button)
    {
        if (button != PointerEventData.InputButton.Left || equipmentSystem == null || !equipmentSystem.IsSlotUnlocked(slotIndex))
            return;

        if (IsReward())
        {
            BattleEquipmentSlot clicked = slotIndex < equipmentSystem.Slots.Count ? equipmentSystem.Slots[slotIndex] : null;

            if (selectedRewardSlot < 0)
            {
                if (clicked != null && clicked.equipment != null)
                    selectedRewardSlot = slotIndex;
                return;
            }

            if (selectedRewardSlot == slotIndex)
            {
                selectedRewardSlot = -1;
                return;
            }

            if (equipmentSystem.SwapSlots(selectedRewardSlot, slotIndex))
                selectedRewardSlot = -1;
            return;
        }

        if (IsCombat())
        {
            BattleEquipmentSlot slot = slotIndex < equipmentSystem.Slots.Count ? equipmentSystem.Slots[slotIndex] : null;
            if (slot != null && slot.equipment != null && slot.equipment.shootingData != null)
                equipmentSystem.EquipSlot(slotIndex);
        }
    }

    internal void HandleTrashDrop(PointerEventData eventData)
    {
        if (equipmentSystem == null || eventData == null)
            return;

        BattleInventorySlotPointer source = eventData.pointerDrag != null
            ? eventData.pointerDrag.GetComponent<BattleInventorySlotPointer>()
            : null;
        if (source == null || !source.IsDragging)
            return;

        int sourceIndex = source.SlotIndex;
        if (!HasItem(sourceIndex))
            return;

        if (equipmentSystem.DiscardSlot(sourceIndex))
        {
            if (selectedRewardSlot == sourceIndex)
                selectedRewardSlot = -1;
            dragConsumed = true;
        }
    }

    internal void HandleTrashClick(PointerEventData.InputButton button)
    {
        if (button != PointerEventData.InputButton.Left || equipmentSystem == null)
            return;
        if (!IsReward() || !HasItem(selectedRewardSlot))
            return;

        equipmentSystem.DiscardSlot(selectedRewardSlot);
        selectedRewardSlot = -1;
    }

    internal void HandleTrashHover(bool hovered)
    {
        if (trashBack == null)
            return;
        trashBack.color = hovered
            ? new Color(accentPink.r, accentPink.g, accentPink.b, 0.98f)
            : inkColor;
        if (trashLabel != null)
            trashLabel.color = hovered ? inkColor : accentPink;
    }

    private bool HasItem(int index)
    {
        return equipmentSystem != null && index >= 0 && index < equipmentSystem.Slots.Count &&
               equipmentSystem.IsSlotUnlocked(index) && equipmentSystem.Slots[index] != null &&
               equipmentSystem.Slots[index].equipment != null;
    }

    private void EnsureOverlayCanvas()
    {
        if (interactionCanvas != null)
            return;

        EnsureEventSystem();

        GameObject canvasObject = new("BattleInventoryInteractionCanvas");
        canvasObject.transform.SetParent(transform, false);
        interactionCanvas = canvasObject.AddComponent<Canvas>();
        interactionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        interactionCanvas.overrideSorting = true;
        interactionCanvas.sortingOrder = OverlaySortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        interactionRoot = CreateRect(canvasObject.transform, "InteractionRoot", Vector2.zero);
        Stretch(interactionRoot);

        dragGhostRoot = CreateRect(interactionRoot, "InventoryDragGhost", new Vector2(84f, 84f));
        Image ghostBack = dragGhostRoot.gameObject.AddComponent<Image>();
        ghostBack.color = new Color(inkColor.r, inkColor.g, inkColor.b, 0.94f);
        ghostBack.raycastTarget = false;
        Outline ghostOutline = dragGhostRoot.gameObject.AddComponent<Outline>();
        ghostOutline.effectColor = accentYellow;
        ghostOutline.effectDistance = new Vector2(4f, -4f);

        dragGhostIcon = CreateImage(dragGhostRoot, "Icon", new Vector2(66f, 66f));
        dragGhostIcon.rectTransform.anchorMin = dragGhostIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        dragGhostIcon.rectTransform.anchoredPosition = Vector2.zero;
        dragGhostIcon.raycastTarget = false;
        dragGhostRoot.gameObject.SetActive(false);

        trashRoot = CreateRect(interactionRoot, "InventoryTrash", new Vector2(168f, 66f));
        trashRoot.anchorMin = trashRoot.anchorMax = new Vector2(0f, 0f);
        trashRoot.pivot = new Vector2(0f, 0f);
        trashRoot.anchoredPosition = new Vector2(265f, 28f);
        trashRoot.localRotation = Quaternion.Euler(0f, 0f, -2.5f);
        trashBack = trashRoot.gameObject.AddComponent<Image>();
        trashBack.color = inkColor;
        trashBack.raycastTarget = true;
        Outline trashOutline = trashRoot.gameObject.AddComponent<Outline>();
        trashOutline.effectColor = accentPink;
        trashOutline.effectDistance = new Vector2(4f, -4f);

        trashLabel = CreateText(trashRoot, "×  TRASH", 16, FontStyle.Bold, TextAnchor.MiddleCenter, accentPink);
        Stretch(trashLabel.rectTransform);

        BattleInventoryTrashDropTarget trashTarget = trashRoot.gameObject.AddComponent<BattleInventoryTrashDropTarget>();
        trashTarget.Configure(this);
        trashRoot.gameObject.SetActive(false);
    }

    private void UpdateTrashVisibility()
    {
        if (trashRoot == null)
            return;

        bool fullBoardOpen = false;
        RectTransform full = FindRect("LoadoutSwitchFull");
        if (full != null)
        {
            CanvasGroup group = full.GetComponent<CanvasGroup>();
            fullBoardOpen = group != null && group.alpha > 0.08f;
        }

        bool visible = IsReward() || draggingSlot >= 0 || (IsCombat() && fullBoardOpen);
        if (trashRoot.gameObject.activeSelf != visible)
            trashRoot.gameObject.SetActive(visible);

        if (visible)
        {
            trashRoot.anchorMin = trashRoot.anchorMax = IsReward() ? new Vector2(0f, 0f) : new Vector2(1f, 0f);
            trashRoot.pivot = IsReward() ? new Vector2(0f, 0f) : new Vector2(1f, 0f);
            trashRoot.anchoredPosition = IsReward() ? new Vector2(272f, 34f) : new Vector2(-44f, 42f);
        }
    }

    private void ShowExistingMiniPackForReward()
    {
        ResolveMiniPack();
        if (miniPackRoot == null)
            return;

        if (miniPackGroup != null)
        {
            miniPackGroup.alpha = 1f;
            miniPackGroup.blocksRaycasts = !BattlePauseController.IsPaused;
            miniPackGroup.interactable = !BattlePauseController.IsPaused;
        }

        miniPackRoot.anchoredPosition = new Vector2(32f, 34f);
        miniPackRoot.localScale = Vector3.one * 1.10f;

        // 기존 Reward 전용 중복 3x3 strip은 숨기고 동일 PACK을 직접 Drop Target으로 사용합니다.
        RectTransform oldStrip = FindRect("RewardLoadoutStrip");
        if (oldStrip != null && oldStrip.gameObject.activeSelf)
            oldStrip.gameObject.SetActive(false);
    }

    private void RestoreMiniPackAfterReward()
    {
        if (miniPackRoot == null || !miniPackDefaultsCaptured)
            return;

        miniPackRoot.anchoredPosition = miniPackDefaultPosition;
        miniPackRoot.localScale = miniPackDefaultScale;

        if (miniPackGroup != null && !IsCombat())
        {
            miniPackGroup.blocksRaycasts = false;
            miniPackGroup.interactable = false;
        }
    }

    private void ApplyRewardScreenFocus()
    {
        RectTransform filter = FindRect("FieldBroadcastFilter");
        if (filter != null)
        {
            Image filterImage = filter.GetComponent<Image>();
            if (filterImage != null)
                filterImage.color = new Color(0.005f, 0.006f, 0.014f, rewardFieldDimAlpha);
        }

        RectTransform screen = FindRect("PrizeSelectionScreen");
        if (screen != null)
        {
            screen.anchorMin = screen.anchorMax = rewardScreenAnchor;
            screen.sizeDelta = rewardScreenSize;
            screen.localScale = Vector3.one * rewardScreenScale;
            screen.localRotation = Quaternion.Euler(0f, 0f, -0.65f);

            Image image = screen.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = inkColor;
            }

            Outline outline = screen.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = accentYellow;
                outline.effectDistance = new Vector2(6f, -6f);
            }
        }

        RectTransform inner = FindRectUnder(screen, "ScreenInner");
        if (inner != null)
        {
            Image innerImage = inner.GetComponent<Image>();
            if (innerImage != null)
            {
                innerImage.sprite = null;
                innerImage.type = Image.Type.Simple;
                innerImage.color = new Color(0.052f, 0.047f, 0.070f, 0.995f);
            }

            Text[] texts = inner.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                Text text = texts[i];
                if (text == null)
                    continue;

                string value = text.text ?? string.Empty;
                if (value.Contains("SELECT  •  DRAG  •  DROP"))
                    text.text = "SELECT  •  DRAG TO PACK  •  REORDER";
                else if (value.Contains("loadout strip below"))
                    text.text = "Drag the prize into the PACK at lower left. Drag equipped items to reorder; use TRASH to discard.";
            }
        }

        StylePrizeCardsForFocus();
    }

    private void StylePrizeCardsForFocus()
    {
        RectTransform root = FindRect("PrizeChoices");
        if (root == null)
            return;

        int selected = GetPendingRewardIndex();
        for (int i = 0; i < root.childCount; i++)
        {
            RectTransform card = root.GetChild(i) as RectTransform;
            if (card == null)
                continue;

            bool isSelected = i == selected;
            Image image = card.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = isSelected ? accentYellow : new Color(0.042f, 0.038f, 0.060f, 0.995f);
            }

            Outline outline = card.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = isSelected ? accentPink : new Color(paperColor.r, paperColor.g, paperColor.b, 0.52f);
                outline.effectDistance = isSelected ? new Vector2(6f, -6f) : new Vector2(3f, -3f);
            }

            Text[] texts = card.GetComponentsInChildren<Text>(true);
            for (int t = 0; t < texts.Length; t++)
            {
                Text text = texts[t];
                if (text == null)
                    continue;

                string value = text.text ?? string.Empty;
                if (isSelected)
                    text.color = value.Contains("CLICK") || value.Contains("DRAG") ? accentPink : inkColor;
                else if (value.Contains("CLICK") || value.Contains("DRAG"))
                    text.color = accentPink;
                else if (text.fontSize >= 13)
                    text.color = paperColor;
                else
                    text.color = accentCyan;
            }
        }
    }

    private int GetPendingRewardIndex()
    {
        if (battleHud == null || pendingRewardIndexField == null)
            return -1;
        object value = pendingRewardIndexField.GetValue(battleHud);
        return value is int index ? index : -1;
    }

    private void UpdateSelectionFrames()
    {
        for (int i = 0; i < miniSelectionFrames.Length; i++)
        {
            GameObject frame = miniSelectionFrames[i];
            if (frame != null)
                frame.SetActive(IsReward() && selectedRewardSlot == i);
        }
    }

    private void HideDragVisuals()
    {
        draggingSlot = -1;
        dragConsumed = false;
        if (dragGhostRoot != null)
            dragGhostRoot.gameObject.SetActive(false);
        if (trashRoot != null)
            trashRoot.gameObject.SetActive(false);
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }
        return null;
    }

    private static RectTransform FindRectUnder(RectTransform parent, string childName)
    {
        if (parent == null)
            return null;
        Transform child = parent.Find(childName);
        return child as RectTransform;
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
        Image image = rect.gameObject.AddComponent<Image>();
        image.preserveAspect = true;
        return image;
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

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject go = new("BattleInventoryEventSystem");
        DontDestroyOnLoad(go);
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }
}

internal sealed class BattleInventorySlotPointer : MonoBehaviour,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler,
    IDropHandler,
    IPointerClickHandler
{
    private BattleInventoryInteractionController owner;
    private int slotIndex;
    private BattleInventorySurface surface;
    private bool dragging;

    public int SlotIndex => slotIndex;
    public bool IsDragging => dragging;

    public void Configure(BattleInventoryInteractionController controller, int index, BattleInventorySurface slotSurface)
    {
        owner = controller;
        slotIndex = index;
        surface = slotSurface;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragging = owner != null && owner.BeginSlotDrag(slotIndex, surface, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragging)
            owner?.UpdateSlotDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (dragging)
            owner?.EndSlotDrag();
        dragging = false;
    }

    public void OnDrop(PointerEventData eventData)
    {
        owner?.HandleSlotDrop(slotIndex, eventData);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        owner?.HandleSlotClick(slotIndex, surface, eventData.button);
    }
}

internal sealed class BattleInventoryTrashDropTarget : MonoBehaviour,
    IDropHandler,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    private BattleInventoryInteractionController owner;

    public void Configure(BattleInventoryInteractionController controller)
    {
        owner = controller;
    }

    public void OnDrop(PointerEventData eventData)
    {
        owner?.HandleTrashDrop(eventData);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        owner?.HandleTrashHover(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.HandleTrashHover(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        owner?.HandleTrashClick(eventData.button);
    }
}

public static class BattleInventoryInteractionAutoInstaller
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

            if (manager.GetComponent<BattleInventoryInteractionController>() != null)
                continue;

            Undo.AddComponent<BattleInventoryInteractionController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponent()
    {
        BattleSceneManager[] managers = Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.GetComponent<BattleInventoryInteractionController>() == null)
                manager.gameObject.AddComponent<BattleInventoryInteractionController>();
        }
    }
}
