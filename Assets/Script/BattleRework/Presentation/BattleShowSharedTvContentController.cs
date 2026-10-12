using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reward TV content compatibility owner.
///
/// Script Selection no longer lives inside the mounted TV. This controller now
/// has one job: keep Reward content inside the shared TV viewport and keep the
/// legacy MapSelectionScreen disabled after BattleShowWorldSetController has
/// moved the staging shells into the World-Space show hierarchy.
///
/// Legacy object names are still resolved because BattleHUD keeps them as
/// staging compatibility objects; no Map/route presentation is rendered here.
/// </summary>
[DefaultExecutionOrder(32350)]
[DisallowMultipleComponent]
public sealed class BattleShowSharedTvContentController : MonoBehaviour
{
    private const string SharedFrameName = "PrizeSelectionScreen";
    private const string LegacyScriptSelectionFrameName = "MapSelectionScreen";
    private const string ScreenInnerName = "ScreenInner";
    private const string MountedTvName = "BattleShowMountedTV";
    private const string ViewportName = "ShowContentViewport";
    private const string RewardViewName = "RewardSelectionContent";
    private const string RewardCardRootName = "PrizeChoices";
    private const string RewardNoticeRootName = "PlacementNotice";

    private BattleRunManager runManager;

    private RectTransform sharedFrame;
    private RectTransform legacyScriptSelectionFrame;
    private RectTransform screenInner;
    private RectTransform viewport;
    private RectTransform rewardView;
    private RectTransform rewardCardRoot;
    private RectTransform rewardNoticeRoot;
    private CanvasGroup viewportGroup;

    private bool bound;

    private void OnEnable()
    {
        bound = false;
    }

    private void Update()
    {
        ResolveSystems();

        if (!bound)
        {
            if (!TryBindSharedScreen())
                return;
        }

        ApplyRewardState();
    }

    private void LateUpdate()
    {
        if (!bound)
            return;

        // WorldSet/BattleHUD may touch the legacy staging shell earlier in the
        // frame. Script Selection is detached, so it must never reappear on TV.
        if (legacyScriptSelectionFrame != null &&
            legacyScriptSelectionFrame.gameObject.activeSelf)
        {
            legacyScriptSelectionFrame.gameObject.SetActive(false);
        }

        ApplyRewardState();
    }

