using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Reward 아이템을 PACK에 넣은 뒤의 편집 단계를 Combat Tab과 같은 'Full Inspect' 모드로 전환합니다.
///
/// - PACK + 상세 패널을 화면 중앙에 크게 배치합니다.
/// - 상세 패널은 PACK 오른쪽에 바로 이웃하게 고정합니다.
/// - 기존 PACK / Detail 위치 컨트롤러보다 늦게 실행하여 RectTransform 경쟁과 깜빡임을 차단합니다.
/// - Reward 편집 동안 Time.timeScale을 낮춰 필드 몬스터/투사체/Animator/일반 연출도 함께 Bullet Time 처리합니다.
/// - UI 자체는 unscaled time을 사용하므로 편집 조작은 정상 속도로 유지됩니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33120)]
public sealed class BattleRewardFullInspectController : MonoBehaviour
{
    private const int BackdropSortingOrder = 765;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleInventoryInteractionController inventoryInteraction;
    [SerializeField] private BattleEquipmentDetailPanelController detailController;

    [Header("Full Inspect Layout")]
    [SerializeField] private Vector2 packPosition = new(300f, 245f);
    [SerializeField, Range(1.20f, 1.90f)] private float packScale = 1.52f;
    [SerializeField] private float panelGap = 38f;
    [SerializeField, Range(0.80f, 1.20f)] private float detailScale = 0.98f;
    [SerializeField] private Vector2 controlsOffset = new(0f, -92f);

    [Header("Bullet Time")]
    [SerializeField, Range(0.02f, 0.20f)] private float rewardBulletTimeScale = 0.05f;

    [Header("Backdrop")]
    [SerializeField] private Color backdropColor = new(0.008f, 0.010f, 0.016f, 0.82f);
    [SerializeField] private Color inkColor = new(0.025f, 0.022f, 0.040f, 0.985f);
    [SerializeField] private Color paperColor = new(0.95f, 0.91f, 0.78f, 1f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.08f, 1f);
    [SerializeField] private Color accentCyan = new(0.14f, 0.92f, 0.94f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.16f, 0.50f, 1f);

    private Canvas backdropCanvas;
    private CanvasGroup backdropGroup;
    private RectTransform backdropRoot;

    private RectTransform packRoot;
    private CanvasGroup packGroup;
    private RectTransform detailRoot;
    private CanvasGroup detailGroup;
    private RectTransform trashRoot;
    private RectTransform doneRoot;
    private RectTransform statusRoot;

    private bool fullInspectActive;
    private bool bulletTimeOwned;
    private float previousTimeScale = 1f;
    private float previousFixedDeltaTime = 0.02f;
    private int lastStagedSlot = -1;
    private float nextResolveTime;

    private FieldInfo stagedRewardSlotField;

    private void Awake()
    {
        ResolveReferences();
        CacheReflection();
        EnsureBackdrop();
        ResolveUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheReflection();
        EnsureBackdrop();
        nextResolveTime = 0f;
    }

    private void OnDisable()
    {
        fullInspectActive = false;
        SetBackdropVisible(false, true);
        RestoreBulletTime(true);
    }

    private void Update()
    {
        ResolveReferences();
        EnsureBackdrop();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.10f;
            ResolveUi();
        }

        bool shouldInspect = ShouldUseFullInspect();
        if (shouldInspect && !fullInspectActive)
            EnterFullInspect();
        else if (!shouldInspect && fullInspectActive)
            ExitFullInspect();

        if (shouldInspect)
        {
            MaintainSelectedItem();
            MaintainBulletTime();
        }
        else if (bulletTimeOwned && !BattlePauseController.IsPaused)
        {
            RestoreBulletTime(false);
        }

