using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Combat HUD 보정과 3x3 Loadout Grid의 마우스 입력을 담당합니다.
/// BattleKineticLoadoutUI의 공개 API만 사용하며 private field reflection에 의존하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30500)]
public sealed class BattleCombatHudInputBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleKineticLoadoutUI loadoutUI;
    [SerializeField] private PlayerController player;

    [Header("Compact Vitals")]
    [SerializeField] private Color hpColor = new(0.95f, 0.18f, 0.30f, 1f);
    [SerializeField] private Color staminaColor = new(0.18f, 0.82f, 0.95f, 1f);
    [SerializeField] private Color textColor = new(0.94f, 0.90f, 0.76f, 1f);

    private RectTransform compactRoot;
    private RectTransform hpFillRect;
    private RectTransform staminaFillRect;
    private Text hpText;
    private Text staminaText;
    private CanvasGroup fullGridGroup;

    private float nextResolveTime;
    private bool pointerTargetsInstalled;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        nextResolveTime = 0f;
    }

    private void Update()
    {
        ResolveReferences();
        DisableLegacyTopLeftStatus();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.25f;
            ResolveCompactChip();
            ResolveFullGrid();
            InstallMouseTargets();
        }

        UpdateCompactVitals();
        UpdateGridRaycastState();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (loadoutUI == null)
            loadoutUI = FindFirstObjectByType<BattleKineticLoadoutUI>();
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();
    }

    private void DisableLegacyTopLeftStatus()
    {
        RectTransform legacy = FindRect("BroadcastStatus");
        if (legacy != null && legacy.gameObject.activeSelf)
            legacy.gameObject.SetActive(false);
    }

    private void ResolveCompactChip()
    {
        if (compactRoot == null && loadoutUI != null)
            compactRoot = loadoutUI.CompactRoot;
        if (compactRoot == null)
            compactRoot = FindRect("CurrentLoadoutChip");
        if (compactRoot == null)
            return;

        if (compactRoot.Find("CompactVitals") != null)
            return;

        compactRoot.sizeDelta = new Vector2(430f, 156f);
        compactRoot.anchoredPosition = new Vector2(-28f, 22f);

        Transform icon = compactRoot.Find("Icon");
        if (icon is RectTransform iconRect)
        {
            iconRect.sizeDelta = new Vector2(58f, 58f);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 1f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = new Vector2(76f, -48f);
        }

        Text[] existingTexts = compactRoot.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < existingTexts.Length; i++)
        {
            Text text = existingTexts[i];
            if (text == null)
                continue;

            if (text.text != null && text.text.Contains("TAB"))
                SetAnchors(text.rectTransform, new Vector2(0.30f, 0.48f), new Vector2(0.96f, 0.62f));
            else
                SetAnchors(text.rectTransform, new Vector2(0.30f, 0.67f), new Vector2(0.96f, 0.90f));
        }

        RectTransform vitals = CreateRect(compactRoot, "CompactVitals", Vector2.zero);
        SetAnchors(vitals, new Vector2(0.08f, 0.08f), new Vector2(0.96f, 0.45f));

        hpText = CreateText(vitals, "HP", 9, FontStyle.Bold, TextAnchor.MiddleLeft, textColor);
        SetAnchors(hpText.rectTransform, new Vector2(0f, 0.58f), new Vector2(0.18f, 0.96f));
        hpFillRect = CreateProgressBar(vitals, "HP", new Vector2(0.18f, 0.63f), new Vector2(1f, 0.88f), hpColor);

        staminaText = CreateText(vitals, "ST", 9, FontStyle.Bold, TextAnchor.MiddleLeft, textColor);
        SetAnchors(staminaText.rectTransform, new Vector2(0f, 0.05f), new Vector2(0.18f, 0.43f));
        staminaFillRect = CreateProgressBar(vitals, "ST", new Vector2(0.18f, 0.10f), new Vector2(1f, 0.35f), staminaColor);
    }

    private void ResolveFullGrid()
    {
        if (fullGridGroup == null && loadoutUI != null)
            fullGridGroup = loadoutUI.FullGroup;
        if (fullGridGroup != null)
            return;

        RectTransform full = FindRect("LoadoutSwitchFull");
        if (full == null)
            return;

        fullGridGroup = full.GetComponent<CanvasGroup>();
        Canvas canvas = full.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        EnsureEventSystem();
    }

    private void InstallMouseTargets()
    {
        if (pointerTargetsInstalled || loadoutUI == null)
            return;

        int installed = 0;
        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            RectTransform slot = FindRect($"GridSlot_{i}");
            if (slot == null)
                continue;

            Image background = slot.GetComponent<Image>();
            if (background != null)
                background.raycastTarget = true;

            BattleLoadoutGridPointerTarget target = slot.GetComponent<BattleLoadoutGridPointerTarget>();
            if (target == null)
                target = slot.gameObject.AddComponent<BattleLoadoutGridPointerTarget>();
            target.Configure(this, i);
            installed++;
        }

        pointerTargetsInstalled = installed == BattleEquipmentSystem.MaxSlotCount;
    }

    private void UpdateCompactVitals()
    {
        if (player == null || compactRoot == null)
            return;

        float hpMax = Mathf.Max(1f, player.maxHp);
        float staminaMax = Mathf.Max(1f, player.maxStamina);
        float hp01 = Mathf.Clamp01(player.CurrentHp / hpMax);
        float stamina01 = Mathf.Clamp01(player.CurrentStamina / staminaMax);

        SetBarAmount(hpFillRect, hp01);
        SetBarAmount(staminaFillRect, stamina01);

        if (hpText != null)
            hpText.text = $"HP {player.CurrentHp:0}/{hpMax:0}";
        if (staminaText != null)
            staminaText.text = $"ST {player.CurrentStamina:0}/{staminaMax:0}";
    }

    private void UpdateGridRaycastState()
    {
        if (fullGridGroup == null)
            return;

        bool active = loadoutUI != null && loadoutUI.IsSwitchBoardOpen && fullGridGroup.alpha > 0.05f;
        fullGridGroup.blocksRaycasts = active;
        fullGridGroup.interactable = active;
    }

    internal void HandleSlotHover(int index)
    {
        if (loadoutUI == null || equipmentSystem == null || !loadoutUI.IsSwitchBoardOpen)
            return;
        if (!equipmentSystem.IsSlotUnlocked(index))
            return;

        loadoutUI.SetSelectedIndexFromExternal(index, true);
    }

    internal void HandleSlotClick(int index, PointerEventData.InputButton button)
    {
        if (button != PointerEventData.InputButton.Left || loadoutUI == null || equipmentSystem == null)
            return;
        if (!loadoutUI.IsSwitchBoardOpen || !equipmentSystem.IsSlotUnlocked(index))
            return;

        loadoutUI.SetSelectedIndexFromExternal(index, true);
        equipmentSystem.EquipSlot(index);
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

    private static RectTransform CreateProgressBar(Transform parent, string name, Vector2 min, Vector2 max, Color color)
    {
        RectTransform background = CreateRect(parent, name + "Bar_BG", Vector2.zero);
        SetAnchors(background, min, max);
        Image bg = background.gameObject.AddComponent<Image>();
        bg.color = new Color(0.10f, 0.09f, 0.13f, 0.96f);
        bg.raycastTarget = false;

        RectTransform fill = CreateRect(background, name + "Bar_Fill", Vector2.zero);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = new Vector2(2f, 2f);
        fill.offsetMax = new Vector2(-2f, -2f);
        fill.pivot = new Vector2(0f, 0.5f);
        Image image = fill.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return fill;
    }

    private static void SetBarAmount(RectTransform fill, float amount)
    {
        if (fill == null)
            return;

        amount = Mathf.Clamp01(amount);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(amount, 1f);
        fill.offsetMin = new Vector2(2f, 2f);
        fill.offsetMax = new Vector2(-2f, -2f);
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return rect;
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

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject go = new("BattleLoadoutEventSystem");
        DontDestroyOnLoad(go);
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }
}

internal sealed class BattleLoadoutGridPointerTarget : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    private BattleCombatHudInputBridge owner;
    private int slotIndex;

    public void Configure(BattleCombatHudInputBridge bridge, int index)
    {
        owner = bridge;
        slotIndex = index;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        owner?.HandleSlotHover(slotIndex);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        owner?.HandleSlotClick(slotIndex, eventData.button);
    }
}

public static class BattleCombatHudInputBridgeAutoInstaller
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

            if (manager.GetComponent<BattleCombatHudInputBridge>() != null)
                continue;

            Undo.AddComponent<BattleCombatHudInputBridge>(manager.gameObject);
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
            if (manager != null && manager.GetComponent<BattleCombatHudInputBridge>() == null)
                manager.gameObject.AddComponent<BattleCombatHudInputBridge>();
        }
    }
}