    private void ResolveSystems()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
    }

    private bool TryBindSharedScreen()
    {
        sharedFrame =
            FindRect(
                SharedFrameName);

        legacyScriptSelectionFrame =
            FindRect(
                LegacyScriptSelectionFrameName);

        if (sharedFrame == null ||
            legacyScriptSelectionFrame == null)
        {
            return false;
        }

        // WorldSet must finish moving both staging shells first.
        Transform tvParent =
            sharedFrame.parent;

        if (tvParent == null ||
            tvParent.name != MountedTvName ||
            legacyScriptSelectionFrame.parent != tvParent)
        {
            return false;
        }

        screenInner =
            sharedFrame.Find(
                ScreenInnerName)
            as RectTransform;

        if (screenInner == null)
            return false;

        BuildSharedViewport();

        legacyScriptSelectionFrame.gameObject.SetActive(
            false);

        bound =
            viewport != null &&
            rewardView != null;

        return bound;
    }

    private void BuildSharedViewport()
    {
        Transform existingViewport =
            screenInner.Find(
                ViewportName);

        if (existingViewport is RectTransform existingRect)
        {
            viewport =
                existingRect;

            rewardView =
                viewport.Find(
                    RewardViewName)
                as RectTransform;

            viewportGroup =
                viewport.GetComponent<CanvasGroup>() ??
                viewport.gameObject.AddComponent<CanvasGroup>();

            ResolveRewardInteractionRoots();
            KeepRewardInteractionRootsDirect();
            return;
        }

        List<Transform> rewardChildren =
            new();

        for (int i = 0;
             i < screenInner.childCount;
             i++)
        {
            rewardChildren.Add(
                screenInner.GetChild(i));
        }

        GameObject viewportObject =
            new(
                ViewportName);

        viewportObject.transform.SetParent(
            screenInner,
            false);

        viewport =
            viewportObject.AddComponent<RectTransform>();

        Stretch(
            viewport);

        viewportGroup =
            viewportObject.AddComponent<CanvasGroup>();

        GameObject rewardObject =
            new(
                RewardViewName);

        rewardObject.transform.SetParent(
            viewport,
            false);

        rewardView =
            rewardObject.AddComponent<RectTransform>();

        Stretch(
            rewardView);

        for (int i = 0;
             i < rewardChildren.Count;
             i++)
        {
            Transform child =
                rewardChildren[i];

            if (child != null &&
                child != viewport)
            {
                child.SetParent(
                    rewardView,
                    false);
            }
        }

        ResolveRewardInteractionRoots();
        KeepRewardInteractionRootsDirect();
    }

    private void ResolveRewardInteractionRoots()
    {
        if (rewardCardRoot == null)
        {
            rewardCardRoot =
                FindChildRect(
                    rewardView,
                    RewardCardRootName) ??
                FindChildRect(
                    screenInner,
                    RewardCardRootName);
        }

        if (rewardNoticeRoot == null)
        {
            rewardNoticeRoot =
                FindChildRect(
                    rewardView,
                    RewardNoticeRootName) ??
                FindChildRect(
                    screenInner,
                    RewardNoticeRootName);
        }
    }

    private void KeepRewardInteractionRootsDirect()
    {
        if (screenInner == null)
            return;

        if (rewardCardRoot != null &&
            rewardCardRoot.parent != screenInner)
        {
            rewardCardRoot.SetParent(
                screenInner,
                false);
        }

        if (rewardNoticeRoot != null &&
            rewardNoticeRoot.parent != screenInner)
        {
            rewardNoticeRoot.SetParent(
                screenInner,
                false);
        }
    }

    private void ApplyRewardState()
    {
        bool showReward =
            runManager != null &&
            runManager.RunActive &&
            runManager.State ==
            BattleRunState.Reward;

        if (legacyScriptSelectionFrame != null &&
            legacyScriptSelectionFrame.gameObject.activeSelf)
        {
            legacyScriptSelectionFrame.gameObject.SetActive(
                false);
        }

        if (rewardView != null &&
            rewardView.gameObject.activeSelf != showReward)
        {
            rewardView.gameObject.SetActive(
                showReward);
        }

        if (rewardCardRoot != null &&
            rewardCardRoot.gameObject.activeSelf != showReward)
        {
            rewardCardRoot.gameObject.SetActive(
                showReward);
        }

        // RewardCardActionController owns PlacementNotice during Reward.
        // Outside Reward this compatibility layer only guarantees it is hidden.
        if (!showReward &&
            rewardNoticeRoot != null &&
            rewardNoticeRoot.gameObject.activeSelf)
        {
            rewardNoticeRoot.gameObject.SetActive(
                false);
        }

        if (viewportGroup != null)
        {
            viewportGroup.alpha =
                1f;

            viewportGroup.interactable =
                showReward;

            viewportGroup.blocksRaycasts =
                showReward;
        }
    }

    private static RectTransform FindChildRect(
        Transform root,
        string targetName)
    {
        if (root == null)
            return null;

        RectTransform[] rects =
            root.GetComponentsInChildren<RectTransform>(
                true);

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

    private static void Stretch(
        RectTransform rect)
    {
        if (rect == null)
            return;

        rect.anchorMin =
            Vector2.zero;

        rect.anchorMax =
            Vector2.one;

        rect.offsetMin =
            Vector2.zero;

        rect.offsetMax =
            Vector2.zero;

        rect.localScale =
            Vector3.one;

        rect.localRotation =
            Quaternion.identity;

        rect.anchoredPosition =
            Vector2.zero;
    }
}
