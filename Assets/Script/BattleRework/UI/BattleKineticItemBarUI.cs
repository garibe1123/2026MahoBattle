using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 전투 중 9개 장비 슬롯을 항상 읽을 수 있게 보여주는 Kinetic Item Bar입니다.
/// 기존 BattleHUD의 일자형 EquipmentDock은 BattleKineticLoadoutUI가 Combat에서 숨기고,
/// 이 바가 같은 장비 데이터를 새로운 고대비/비대칭 스타일로 표시합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(29950)]
public sealed class BattleKineticItemBarUI : MonoBehaviour
{
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const int CanvasSortingOrder = 770;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;

    [Header("Kinetic Item Bar")]
    [SerializeField] private Color inkColor = new(0.035f, 0.030f, 0.055f, 0.98f);
    [SerializeField] private Color paperColor = new(0.94f, 0.90f, 0.76f, 1f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color accentCyan = new(0.15f, 0.88f, 0.92f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.52f, 1f);
    [SerializeField] private Color lockedColor = new(0.10f, 0.09f, 0.14f, 0.94f);
    [SerializeField, Range(1f, 1.2f)] private float equippedScale = 1.08f;
    [SerializeField, Min(1f)] private float fadeSharpness = 14f;

    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform root;
    private Text currentName;
    private Text currentPrompt;

    private readonly RectTransform[] slotRects = new RectTransform[SlotCount];
    private readonly Image[] slotBackgrounds = new Image[SlotCount];
    private readonly Image[] slotIcons = new Image[SlotCount];
    private readonly Image[] slotAccents = new Image[SlotCount];
    private readonly Text[] slotGrades = new Text[SlotCount];
    private readonly Text[] slotStates = new Text[SlotCount];

    private bool subscribed;

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

        bool combat = runManager != null && runManager.RunActive && runManager.State == BattleRunState.Combat;
        if (group != null)
        {
            float t = 1f - Mathf.Exp(-Mathf.Max(1f, fadeSharpness) * Time.unscaledDeltaTime);
            group.alpha = Mathf.Lerp(group.alpha, combat ? 1f : 0f, t);
        }
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
    }

    private void Subscribe()
    {
        if (subscribed || equipmentSystem == null)
            return;

        equipmentSystem.InventoryChanged += Refresh;
        equipmentSystem.SlotCapacityChanged += HandleCapacityChanged;
        equipmentSystem.EquippedSlotChanged += HandleEquippedChanged;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || equipmentSystem == null)
            return;

        equipmentSystem.InventoryChanged -= Refresh;
        equipmentSystem.SlotCapacityChanged -= HandleCapacityChanged;
        equipmentSystem.EquippedSlotChanged -= HandleEquippedChanged;
        subscribed = false;
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

        GameObject canvasObject = new("BattleKineticItemBarCanvas");
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = CanvasSortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root = CreateRect(canvas.transform, "KineticItemBar", new Vector2(1100f, 122f));
        root.anchorMin = root.anchorMax = new Vector2(0f, 0f);
        root.pivot = new Vector2(0f, 0f);
        root.anchoredPosition = new Vector2(26f, 24f);
        root.localRotation = Quaternion.Euler(0f, 0f, -1.2f);

