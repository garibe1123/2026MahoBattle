using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Reward TV의 화면 프레임/배경만 담당합니다.
///
/// Phase 6 소유권 규칙:
/// - Reward 카드/Outline/Hover/결정/포기: BattleRewardCardActionController
/// - Reward PACK/Grid: Inventory UI 계층
/// - 이 클래스: PrizeSelectionScreen + ScreenInner의 정적 화면 chrome만
///
/// 카드, PlacementNotice, PACK, 선택 상태는 절대 수정하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30600)]
public sealed class BattleRewardKineticThemeController : MonoBehaviour
{
    [SerializeField] private BattleRunManager runManager;

    [Header("Screen Chrome")]
    [SerializeField] private Color screenColor = new(0.018f, 0.020f, 0.024f, 0.995f);
    [SerializeField] private Color innerColor = new(0.028f, 0.030f, 0.034f, 0.995f);
    [SerializeField] private Color screenOutline = new(0.96f, 0.96f, 0.96f, 0.28f);
    [SerializeField] private Color innerOutline = new(0.96f, 0.96f, 0.96f, 0.16f);
    [SerializeField] private Color titleColor = new(0.96f, 0.96f, 0.96f, 1f);
    [SerializeField] private Color secondaryTextColor = new(0.56f, 0.57f, 0.60f, 1f);

    private RectTransform rewardScreen;
    private RectTransform screenInner;
    private RectTransform prizeChoices;
    private RectTransform placementNotice;
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

    private void Update()
    {
        ResolveReferences();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.20f;
            ResolveUi();
        }

        if (!IsReward())
            return;

        ApplyScreenChrome();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
    }

    private bool IsReward()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward;
    }

    private void ResolveUi()
    {
        if (rewardScreen == null)
            rewardScreen = FindRect("PrizeSelectionScreen");

        if (rewardScreen == null)
        {
            screenInner = null;
            prizeChoices = null;
            placementNotice = null;
            return;
        }

        screenInner = rewardScreen.Find("ScreenInner") as RectTransform;
        prizeChoices = screenInner != null
            ? screenInner.Find("PrizeChoices") as RectTransform
            : null;
        placementNotice = screenInner != null
            ? screenInner.Find("PlacementNotice") as RectTransform
            : null;
    }

    private void ApplyScreenChrome()
    {
        if (rewardScreen != null)
        {
            rewardScreen.localRotation = Quaternion.identity;
            StylePanel(rewardScreen, screenColor, screenOutline, new Vector2(2f, -2f));
        }

        if (screenInner == null)
            return;

        StylePanel(screenInner, innerColor, innerOutline, new Vector2(2f, -2f));

        Transform obsoleteAccent = screenInner.Find("RewardKineticAccentLayer");
        if (obsoleteAccent != null && obsoleteAccent.gameObject.activeSelf)
            obsoleteAccent.gameObject.SetActive(false);

        Text[] texts = screenInner.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null || IsChildOf(text.transform, prizeChoices) || IsChildOf(text.transform, placementNotice))
                continue;

            string value = text.text ?? string.Empty;
            text.color = value.Contains("CHOOSE YOUR PRIZE")
                ? titleColor
                : secondaryTextColor;
        }
    }

    private static void StylePanel(RectTransform target, Color backgroundColor, Color outlineColor, Vector2 outlineDistance)
    {
        if (target == null)
            return;

        Image image = target.GetComponent<Image>();
        if (image != null)
            image.color = backgroundColor;

        Outline outline = target.GetComponent<Outline>();
        if (outline == null)
            outline = target.gameObject.AddComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = outlineDistance;
    }

    private static bool IsChildOf(Transform child, Transform parent)
    {
        if (child == null || parent == null)
            return false;

        Transform current = child;
        while (current != null)
        {
            if (current == parent)
                return true;
            current = current.parent;
        }
        return false;
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

public static class BattleRewardKineticThemeAutoInstaller
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

            if (manager.GetComponent<BattleRewardKineticThemeController>() != null)
                continue;

            Undo.AddComponent<BattleRewardKineticThemeController>(manager.gameObject);
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
            if (manager != null && manager.GetComponent<BattleRewardKineticThemeController>() == null)
                manager.gameObject.AddComponent<BattleRewardKineticThemeController>();
        }
    }
}
