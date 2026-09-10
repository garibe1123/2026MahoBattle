using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Reward 선택 단계의 액션 버튼을 현재 선택된 카드에 직접 부착합니다.
/// - Hover만으로는 표시하지 않습니다.
/// - 클릭되어 pendingRewardIndex가 된 카드에만 [결정] / [아이템 획득 포기]를 표시합니다.
/// - 카드 Transform은 절대 수정하지 않고 버튼 RectTransform만 카드 로컬 좌표에 배치합니다.
/// - 기존 BattleRewardDecisionFlowController가 만든 RewardDecisionConfirm 버튼을 그대로 재사용합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(44100)]
public sealed class BattleRewardCardActionController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Header("Selected Card Actions")]
    [SerializeField] private Vector2 decideSize = new(206f, 48f);
    [SerializeField] private Vector2 decidePosition = new(0f, 60f);
    [SerializeField] private Vector2 skipSize = new(206f, 38f);
    [SerializeField] private Vector2 skipPosition = new(0f, 14f);

    [Header("Monochrome")]
    [SerializeField] private Color inkColor = new(0.015f, 0.016f, 0.019f, 0.99f);
    [SerializeField] private Color paperColor = new(0.96f, 0.96f, 0.96f, 1f);
    [SerializeField] private Color mutedColor = new(0.58f, 0.59f, 0.62f, 1f);

    private BattleRunManager runManager;
    private BattleHUD battleHud;
    private BattleInventoryInteractionController inventoryInteraction;

    private FieldInfo pendingRewardIndexField;

    private RectTransform prizeChoices;
    private RectTransform decideRoot;
    private RectTransform skipRoot;
    private Button skipButton;
    private int lastSelectedIndex = int.MinValue;
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
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
        Canvas.willRenderCanvases += HandleWillRenderCanvases;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
        HideActions();
    }

    private void OnDestroy()
    {
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
    }

    private void Update()
    {
        ResolveReferences();
        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.08f;
            ResolveUi();
        }
    }

    private void LateUpdate()
    {
        ApplySelectedCardActions();
    }

    private void HandleWillRenderCanvases()
    {
        if (isActiveAndEnabled)
            ApplySelectedCardActions();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (battleHud == null)
            battleHud = FindFirstObjectByType<BattleHUD>(FindObjectsInactive.Include);
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);

        if (pendingRewardIndexField == null)
            pendingRewardIndexField = typeof(BattleHUD).GetField("pendingRewardIndex", PrivateInstance);
    }

    private void ResolveUi()
    {
        RectTransform rewardScreen = FindRect("PrizeSelectionScreen");
        RectTransform inner = rewardScreen != null
            ? rewardScreen.Find("ScreenInner") as RectTransform
            : null;

        RectTransform resolvedChoices = inner != null
            ? inner.Find("PrizeChoices") as RectTransform
            : FindRect("PrizeChoices");
        if (resolvedChoices != null)
            prizeChoices = resolvedChoices;

        RectTransform resolvedDecide = FindRect("RewardDecisionConfirm");
        if (resolvedDecide != null)
            decideRoot = resolvedDecide;

        EnsureSkipButton();
    }

    private void EnsureSkipButton()
    {
        if (skipRoot != null)
            return;

        RectTransform existing = FindRect("RewardDecisionSkip");
        if (existing != null)
        {
            skipRoot = existing;
            skipButton = skipRoot.GetComponent<Button>();
            return;
        }

        if (prizeChoices == null)
            return;

        GameObject go = new("RewardDecisionSkip");
        go.transform.SetParent(prizeChoices, false);
        skipRoot = go.AddComponent<RectTransform>();
        skipRoot.sizeDelta = skipSize;

        Image back = go.AddComponent<Image>();
        back.color = inkColor;
        back.raycastTarget = true;

        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = mutedColor;
        outline.effectDistance = new Vector2(3f, -3f);

        RectTransform textRect = new GameObject("Text").AddComponent<RectTransform>();
        textRect.SetParent(skipRoot, false);
        Stretch(textRect);

        Text label = textRect.gameObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = "아이템 획득 포기";
        label.fontSize = 14;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = paperColor;
        label.raycastTarget = false;

        skipButton = go.AddComponent<Button>();
        skipButton.targetGraphic = back;
        skipButton.onClick.AddListener(SkipReward);

        ColorBlock colors = skipButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
        colors.pressedColor = new Color(0.68f, 0.68f, 0.68f, 1f);
        colors.selectedColor = colors.highlightedColor;
        skipButton.colors = colors;

        skipRoot.gameObject.SetActive(false);
    }

    private void ApplySelectedCardActions()
    {
        if (!IsChoiceStage())
        {
            HideActions();
            return;
        }

        ResolveUi();

        int selectedIndex = GetPendingRewardIndex();
        RectTransform selectedCard = FindRewardCard(selectedIndex);
        bool valid = selectedCard != null &&
                     runManager != null &&
                     selectedIndex >= 0 &&
                     selectedIndex < runManager.CurrentRewardChoices.Count;

        if (!valid)
        {
            HideActions();
            lastSelectedIndex = selectedIndex;
            return;
        }

        if (decideRoot != null)
        {
            if (decideRoot.parent != selectedCard)
                decideRoot.SetParent(selectedCard, false);

            decideRoot.anchorMin = decideRoot.anchorMax = new Vector2(0.5f, 0f);
            decideRoot.pivot = new Vector2(0.5f, 0.5f);
            decideRoot.sizeDelta = decideSize;
            decideRoot.anchoredPosition = decidePosition;
            decideRoot.localScale = Vector3.one;
            decideRoot.localRotation = Quaternion.identity;
            decideRoot.gameObject.SetActive(true);
            decideRoot.SetAsLastSibling();
        }

        EnsureSkipButton();
        if (skipRoot != null)
        {
            if (skipRoot.parent != selectedCard)
                skipRoot.SetParent(selectedCard, false);

            skipRoot.anchorMin = skipRoot.anchorMax = new Vector2(0.5f, 0f);
            skipRoot.pivot = new Vector2(0.5f, 0.5f);
            skipRoot.sizeDelta = skipSize;
            skipRoot.anchoredPosition = skipPosition;
            skipRoot.localScale = Vector3.one;
            skipRoot.localRotation = Quaternion.identity;
            skipRoot.gameObject.SetActive(true);
            skipRoot.SetAsLastSibling();
        }

        lastSelectedIndex = selectedIndex;
    }

    private RectTransform FindRewardCard(int rewardIndex)
    {
        if (prizeChoices == null || rewardIndex < 0)
            return null;

        RewardPrizeDrag[] cards = prizeChoices.GetComponentsInChildren<RewardPrizeDrag>(true);
        for (int i = 0; i < cards.Length; i++)
        {
            RewardPrizeDrag drag = cards[i];
            if (drag != null && drag.RewardIndex == rewardIndex)
                return drag.transform as RectTransform;
        }
        return null;
    }

    private bool IsChoiceStage()
    {
        if (runManager == null || !runManager.RunActive || runManager.State != BattleRunState.Reward)
            return false;

        return inventoryInteraction == null || !inventoryInteraction.IsRewardPackEditing;
    }

    private int GetPendingRewardIndex()
    {
        if (battleHud == null || pendingRewardIndexField == null)
            return -1;
        object raw = pendingRewardIndexField.GetValue(battleHud);
        return raw is int index ? index : -1;
    }

    private void SkipReward()
    {
        if (!IsChoiceStage() || runManager == null)
            return;

        if (pendingRewardIndexField != null && battleHud != null)
            pendingRewardIndexField.SetValue(battleHud, -1);

        HideActions();
        runManager.SkipReward();
    }

    private void HideActions()
    {
        if (decideRoot != null && decideRoot.gameObject.activeSelf)
            decideRoot.gameObject.SetActive(false);
        if (skipRoot != null && skipRoot.gameObject.activeSelf)
            skipRoot.gameObject.SetActive(false);
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = Object.FindObjectsByType<RectTransform>(
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

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

public static class BattleRewardCardActionAutoInstaller
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

            if (manager.GetComponent<BattleRewardCardActionController>() != null)
                continue;

            Undo.AddComponent<BattleRewardCardActionController>(manager.gameObject);
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
            if (manager != null && manager.GetComponent<BattleRewardCardActionController>() == null)
                manager.gameObject.AddComponent<BattleRewardCardActionController>();
        }
    }
}
