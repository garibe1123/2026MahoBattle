using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PACK / Full Grid의 기본 아이템 시각 정책만 담당합니다.
///
/// 소유권 규칙:
/// - Reward 카드 외형은 BattleRewardCardActionController만 소유합니다.
/// - Mini PACK의 InteractionSelectionFrame은 BattleInventoryInteractionController가 소유합니다.
/// - Full Grid의 UnifiedSelectionFrame은 BattleUnifiedInventoryInspectController가 소유합니다.
/// - 최근 변경 상태의 수명은 BattlePackChangeFeedbackController가 소유하고,
///   이 클래스는 기존 태그를 숨긴 뒤 바깥쪽 Negative Stroke로만 표현합니다.
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
    private const string RecentChangeMarkerName = "PackRecentChangeMarker";
    private const string RecentChangeStrokeName = "PackRecentChangeNegativeStroke";

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

            PushSelectionFrameBehind(slotRect, "InteractionSelectionFrame");

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
            ApplyLevelLabel(slotRect, runtimeSlot, focused);
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

            // Selection frame은 배경 강조만 담당하고 아이콘/텍스트보다 항상 뒤에 둡니다.
            // 투명 Image + Outline 조합이 선택색 전체 Quad처럼 보이더라도 정보 레이어를 가리지 않습니다.
            PushSelectionFrameBehind(slotRect, "InteractionFullSelectionFrame");
            PushSelectionFrameBehind(slotRect, "UnifiedSelectionFrame");

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
            ApplyLevelLabel(slotRect, runtimeSlot, focused);
            ApplyGridTextTone(slotRect, runtimeSlot, focused, i == equipmentSystem.EquippedSlotIndex);
            ApplyRecentChangeStroke(slotRect);
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

        // 선택 중에는 Monochrome을 해제하고 원본 아이콘을 100% 불투명으로 유지합니다.
        icon.material = focused ? null : monochromeMaterial;
        icon.color = focused
            ? Color.white
            : new Color(0.82f, 0.82f, 0.82f, 0.76f);
    }

    private void ApplyLevelLabel(RectTransform slotRect, BattleEquipmentSlot runtimeSlot, bool focused)
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
            text.color = focused ? black : tier;
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

            if (focused)
            {
                // 노란 선택 배경에서는 모든 슬롯 정보가 검정으로 읽히게 합니다.
                text.color = black;
            }
            else if (value.Contains("EQUIPPED"))
            {
                text.color = equipped ? equippedAccent : muted;
            }
            else
            {
                text.color = muted;
            }
        }
    }

    private void ApplyRecentChangeStroke(RectTransform slotRect)
    {
        if (slotRect == null)
            return;

        Transform marker = slotRect.Find(RecentChangeMarkerName);
        RectTransform stroke = slotRect.Find(RecentChangeStrokeName) as RectTransform;

        if (marker == null)
        {
            if (stroke != null && stroke.gameObject.activeSelf)
                stroke.gameObject.SetActive(false);
            return;
        }

        Image markerImage = marker.GetComponent<Image>();
        Outline markerOutline = marker.GetComponent<Outline>();
        Color sourceColor = markerImage != null ? markerImage.color : equippedAccent;

        // 기존 우측 상단 다이아 태그는 상태 보존용으로만 남기고 화면에는 그리지 않습니다.
        if (markerImage != null)
            markerImage.enabled = false;
        if (markerOutline != null)
            markerOutline.enabled = false;

        if (stroke == null)
        {
            GameObject strokeObject = new(RecentChangeStrokeName);
            strokeObject.transform.SetParent(slotRect, false);
            stroke = strokeObject.AddComponent<RectTransform>();
            stroke.anchorMin = Vector2.zero;
            stroke.anchorMax = Vector2.one;
            stroke.offsetMin = new Vector2(-6f, -6f);
            stroke.offsetMax = new Vector2(6f, 6f);
        }

        if (!stroke.gameObject.activeSelf)
            stroke.gameObject.SetActive(true);

        // 이전 버전의 투명 Image + Outline은 사각형 전체를 색으로 덮을 수 있으므로 즉시 비활성화합니다.
        Image legacyFill = stroke.GetComponent<Image>();
        if (legacyFill != null)
            legacyFill.enabled = false;
        Outline legacyOutline = stroke.GetComponent<Outline>();
        if (legacyOutline != null)
            legacyOutline.enabled = false;

        // 실제 정보 레이어 위에는 4개의 얇은 Edge만 렌더합니다.
        stroke.SetAsLastSibling();
        float pulse01 = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.4f);
        Color negative = ResolveNegativeStrokeColor(sourceColor);
        negative.a = Mathf.Lerp(0.82f, 1f, pulse01);
        float thickness = Mathf.Lerp(3.4f, 4.8f, pulse01);
        ApplyEdgeStroke(stroke, negative, thickness);
    }

    private static void ApplyEdgeStroke(RectTransform root, Color color, float thickness)
    {
        if (root == null)
            return;

        thickness = Mathf.Max(1f, thickness);
        ConfigureEdge(root, "Top", color,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -thickness), Vector2.zero);
        ConfigureEdge(root, "Bottom", color,
            Vector2.zero, new Vector2(1f, 0f),
            Vector2.zero, new Vector2(0f, thickness));
        ConfigureEdge(root, "Left", color,
            Vector2.zero, new Vector2(0f, 1f),
            Vector2.zero, new Vector2(thickness, 0f));
        ConfigureEdge(root, "Right", color,
            new Vector2(1f, 0f), Vector2.one,
            new Vector2(-thickness, 0f), Vector2.zero);
    }

    private static void ConfigureEdge(
        RectTransform root,
        string edgeName,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        RectTransform edge = root.Find(edgeName) as RectTransform;
        if (edge == null)
        {
            GameObject edgeObject = new(edgeName);
            edgeObject.transform.SetParent(root, false);
            edge = edgeObject.AddComponent<RectTransform>();
            Image image = edgeObject.AddComponent<Image>();
            image.raycastTarget = false;
        }

        edge.anchorMin = anchorMin;
        edge.anchorMax = anchorMax;
        edge.offsetMin = offsetMin;
        edge.offsetMax = offsetMax;
        edge.localScale = Vector3.one;
        edge.localRotation = Quaternion.identity;

        Image edgeImage = edge.GetComponent<Image>();
        if (edgeImage != null)
        {
            edgeImage.enabled = true;
            edgeImage.color = color;
            edgeImage.raycastTarget = false;
        }
    }

    private static Color ResolveNegativeStrokeColor(Color source)
    {
        Color inverted = new(1f - source.r, 1f - source.g, 1f - source.b, 1f);
        Color.RGBToHSV(inverted, out float h, out float s, out float v);

        // 무채색의 단순 반전은 어두운 슬롯 위에서 다시 묻힐 수 있으므로 밝은 Negative로 보정합니다.
        if (s < 0.18f)
            return new Color(0.96f, 0.96f, 0.96f, 1f);

        s = Mathf.Max(0.72f, s);
        v = Mathf.Max(0.90f, v);
        Color result = Color.HSVToRGB(h, s, v);
        result.a = 1f;
        return result;
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

    private static void PushSelectionFrameBehind(RectTransform slotRect, string frameName)
    {
        if (slotRect == null)
            return;

        Transform frame = slotRect.Find(frameName);
        if (frame != null && frame.GetSiblingIndex() != 0)
            frame.SetAsFirstSibling();
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
