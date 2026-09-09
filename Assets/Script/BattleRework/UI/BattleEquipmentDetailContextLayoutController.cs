using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// BattleEquipmentDetailPanelController의 위치/강조를 현재 UI 문맥에 맞게 보정합니다.
///
/// Reward:
/// - 상세 설명 패널을 우측 외곽이 아니라 PACK 바로 위의 빈 공간으로 이동합니다.
/// - 현재 선택 중인 아이템이라는 의미가 분명하도록 테두리/헤더 색을 강하게 사용합니다.
///
/// Combat Tab:
/// - 기존 우측 상세 패널 배치를 유지합니다.
///
/// 기존 장비 데이터나 PACK 배치 데이터는 변경하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30980)]
public sealed class BattleEquipmentDetailContextLayoutController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentDetailPanelController detailController;

    [Header("Reward Detail Layout")]
    [SerializeField, Range(0.55f, 0.95f)] private float rewardPanelScale = 0.72f;
    [SerializeField] private Vector2 rewardPackOffset = new(8f, 26f);
    [SerializeField] private float safeMargin = 20f;

    [Header("Combat Detail Layout")]
    [SerializeField] private Vector2 combatVisibleOffset = new(-34f, 0f);
    [SerializeField] private Vector2 combatHiddenOffset = new(54f, 0f);

    [Header("Selected Item Accent")]
    [SerializeField] private Color inkColor = new(0.025f, 0.022f, 0.040f, 0.985f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.08f, 1f);
    [SerializeField] private Color accentCyan = new(0.14f, 0.92f, 0.94f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.16f, 0.50f, 1f);

    private RectTransform detailRoot;
    private CanvasGroup detailGroup;
    private RectTransform packRoot;
    private Outline detailOutline;
    private RectTransform contextFrame;
    private Text headerText;
    private Image headerBack;
    private bool rewardLayoutApplied;
    private float nextResolveTime;

    private void Awake()
    {
        ResolveReferences();
        ResolveUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResolveUi();
        nextResolveTime = 0f;
    }

    private void LateUpdate()
    {
        ResolveReferences();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.12f;
            ResolveUi();
        }

        if (detailRoot == null)
            return;

        bool reward = IsReward();
        bool visible = detailGroup != null && detailGroup.alpha > 0.015f;

        if (reward)
            ApplyRewardLayout(visible);
        else
            ApplyCombatLayout();

        ApplyContextAccent(reward, visible);
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (detailController == null)
            detailController = FindFirstObjectByType<BattleEquipmentDetailPanelController>();
    }

    private bool IsReward()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward;
    }

    private void ResolveUi()
    {
        if (detailRoot == null)
        {
            detailRoot = FindRect("EquipmentDetailPanel");
            if (detailRoot != null)
            {
                detailGroup = detailRoot.GetComponent<CanvasGroup>();
                detailOutline = detailRoot.GetComponent<Outline>();
                EnsureContextFrame();
                ResolveHeader();
            }
        }

        if (packRoot == null)
            packRoot = FindRect("BackpackMiniGrid");
    }

    private void ResolveHeader()
    {
        if (detailRoot == null)
            return;

        Transform header = detailRoot.Find("HeaderTag");
        if (header == null)
            return;

        headerBack = header.GetComponent<Image>();
        headerText = header.GetComponentInChildren<Text>(true);
    }

    private void EnsureContextFrame()
    {
        if (detailRoot == null || contextFrame != null)
            return;

        Transform existing = detailRoot.Find("SelectedItemContextFrame");
        if (existing is RectTransform existingRect)
        {
            contextFrame = existingRect;
            return;
        }

        contextFrame = CreateRect(detailRoot, "SelectedItemContextFrame", Vector2.zero);
        Stretch(contextFrame);
        contextFrame.SetAsLastSibling();

        CreateEdge(contextFrame, "SelectedTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -7f), new Vector2(0f, -1f), accentYellow);
        CreateEdge(contextFrame, "SelectedBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(0f, 7f), accentYellow);
        CreateEdge(contextFrame, "SelectedLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(-7f, 0f), new Vector2(1f, 0f), accentCyan);
        CreateEdge(contextFrame, "SelectedRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-1f, 0f), new Vector2(7f, 0f), accentPink);

        RectTransform upperCut = CreateRect(contextFrame, "SelectedUpperCut", new Vector2(112f, 12f));
        upperCut.anchorMin = upperCut.anchorMax = new Vector2(0.68f, 1f);
        upperCut.pivot = new Vector2(0.5f, 0.5f);
        upperCut.anchoredPosition = new Vector2(0f, 3f);
        upperCut.localRotation = Quaternion.Euler(0f, 0f, -2.2f);
        Image upperImage = upperCut.gameObject.AddComponent<Image>();
        upperImage.color = accentCyan;
        upperImage.raycastTarget = false;

        RectTransform lowerCut = CreateRect(contextFrame, "SelectedLowerCut", new Vector2(84f, 10f));
        lowerCut.anchorMin = lowerCut.anchorMax = new Vector2(0.26f, 0f);
        lowerCut.pivot = new Vector2(0.5f, 0.5f);
        lowerCut.anchoredPosition = new Vector2(0f, -3f);
        lowerCut.localRotation = Quaternion.Euler(0f, 0f, 2.5f);
        Image lowerImage = lowerCut.gameObject.AddComponent<Image>();
        lowerImage.color = accentPink;
        lowerImage.raycastTarget = false;

        contextFrame.gameObject.SetActive(false);
    }

    private static void CreateEdge(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax,
        Color color)
    {
        RectTransform edge = CreateRect(parent, name, Vector2.zero);
        edge.anchorMin = anchorMin;
        edge.anchorMax = anchorMax;
        edge.offsetMin = offsetMin;
        edge.offsetMax = offsetMax;
        Image image = edge.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private void ApplyRewardLayout(bool visible)
    {
        if (packRoot == null)
        {
            packRoot = FindRect("BackpackMiniGrid");
            if (packRoot == null)
                return;
        }

        rewardLayoutApplied = true;

        detailRoot.anchorMin = detailRoot.anchorMax = Vector2.zero;
        detailRoot.pivot = Vector2.zero;
        detailRoot.localScale = Vector3.one * rewardPanelScale;
        detailRoot.localRotation = Quaternion.Euler(0f, 0f, -0.45f);

        float packScaleX = Mathf.Max(0.01f, Mathf.Abs(packRoot.localScale.x));
        float packScaleY = Mathf.Max(0.01f, Mathf.Abs(packRoot.localScale.y));
        float packTop = packRoot.anchoredPosition.y + packRoot.sizeDelta.y * packScaleY;

        Vector2 target = new(
            packRoot.anchoredPosition.x + rewardPackOffset.x,
            packTop + rewardPackOffset.y);

        float scaledWidth = detailRoot.sizeDelta.x * rewardPanelScale;
        float scaledHeight = detailRoot.sizeDelta.y * rewardPanelScale;
        target.x = Mathf.Clamp(target.x, safeMargin, 1920f - scaledWidth - safeMargin);
        target.y = Mathf.Clamp(target.y, safeMargin, 1080f - scaledHeight - safeMargin);

        detailRoot.anchoredPosition = target;

        if (!visible && detailGroup != null && detailGroup.alpha <= 0.001f)
            contextFrame?.gameObject.SetActive(false);
    }

    private void ApplyCombatLayout()
    {
        if (!rewardLayoutApplied)
            return;

        rewardLayoutApplied = false;
        detailRoot.anchorMin = detailRoot.anchorMax = new Vector2(1f, 0.5f);
        detailRoot.pivot = new Vector2(1f, 0.5f);
        detailRoot.localScale = Vector3.one;
        detailRoot.localRotation = Quaternion.Euler(0f, 0f, 0.7f);

        bool visible = detailGroup != null && detailGroup.alpha > 0.02f;
        detailRoot.anchoredPosition = visible ? combatVisibleOffset : combatHiddenOffset;
    }

    private void ApplyContextAccent(bool reward, bool visible)
    {
        if (contextFrame != null)
            contextFrame.gameObject.SetActive(visible);

        if (detailOutline != null)
        {
            detailOutline.effectColor = reward ? accentYellow : accentCyan;
            detailOutline.effectDistance = reward ? new Vector2(8f, -8f) : new Vector2(5f, -5f);
        }

        if (headerBack != null)
            headerBack.color = reward ? accentYellow : accentCyan;

        if (headerText != null)
        {
            headerText.text = reward ? "SELECTED // ITEM" : "ITEM // DATA";
            headerText.color = inkColor;
        }
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

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

public static class BattleEquipmentDetailContextLayoutAutoInstaller
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

            if (manager.GetComponent<BattleEquipmentDetailContextLayoutController>() != null)
                continue;

            Undo.AddComponent<BattleEquipmentDetailContextLayoutController>(manager.gameObject);
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
            if (manager != null && manager.GetComponent<BattleEquipmentDetailContextLayoutController>() == null)
                manager.gameObject.AddComponent<BattleEquipmentDetailContextLayoutController>();
        }
    }
}