        Image back = root.gameObject.AddComponent<Image>();
        back.color = inkColor;
        back.raycastTarget = false;
        Outline outline = root.gameObject.AddComponent<Outline>();
        outline.effectColor = accentYellow;
        outline.effectDistance = new Vector2(4f, -4f);

        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        BuildTitleBlock();
        BuildSlots();
        Refresh();
    }

    private void BuildTitleBlock()
    {
        RectTransform wedge = CreateRect(root, "ItemBarYellowWedge", new Vector2(270f, 138f));
        wedge.anchorMin = wedge.anchorMax = new Vector2(0f, 0.5f);
        wedge.pivot = new Vector2(0.5f, 0.5f);
        wedge.anchoredPosition = new Vector2(96f, 4f);
        wedge.localRotation = Quaternion.Euler(0f, 0f, -7f);
        Image wedgeImage = wedge.gameObject.AddComponent<Image>();
        wedgeImage.color = accentYellow;
        wedgeImage.raycastTarget = false;

        RectTransform pinkSlash = CreateRect(root, "ItemBarPinkSlash", new Vector2(18f, 150f));
        pinkSlash.anchorMin = pinkSlash.anchorMax = new Vector2(0f, 0.5f);
        pinkSlash.anchoredPosition = new Vector2(224f, 0f);
        pinkSlash.localRotation = Quaternion.Euler(0f, 0f, 11f);
        Image pink = pinkSlash.gameObject.AddComponent<Image>();
        pink.color = accentPink;
        pink.raycastTarget = false;

        Text title = CreateText(root, "ITEM // LOADOUT", 20, FontStyle.Bold, TextAnchor.MiddleLeft, inkColor);
        title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        title.rectTransform.pivot = new Vector2(0f, 0.5f);
        title.rectTransform.sizeDelta = new Vector2(210f, 30f);
        title.rectTransform.anchoredPosition = new Vector2(24f, 26f);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -4f);

        currentName = CreateText(root, "NO WEAPON", 13, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor);
        currentName.rectTransform.anchorMin = currentName.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        currentName.rectTransform.pivot = new Vector2(0f, 0.5f);
        currentName.rectTransform.sizeDelta = new Vector2(245f, 28f);
        currentName.rectTransform.anchoredPosition = new Vector2(248f, 28f);

        currentPrompt = CreateText(root, "TAB / LB  TAP NEXT // HOLD GRID", 9, FontStyle.Bold, TextAnchor.MiddleLeft, accentCyan);
        currentPrompt.rectTransform.anchorMin = currentPrompt.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        currentPrompt.rectTransform.pivot = new Vector2(0f, 0.5f);
        currentPrompt.rectTransform.sizeDelta = new Vector2(250f, 24f);
        currentPrompt.rectTransform.anchoredPosition = new Vector2(248f, -28f);
    }

    private void BuildSlots()
    {
        const float slotWidth = 72f;
        const float slotHeight = 82f;
        const float gap = 8f;
        const float startX = 488f;

        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform slot = CreateRect(root, $"KineticBarSlot_{i}", new Vector2(slotWidth, slotHeight));
            slot.anchorMin = slot.anchorMax = new Vector2(0f, 0.5f);
            slot.pivot = new Vector2(0.5f, 0.5f);
            slot.anchoredPosition = new Vector2(startX + i * (slotWidth + gap), 0f);
            slot.localRotation = Quaternion.Euler(0f, 0f, (i % 3 - 1) * 1.2f);
            slotRects[i] = slot;

            Image back = slot.gameObject.AddComponent<Image>();
            back.color = inkColor;
            back.raycastTarget = false;
            slotBackgrounds[i] = back;

            Outline outline = slot.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(3f, -3f);

            Image icon = CreateImage(slot, "Icon", new Vector2(44f, 44f));
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.58f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            slotIcons[i] = icon;

            Text grade = CreateText(slot, string.Empty, 9, FontStyle.Bold, TextAnchor.UpperRight, accentYellow);
            SetAnchors(grade.rectTransform, new Vector2(0.58f, 0.73f), new Vector2(0.92f, 0.96f));
            slotGrades[i] = grade;

            Text state = CreateText(slot, string.Empty, 8, FontStyle.Bold, TextAnchor.LowerCenter, paperColor);
            SetAnchors(state.rectTransform, new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.27f));
            slotStates[i] = state;

            RectTransform accent = CreateRect(slot, "ActiveAccent", new Vector2(slotWidth - 8f, 5f));
            accent.anchorMin = accent.anchorMax = new Vector2(0.5f, 0f);
            accent.pivot = new Vector2(0.5f, 0f);
            accent.anchoredPosition = new Vector2(0f, 2f);
            Image accentImage = accent.gameObject.AddComponent<Image>();
            accentImage.color = accentYellow;
            accentImage.raycastTarget = false;
            slotAccents[i] = accentImage;
        }
    }

    private void Refresh()
    {
        if (equipmentSystem == null || canvas == null)
            return;

        int equipped = equipmentSystem.EquippedSlotIndex;
        BattleEquipmentSO current = null;
        if (equipped >= 0 && equipped < equipmentSystem.Slots.Count)
            current = equipmentSystem.Slots[equipped]?.equipment;

        if (currentName != null)
            currentName.text = current != null ? current.GetDisplayName().ToUpperInvariant() : "NO MANUAL WEAPON";

        for (int i = 0; i < SlotCount; i++)
        {
            bool unlocked = equipmentSystem.IsSlotUnlocked(i);
            BattleEquipmentSlot slot = i < equipmentSystem.Slots.Count ? equipmentSystem.Slots[i] : null;
            BattleEquipmentSO equipment = slot?.equipment;
            bool isEquipped = i == equipped;

            if (slotBackgrounds[i] != null)
            {
                slotBackgrounds[i].color = !unlocked
                    ? lockedColor
                    : isEquipped
                        ? new Color(0.045f, 0.23f, 0.28f, 1f)
                        : inkColor;
            }

            if (slotRects[i] != null)
                slotRects[i].localScale = Vector3.one * (isEquipped ? equippedScale : 1f);

            if (slotIcons[i] != null)
            {
                slotIcons[i].sprite = equipment != null ? equipment.icon : null;
                slotIcons[i].enabled = unlocked && equipment != null && equipment.icon != null;
                slotIcons[i].color = isEquipped ? Color.white : new Color(0.90f, 0.90f, 0.90f, 1f);
            }

            if (slotGrades[i] != null)
            {
                slotGrades[i].text = unlocked && equipment != null ? $"G{slot.grade}" : string.Empty;
                slotGrades[i].color = isEquipped ? accentCyan : accentYellow;
            }

            if (slotStates[i] != null)
            {
                Vector2Int p = BattleEquipmentSystem.SlotIndexToGrid(i);
                slotStates[i].text = !unlocked
                    ? "LOCK"
                    : isEquipped
                        ? "ACTIVE"
                        : equipment != null
                            ? $"{p.x + 1}-{p.y + 1}"
                            : "--";
                slotStates[i].color = isEquipped ? accentCyan : paperColor;
            }

            if (slotAccents[i] != null)
            {
                slotAccents[i].enabled = unlocked;
                slotAccents[i].color = isEquipped
                    ? accentYellow
                    : new Color(accentPink.r, accentPink.g, accentPink.b, equipment != null ? 0.42f : 0.12f);
            }
        }
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
}

/// <summary>
/// 별도 프리팹 수정 없이 BattleSceneManager에 Item Bar를 자동 설치합니다.
/// </summary>
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
