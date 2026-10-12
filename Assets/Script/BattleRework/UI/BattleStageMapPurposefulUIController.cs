using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Legacy-named compatibility component for the detached Script Selection canvas.
///
/// The old Stage Map node/link renderer has been removed. BattleSpatialMapController
/// owns the actual script cards, page interaction, hover tracking and confirmation.
/// This component only keeps the detached Script Selection root unclipped and
/// interactive while BattleRunManager is in SelectingNode.
///
/// The class/file name is intentionally preserved so existing scene references do
/// not become Missing Script components.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(32580)]
public sealed class BattleStageMapPurposefulUIController : MonoBehaviour
{
    private const string LegacyScriptSelectionContentName =
        "MapSelectionContent";

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleHUD hud;

    private BattleRunManager subscribedRunManager;
    private RectTransform scriptSelectionContent;
    private CanvasGroup scriptSelectionGroup;
    private Canvas scriptSelectionCanvas;
    private Coroutine refreshRoutine;

    private readonly Dictionary<Behaviour, bool> clippingStates =
        new();

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeRunEvents();

        if (IsScriptSelectionActive())
            QueueRefresh();
    }

    private void OnDisable()
    {
        if (refreshRoutine != null)
        {
            StopCoroutine(
                refreshRoutine);

            refreshRoutine = null;
        }

        UnsubscribeRunEvents();
        RestoreClipping();
    }

    private void OnDestroy()
    {
        UnsubscribeRunEvents();
        RestoreClipping();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager =
                FindFirstObjectByType<BattleRunManager>();

        if (hud == null)
            hud =
                FindFirstObjectByType<BattleHUD>();

        SubscribeRunEvents();
    }

    private void SubscribeRunEvents()
    {
        if (subscribedRunManager == runManager)
            return;

        if (subscribedRunManager != null)
        {
            subscribedRunManager.StateChanged -=
                HandleRunStateChanged;

            subscribedRunManager.NextNodeSelectionRequested -=
                HandleNextNodeSelectionRequested;

            subscribedRunManager.NodeEntered -=
                HandleNodeEntered;
        }

        subscribedRunManager =
            runManager;

        if (subscribedRunManager != null)
        {
            subscribedRunManager.StateChanged +=
                HandleRunStateChanged;

            subscribedRunManager.NextNodeSelectionRequested +=
                HandleNextNodeSelectionRequested;

            subscribedRunManager.NodeEntered +=
                HandleNodeEntered;
        }
    }

    private void UnsubscribeRunEvents()
    {
        if (subscribedRunManager == null)
            return;

        subscribedRunManager.StateChanged -=
            HandleRunStateChanged;

        subscribedRunManager.NextNodeSelectionRequested -=
            HandleNextNodeSelectionRequested;

        subscribedRunManager.NodeEntered -=
            HandleNodeEntered;

        subscribedRunManager = null;
    }

    private void HandleRunStateChanged(
        BattleRunState state)
    {
        if (state == BattleRunState.SelectingNode)
            QueueRefresh();
        else
            RestoreClipping();
    }

    private void HandleNextNodeSelectionRequested(
        IReadOnlyList<BattleNodeData> _)
    {
        QueueRefresh();
    }

    private void HandleNodeEntered(
        BattleNodeData _)
    {
        RestoreClipping();
    }

    private bool IsScriptSelectionActive()
    {
        return
            runManager != null &&
            runManager.RunActive &&
            runManager.State ==
            BattleRunState.SelectingNode;
    }

    private void QueueRefresh()
    {
        if (!isActiveAndEnabled)
            return;

        if (refreshRoutine != null)
            StopCoroutine(
                refreshRoutine);

        refreshRoutine =
            StartCoroutine(
                RefreshAfterLayout());
    }

    private IEnumerator RefreshAfterLayout()
    {
        // WorldSet reparents the legacy staging content to the dedicated
        // BattleScriptSelectionCanvas. Waiting one frame avoids fighting that move.
        yield return null;

        refreshRoutine = null;

        if (!IsScriptSelectionActive())
            yield break;

        ResolveScriptSelectionUi();
        ApplyDetachedScriptSelectionPolicy();
    }

    private void ResolveScriptSelectionUi()
    {
        if (hud == null)
            hud =
                FindFirstObjectByType<BattleHUD>();

        if (scriptSelectionContent == null &&
            hud != null)
        {
            scriptSelectionContent =
                hud.ScriptSelectionRoot;
        }

        if (scriptSelectionContent == null)
        {
            scriptSelectionContent =
                FindRect(
                    LegacyScriptSelectionContentName);
        }

        if (scriptSelectionContent == null)
            return;

        scriptSelectionGroup =
            scriptSelectionContent.GetComponent<CanvasGroup>();

        if (scriptSelectionGroup == null)
        {
            scriptSelectionGroup =
                scriptSelectionContent.gameObject
                    .AddComponent<CanvasGroup>();
        }

        scriptSelectionCanvas =
            scriptSelectionContent
                .GetComponentInParent<Canvas>();
    }

    private void ApplyDetachedScriptSelectionPolicy()
    {
        if (scriptSelectionContent == null)
            return;

        scriptSelectionContent.anchorMin =
            Vector2.zero;

        scriptSelectionContent.anchorMax =
            Vector2.one;

        scriptSelectionContent.offsetMin =
            new Vector2(
                18f,
                14f);

        scriptSelectionContent.offsetMax =
            new Vector2(
                -18f,
                -14f);

        scriptSelectionContent.pivot =
            new Vector2(
                0.5f,
                0.5f);

        scriptSelectionContent.localRotation =
            Quaternion.identity;

        if (scriptSelectionGroup != null)
        {
            scriptSelectionGroup.interactable =
                true;

            if (scriptSelectionGroup.alpha >= 0.75f)
            {
                scriptSelectionGroup.blocksRaycasts =
                    true;
            }
        }

        DisableClipping(
            scriptSelectionContent);

        if (scriptSelectionCanvas != null)
        {
            if (scriptSelectionCanvas.renderMode !=
                    RenderMode.ScreenSpaceOverlay &&
                scriptSelectionCanvas.worldCamera == null)
            {
                scriptSelectionCanvas.worldCamera =
                    Camera.main;
            }

            if (scriptSelectionCanvas
                    .GetComponent<GraphicRaycaster>() ==
                null)
            {
                scriptSelectionCanvas.gameObject
                    .AddComponent<GraphicRaycaster>();
            }
        }

        EnsureEventSystem();
    }

    private void DisableClipping(
        RectTransform root)
    {
        if (root == null)
            return;

        RectMask2D[] rectMasks =
            root.GetComponentsInParent<RectMask2D>(
                true);

        for (int i = 0;
             i < rectMasks.Length;
             i++)
        {
            RememberAndDisable(
                rectMasks[i]);
        }

        Mask[] masks =
            root.GetComponentsInParent<Mask>(
                true);

        for (int i = 0;
             i < masks.Length;
             i++)
        {
            RememberAndDisable(
                masks[i]);
        }

        RectMask2D[] childRectMasks =
            root.GetComponentsInChildren<RectMask2D>(
                true);

        for (int i = 0;
             i < childRectMasks.Length;
             i++)
        {
            RememberAndDisable(
                childRectMasks[i]);
        }

        Mask[] childMasks =
            root.GetComponentsInChildren<Mask>(
                true);

        for (int i = 0;
             i < childMasks.Length;
             i++)
        {
            RememberAndDisable(
                childMasks[i]);
        }
    }

    private void RememberAndDisable(
        Behaviour behaviour)
    {
        if (behaviour == null)
            return;

        if (!clippingStates.ContainsKey(
                behaviour))
        {
            clippingStates.Add(
                behaviour,
                behaviour.enabled);
        }

        behaviour.enabled =
            false;
    }

    private void RestoreClipping()
    {
        foreach (
            KeyValuePair<Behaviour, bool> pair
            in clippingStates)
        {
            if (pair.Key != null)
                pair.Key.enabled = pair.Value;
        }

        clippingStates.Clear();
    }

    private static RectTransform FindRect(
        string targetName)
    {
        RectTransform[] rects =
            FindObjectsByType<RectTransform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        for (int i = 0;
             i < rects.Length;
             i++)
        {
            RectTransform rect =
                rects[i];

            if (rect != null &&
                rect.name == targetName)
            {
                return rect;
            }
        }

        return null;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        GameObject go =
            new(
                "BattleScriptSelectionEventSystem");

        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }
}