        SetBackdropVisible(shouldInspect, false);
    }

    private void LateUpdate()
    {
        if (!fullInspectActive)
            return;

        ResolveUi();
        ApplyFullInspectLayout();
        StabilizeDetailPanel();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>();
        if (detailController == null)
            detailController = FindFirstObjectByType<BattleEquipmentDetailPanelController>();
    }

    private void CacheReflection()
    {
        stagedRewardSlotField ??= typeof(BattleInventoryInteractionController).GetField(
            "stagedRewardSlot",
            PrivateInstance);
    }

    private bool ShouldUseFullInspect()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Reward &&
               inventoryInteraction != null &&
               inventoryInteraction.IsRewardPackEditing;
    }

    private void EnterFullInspect()
    {
        fullInspectActive = true;
        lastStagedSlot = -1;
        ResolveUi();
        EnterBulletTime();
        MaintainSelectedItem();
        ApplyFullInspectLayout();
        StabilizeDetailPanel();
    }

    private void ExitFullInspect()
    {
        fullInspectActive = false;
        lastStagedSlot = -1;
        if (!BattlePauseController.IsPaused)
            RestoreBulletTime(false);
    }

    private void MaintainSelectedItem()
    {
        if (inventoryInteraction == null || detailController == null || stagedRewardSlotField == null)
            return;

        object raw = stagedRewardSlotField.GetValue(inventoryInteraction);
        if (raw is not int slot || slot < 0)
            return;

        if (slot != lastStagedSlot)
        {
            lastStagedSlot = slot;
            detailController.SelectSlotFromPointer(slot);
        }
    }

    private void ResolveUi()
    {
        if (packRoot == null)
        {
            packRoot = FindRect("BackpackMiniGrid");
            if (packRoot != null)
                packGroup = packRoot.GetComponent<CanvasGroup>();
        }

        if (detailRoot == null)
        {
            detailRoot = FindRect("EquipmentDetailPanel");
            if (detailRoot != null)
                detailGroup = detailRoot.GetComponent<CanvasGroup>();
        }

        if (trashRoot == null)
            trashRoot = FindRect("InventoryTrash");
        if (doneRoot == null)
            doneRoot = FindRect("RewardPackDone");
        if (statusRoot == null)
        {
            Text[] texts = UnityEngine.Object.FindObjectsByType<Text>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < texts.Length; i++)
            {
                Text text = texts[i];
                if (text != null && text.text != null && text.text.Contains("ITEM IN PACK"))
                {
                    statusRoot = text.rectTransform;
                    break;
                }
            }
        }
    }

    private void ApplyFullInspectLayout()
    {
        if (packRoot == null || detailRoot == null)
            return;

        packRoot.anchorMin = packRoot.anchorMax = Vector2.zero;
        packRoot.pivot = Vector2.zero;
        packRoot.anchoredPosition = packPosition;
        packRoot.localScale = Vector3.one * packScale;
        packRoot.localRotation = Quaternion.Euler(0f, 0f, -0.6f);

        if (packGroup != null)
        {
            packGroup.alpha = 1f;
            packGroup.blocksRaycasts = !BattlePauseController.IsPaused;
            packGroup.interactable = !BattlePauseController.IsPaused;
        }

        float packWidth = packRoot.sizeDelta.x * packScale;
        float packHeight = packRoot.sizeDelta.y * packScale;
        float detailWidth = detailRoot.sizeDelta.x * detailScale;
        float detailHeight = detailRoot.sizeDelta.y * detailScale;

        float detailX = packPosition.x + packWidth + panelGap;
        float detailY = packPosition.y + (packHeight - detailHeight) * 0.5f;

        detailRoot.anchorMin = detailRoot.anchorMax = Vector2.zero;
        detailRoot.pivot = Vector2.zero;
        detailRoot.anchoredPosition = new Vector2(detailX, detailY);
        detailRoot.localScale = Vector3.one * detailScale;
        detailRoot.localRotation = Quaternion.Euler(0f, 0f, 0.35f);

        PositionEditControls(packWidth, packHeight);
    }

    private void PositionEditControls(float packWidth, float packHeight)
    {
        if (packRoot == null)
            return;

        Vector2 rightBase = packPosition + new Vector2(packWidth + 22f, 0f);

        if (trashRoot != null)
        {
            trashRoot.anchorMin = trashRoot.anchorMax = Vector2.zero;
            trashRoot.pivot = Vector2.zero;
            trashRoot.anchoredPosition = rightBase + new Vector2(0f, 0f) + controlsOffset;
        }

        if (doneRoot != null)
        {
            doneRoot.anchorMin = doneRoot.anchorMax = Vector2.zero;
            doneRoot.pivot = Vector2.zero;
            doneRoot.anchoredPosition = rightBase + new Vector2(0f, 88f) + controlsOffset;
        }

        if (statusRoot != null)
        {
            statusRoot.anchorMin = statusRoot.anchorMax = Vector2.zero;
            statusRoot.pivot = Vector2.zero;
            statusRoot.anchoredPosition = packPosition + new Vector2(0f, packHeight + 24f);
            statusRoot.sizeDelta = new Vector2(720f, 44f);
        }
    }

    private void StabilizeDetailPanel()
    {
        if (detailRoot == null)
            return;

        if (detailGroup == null)
            detailGroup = detailRoot.GetComponent<CanvasGroup>();

        // Reward 편집 단계에서는 staged item이 이미 확정되어 있으므로, 다른 컨트롤러의
        // hover/selection 상태가 한 프레임 흔들리더라도 패널 자체는 절대 닫히지 않습니다.
        if (detailGroup != null)
        {
            detailGroup.alpha = 1f;
            detailGroup.blocksRaycasts = false;
            detailGroup.interactable = false;
        }

        if (!detailRoot.gameObject.activeSelf)
            detailRoot.gameObject.SetActive(true);
    }

    private void EnterBulletTime()
    {
        if (bulletTimeOwned || BattlePauseController.IsPaused)
            return;

        previousTimeScale = Time.timeScale;
        previousFixedDeltaTime = Time.fixedDeltaTime;
        if (previousTimeScale <= 0f)
            return;

        bulletTimeOwned = true;
        ApplyBulletTimeScale();
    }

    private void MaintainBulletTime()
    {
        if (BattlePauseController.IsPaused)
            return;

        if (!bulletTimeOwned)
        {
            EnterBulletTime();
            return;
        }

        float target = Mathf.Clamp(rewardBulletTimeScale, 0.02f, 0.20f);
        float expected = previousTimeScale * target;
        if (!Mathf.Approximately(Time.timeScale, expected))
            ApplyBulletTimeScale();
    }

    private void ApplyBulletTimeScale()
    {
        if (!bulletTimeOwned || BattlePauseController.IsPaused)
            return;

        float scale = Mathf.Clamp(rewardBulletTimeScale, 0.02f, 0.20f);
        Time.timeScale = previousTimeScale * scale;
        Time.fixedDeltaTime = Mathf.Max(0.0001f, previousFixedDeltaTime * scale);
    }

    private void RestoreBulletTime(bool force)
    {
        if (!bulletTimeOwned)
            return;

        if (!force && BattlePauseController.IsPaused)
            return;

        Time.timeScale = previousTimeScale;
        Time.fixedDeltaTime = previousFixedDeltaTime;
        bulletTimeOwned = false;
    }

    private void EnsureBackdrop()
    {
        if (backdropCanvas != null)
            return;

        GameObject canvasObject = new("RewardFullInspectBackdropCanvas");
        canvasObject.transform.SetParent(transform, false);
        backdropCanvas = canvasObject.AddComponent<Canvas>();
        backdropCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        backdropCanvas.overrideSorting = true;
        backdropCanvas.sortingOrder = BackdropSortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        backdropRoot = CreateRect(canvasObject.transform, "RewardFullInspectBackdrop", Vector2.zero);
        Stretch(backdropRoot);
        Image back = backdropRoot.gameObject.AddComponent<Image>();
        back.color = backdropColor;
        back.raycastTarget = false;

        backdropGroup = backdropRoot.gameObject.AddComponent<CanvasGroup>();
        backdropGroup.alpha = 0f;
        backdropGroup.blocksRaycasts = false;
        backdropGroup.interactable = false;

        RectTransform yellow = CreateRect(backdropRoot, "InspectYellowSlash", new Vector2(720f, 16f));
        yellow.anchorMin = yellow.anchorMax = new Vector2(0.5f, 0.92f);
        yellow.anchoredPosition = new Vector2(-260f, 0f);
        yellow.localRotation = Quaternion.Euler(0f, 0f, -1.6f);
        Image yellowImage = yellow.gameObject.AddComponent<Image>();
        yellowImage.color = accentYellow;
        yellowImage.raycastTarget = false;

        RectTransform cyan = CreateRect(backdropRoot, "InspectCyanSlash", new Vector2(520f, 10f));
        cyan.anchorMin = cyan.anchorMax = new Vector2(0.5f, 0.88f);
        cyan.anchoredPosition = new Vector2(330f, 0f);
        cyan.localRotation = Quaternion.Euler(0f, 0f, 1.2f);
        Image cyanImage = cyan.gameObject.AddComponent<Image>();
        cyanImage.color = accentCyan;
        cyanImage.raycastTarget = false;

        Text title = CreateText(backdropRoot, "PACK // EDIT MODE", 28, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor);
        title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 0.95f);
        title.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        title.rectTransform.sizeDelta = new Vector2(980f, 70f);
        title.rectTransform.anchoredPosition = new Vector2(-170f, 0f);

        Text hint = CreateText(backdropRoot, "REARRANGE  //  TRASH  //  DONE WHEN READY", 13, FontStyle.Bold, TextAnchor.MiddleRight, accentPink);
        hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0.88f);
        hint.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        hint.rectTransform.sizeDelta = new Vector2(900f, 44f);
        hint.rectTransform.anchoredPosition = new Vector2(250f, 0f);
    }

    private void SetBackdropVisible(bool visible, bool immediate)
    {
        if (backdropGroup == null)
            return;

        if (immediate)
        {
            backdropGroup.alpha = visible ? 1f : 0f;
            return;
        }

        float t = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
        backdropGroup.alpha = Mathf.Lerp(backdropGroup.alpha, visible ? 1f : 0f, t);
        if (!visible && backdropGroup.alpha < 0.002f)
            backdropGroup.alpha = 0f;
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

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

public static class BattleRewardFullInspectAutoInstaller
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

            if (manager.GetComponent<BattleRewardFullInspectController>() != null)
                continue;

            Undo.AddComponent<BattleRewardFullInspectController>(manager.gameObject);
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
            if (manager != null && manager.GetComponent<BattleRewardFullInspectController>() == null)
                manager.gameObject.AddComponent<BattleRewardFullInspectController>();
        }
    }
}
