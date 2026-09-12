using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reward PACK 편집 중 장비가 실제로 어떻게 바뀌었는지 짧고 명확하게 보여주는 시각 전용 컨트롤러입니다.
///
/// BattleEquipmentSystem의 authoritative 데이터를 수정하지 않습니다.
/// InventoryChanged 전/후 3x3 슬롯 스냅샷을 비교해 변경 슬롯만 연출합니다.
/// 단순 Drag 이동/Swap처럼 PACK 안의 아이템 구성이 그대로인 재배치는 새 변경으로 취급하지 않습니다.
///
/// 기본 연출:
/// - 변경되지 않은 슬롯을 잠깐 어둡게 눌러 시선을 한 곳으로 모읍니다.
/// - 변경 슬롯은 Field Dock과 같은 큰 충돌 -> 반동 -> 정착 리듬으로 반응합니다.
/// - 교체/삭제 시 기존 아이콘은 고스트로 빠져나갑니다.
/// - 추가/교체/합성 시 현재 아이콘은 슬롯 안으로 짧게 들어와 정착합니다.
/// - 슬롯 위에는 짧은 OLD -> NEW 비교를 표시합니다.
/// - Reward로 실제 유입/교체/합성된 아이템의 최근 변경 마커는 PACK을 닫을 때까지 유지합니다.
/// - 그 아이템을 PACK 안에서 이동하면 마커는 새 슬롯을 따라가며 새 마커를 만들지 않습니다.
///
/// 카메라, TimeScale, Reward 규칙은 소유하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33520)]
public sealed class BattlePackChangeFeedbackController : MonoBehaviour
{
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;

    private enum ChangeKind
    {
        Added,
        Removed,
        Replaced,
        Progressed
    }

    private struct SlotSnapshot
    {
        public BattleEquipmentSO equipment;
        public int grade;
        public int copies;

        public bool SameAs(SlotSnapshot other)
        {
            return equipment == other.equipment && grade == other.grade && copies == other.copies;
        }
    }

    private struct SlotChange
    {
        public int index;
        public SlotSnapshot before;
        public SlotSnapshot after;
        public ChangeKind kind;
    }

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleRewardFlow rewardFlow;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;

    [Header("Focus")]
    [SerializeField, Range(0.45f, 0.90f)] private float otherSlotAlpha = 0.72f;
    [SerializeField, Min(0.01f)] private float dimInDuration = 0.08f;
    [SerializeField, Min(0.05f)] private float focusHoldDuration = 0.56f;
    [SerializeField, Min(0.01f)] private float dimOutDuration = 0.18f;

    [Header("Slot Impact")]
    [SerializeField, Range(1.02f, 1.30f)] private float impactScale = 1.14f;
    [SerializeField, Range(0.80f, 0.99f)] private float reboundScale = 0.96f;
    [SerializeField, Range(1.01f, 1.15f)] private float settleOvershoot = 1.04f;
    [SerializeField, Min(0f)] private float impactDelay = 0.18f;

    [Header("Icon Motion")]
    [SerializeField] private Vector2 incomingIconOffset = new(0f, -22f);
    [SerializeField] private Vector2 outgoingIconOffset = new(-24f, 30f);
    [SerializeField, Range(0.4f, 1f)] private float incomingStartScale = 0.76f;

    [Header("Change Marker")]
    [SerializeField] private Color addedColor = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color replacedColor = new(1f, 0.18f, 0.52f, 1f);
    [SerializeField] private Color progressedColor = new(0.15f, 0.88f, 0.92f, 1f);
    [SerializeField] private Color removedColor = new(0.72f, 0.72f, 0.76f, 1f);

    private RectTransform boardRoot;
    private readonly RectTransform[] slotRects = new RectTransform[SlotCount];
    private readonly Image[] slotIcons = new Image[SlotCount];
    private readonly CanvasGroup[] slotGroups = new CanvasGroup[SlotCount];
    private readonly GameObject[] changeMarkers = new GameObject[SlotCount];
    private readonly SlotSnapshot[] snapshots = new SlotSnapshot[SlotCount];
    private readonly List<GameObject> transientVisuals = new();

    private BattleEquipmentSystem subscribedEquipmentSystem;
    private Coroutine presentationRoutine;
    private bool snapshotInitialized;
    private bool wasPackEditing;
    private float nextResolveTime;

    private void Awake()
    {
        ResolveReferences();
        ResolveUi();
        SubscribeEquipment();
        CaptureSnapshot();
        wasPackEditing = IsPackEditing();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResolveUi();
        SubscribeEquipment();
        CaptureSnapshot();
        wasPackEditing = IsPackEditing();
        nextResolveTime = 0f;
    }

    private void OnDisable()
    {
        UnsubscribeEquipment();
        StopPresentation(true, true);
        wasPackEditing = false;
    }

    private void OnDestroy()
    {
        UnsubscribeEquipment();
        StopPresentation(true, true);
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.15f;
            ResolveReferences();
            ResolveUi();
            SubscribeEquipment();
            if (!snapshotInitialized)
                CaptureSnapshot();
        }

        bool packEditing = IsPackEditing();
        if (wasPackEditing && !packEditing)
            StopPresentation(true, true);

        wasPackEditing = packEditing;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (rewardFlow == null)
            rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
    }

    private void SubscribeEquipment()
    {
        if (subscribedEquipmentSystem == equipmentSystem)
            return;

        UnsubscribeEquipment();
        subscribedEquipmentSystem = equipmentSystem;
        if (subscribedEquipmentSystem == null)
            return;

        CaptureSnapshot();
        subscribedEquipmentSystem.InventoryChanged += HandleInventoryChanged;
    }

    private void UnsubscribeEquipment()
    {
        if (subscribedEquipmentSystem != null)
            subscribedEquipmentSystem.InventoryChanged -= HandleInventoryChanged;
        subscribedEquipmentSystem = null;
    }

    private void HandleInventoryChanged()
    {
        if (equipmentSystem == null)
            return;

        if (!snapshotInitialized)
        {
            CaptureSnapshot();
            return;
        }

        List<SlotChange> changes = new();
        for (int i = 0; i < SlotCount; i++)
        {
            SlotSnapshot current = ReadSlot(i);
            SlotSnapshot previous = snapshots[i];
            if (!previous.SameAs(current))
            {
                ChangeKind? kind = ResolveChangeKind(previous, current);
                if (kind.HasValue)
                {
                    changes.Add(new SlotChange
                    {
                        index = i,
                        before = previous,
                        after = current,
                        kind = kind.Value
                    });
                }
            }

            snapshots[i] = current;
        }

        if (changes.Count == 0 || !IsRewardMutationWindow())
            return;

        // Drag 이동/Swap은 슬롯 위치만 달라질 뿐 PACK 안의 실제 아이템 멀티셋은 동일합니다.
        // 이 경우 NEW/CHANGED 연출을 새로 만들지 않고, 기존 마커가 있으면 그 아이템을 따라 이동만 합니다.
        if (IsPureRearrangement(changes))
        {
            StopPresentation(false, false);
            TransferMarkersForRearrangement(changes);
            RestoreSlotAlphas();
            return;
        }

        if (presentationRoutine != null)
            StopCoroutine(presentationRoutine);
        presentationRoutine = StartCoroutine(PresentWhenPackReady(changes));
    }

    private bool IsRewardMutationWindow()
    {
        if (runManager == null || !runManager.RunActive || runManager.State != BattleRunState.Reward || rewardFlow == null)
            return false;

        return rewardFlow.Phase == BattleRewardPhase.Choosing ||
               rewardFlow.Phase == BattleRewardPhase.PackEditing;
    }

    private bool IsPackEditing()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward &&
               rewardFlow != null && rewardFlow.Phase == BattleRewardPhase.PackEditing;
    }

    private IEnumerator PresentWhenPackReady(List<SlotChange> changes)
    {
        // 빈 슬롯에 Reward가 자동 저장되는 경우 InventoryChanged가 PackEditing 전환보다 먼저 옵니다.
        // 한 프레임 이상 기다려 Full PACK이 실제로 열린 뒤 연출합니다.
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
            ResolveReferences();
            ResolveUi();
            if (IsPackEditing() && boardRoot != null && boardRoot.gameObject.activeInHierarchy)
                break;
        }

        if (!IsPackEditing() || boardRoot == null)
        {
            presentationRoutine = null;
            yield break;
        }

        // KineticLoadoutUI의 InventoryChanged RefreshAll이 슬롯 Scale/Icon을 먼저 확정하도록 한 프레임 더 양보합니다.
        yield return null;
        ResolveUi();
        if (!IsPackEditing())
        {
            presentationRoutine = null;
            yield break;
        }

        PlayChanges(changes);
        presentationRoutine = null;
    }

    private void PlayChanges(List<SlotChange> changes)
    {
        if (changes == null || changes.Count == 0)
            return;

        StopPresentation(false, false);

        bool[] changed = new bool[SlotCount];
        for (int i = 0; i < changes.Count; i++)
        {
            int index = changes[i].index;
            if (index >= 0 && index < SlotCount)
                changed[index] = true;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            CanvasGroup group = slotGroups[i];
            if (group == null)
                continue;

            group.DOKill();
            group.DOFade(changed[i] ? 1f : otherSlotAlpha, dimInDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }

        for (int i = 0; i < changes.Count; i++)
        {
            SlotChange change = changes[i];
            if (change.index < 0 || change.index >= SlotCount || slotRects[change.index] == null)
                continue;

            // EMPTY가 된 슬롯은 '새 아이템' 마커를 남기지 않습니다.
            if (change.kind != ChangeKind.Removed && change.after.equipment != null)
                EnsureChangeMarker(change.index, change.kind);

            PlaySlotImpact(change.index);
            PlayOutgoingGhost(change);
            PlayIncomingIcon(change);
            CreateComparisonCard(change);
        }

        Sequence restore = DOTween.Sequence().SetUpdate(true);
        restore.AppendInterval(Mathf.Max(0.05f, focusHoldDuration));
        restore.AppendCallback(RestoreSlotAlphas);
    }

    private void PlaySlotImpact(int index)
    {
        RectTransform slot = slotRects[index];
        if (slot == null)
            return;

        slot.DOKill();
        slot.localScale = Vector3.one;

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.AppendInterval(Mathf.Max(0f, impactDelay));
        sequence.Append(slot.DOScale(Vector3.one * impactScale, 0.08f).SetEase(Ease.OutQuad));
        sequence.Append(slot.DOScale(Vector3.one * reboundScale, 0.07f).SetEase(Ease.InOutQuad));
        sequence.Append(slot.DOScale(Vector3.one * settleOvershoot, 0.07f).SetEase(Ease.OutQuad));
        sequence.Append(slot.DOScale(Vector3.one, 0.11f).SetEase(Ease.OutCubic));
    }

    private void PlayOutgoingGhost(SlotChange change)
    {
        if (change.before.equipment == null || change.before.equipment.icon == null)
            return;
        if (change.kind != ChangeKind.Replaced && change.kind != ChangeKind.Removed)
            return;

        RectTransform slot = slotRects[change.index];
        Image sourceIcon = slotIcons[change.index];
        if (slot == null || sourceIcon == null)
            return;

        GameObject ghostObject = new("PackChange_OldGhost");
        ghostObject.transform.SetParent(slot, false);
        RectTransform ghost = ghostObject.AddComponent<RectTransform>();
        CopyRectGeometry(sourceIcon.rectTransform, ghost);

        Image ghostImage = ghostObject.AddComponent<Image>();
        ghostImage.sprite = change.before.equipment.icon;
        ghostImage.preserveAspect = true;
        ghostImage.raycastTarget = false;
        ghostImage.color = Color.white;

        CanvasGroup group = ghostObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        transientVisuals.Add(ghostObject);
        Vector2 start = ghost.anchoredPosition;

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.AppendInterval(0.04f);
        sequence.Append(ghost.DOAnchorPos(start + outgoingIconOffset, 0.26f).SetEase(Ease.OutCubic));
        sequence.Join(group.DOFade(0f, 0.24f));
        sequence.Join(ghost.DOScale(Vector3.one * 0.82f, 0.26f));
        sequence.OnComplete(() => DestroyTransient(ghostObject));
    }

    private void PlayIncomingIcon(SlotChange change)
    {
        if (change.after.equipment == null || change.kind == ChangeKind.Removed)
            return;

        Image icon = slotIcons[change.index];
        if (icon == null || !icon.gameObject.activeInHierarchy)
            return;

        RectTransform rect = icon.rectTransform;
        CanvasGroup group = icon.GetComponent<CanvasGroup>();
        if (group == null)
            group = icon.gameObject.AddComponent<CanvasGroup>();

        rect.DOKill();
        group.DOKill();

        Vector2 basePosition = rect.anchoredPosition;
        Vector3 baseScale = Vector3.one;

        if (change.kind == ChangeKind.Progressed)
        {
            rect.localScale = Vector3.one * 0.90f;
            Sequence progress = DOTween.Sequence().SetUpdate(true);
            progress.AppendInterval(0.10f);
            progress.Append(rect.DOScale(Vector3.one * 1.12f, 0.10f).SetEase(Ease.OutQuad));
            progress.Append(rect.DOScale(baseScale, 0.14f).SetEase(Ease.OutCubic));
            return;
        }

        rect.anchoredPosition = basePosition + incomingIconOffset;
        rect.localScale = Vector3.one * incomingStartScale;
        group.alpha = 0.18f;

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.AppendInterval(0.11f);
        sequence.Append(rect.DOAnchorPos(basePosition, 0.13f).SetEase(Ease.OutCubic));
        sequence.Join(group.DOFade(1f, 0.10f));
        sequence.Join(rect.DOScale(Vector3.one * 1.10f, 0.13f).SetEase(Ease.OutQuad));
        sequence.Append(rect.DOScale(baseScale, 0.12f).SetEase(Ease.OutCubic));
    }

    private void CreateComparisonCard(SlotChange change)
    {
        RectTransform slot = slotRects[change.index];
        if (slot == null)
            return;

        string label = BuildComparisonLabel(change);
        if (string.IsNullOrEmpty(label))
            return;

        GameObject cardObject = new("PackChange_Comparison");
        cardObject.transform.SetParent(slot, false);
        RectTransform card = cardObject.AddComponent<RectTransform>();
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 1f);
        card.pivot = new Vector2(0.5f, 0f);
        card.sizeDelta = new Vector2(254f, 42f);
        card.anchoredPosition = new Vector2(0f, 20f);

        Image back = cardObject.AddComponent<Image>();
        back.color = new Color(0.035f, 0.030f, 0.055f, 0.96f);
        back.raycastTarget = false;

        Outline outline = cardObject.AddComponent<Outline>();
        outline.effectColor = ResolveMarkerColor(change.kind);
        outline.effectDistance = new Vector2(3f, -3f);

        GameObject textObject = new("Text");
        textObject.transform.SetParent(card, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 3f);
        textRect.offsetMax = new Vector2(-8f, -3f);

        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 11;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.94f, 0.90f, 0.76f, 1f);
        text.raycastTarget = false;
        text.text = label;

        CanvasGroup group = cardObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        transientVisuals.Add(cardObject);

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.AppendInterval(0.24f);
        sequence.Append(group.DOFade(1f, 0.08f));
        sequence.Join(card.DOAnchorPosY(28f, 0.10f).SetEase(Ease.OutCubic));
        sequence.AppendInterval(0.32f);
        sequence.Append(group.DOFade(0f, 0.12f));
        sequence.OnComplete(() => DestroyTransient(cardObject));
    }

    private void EnsureChangeMarker(int index, ChangeKind kind)
    {
        RectTransform slot = slotRects[index];
        if (slot == null)
            return;

        GameObject marker = changeMarkers[index];
        if (marker == null)
        {
            marker = new GameObject("PackRecentChangeMarker");
            marker.transform.SetParent(slot, false);
            RectTransform rect = marker.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(15f, 15f);
            rect.anchoredPosition = new Vector2(-8f, -8f);
            rect.localRotation = Quaternion.Euler(0f, 0f, 45f);

            Image image = marker.AddComponent<Image>();
            image.raycastTarget = false;

            Outline outline = marker.AddComponent<Outline>();
            outline.effectColor = new Color(0.035f, 0.030f, 0.055f, 0.95f);
            outline.effectDistance = new Vector2(2f, -2f);

            changeMarkers[index] = marker;
        }

        Image markerImage = marker.GetComponent<Image>();
        if (markerImage != null)
            markerImage.color = ResolveMarkerColor(kind);

        marker.SetActive(true);
        marker.transform.SetAsLastSibling();
        marker.transform.DOKill();
        marker.transform.localScale = Vector3.zero;
        marker.transform.DOScale(Vector3.one, 0.16f)
            .SetEase(Ease.OutBack)
            .SetUpdate(true);
    }

    private void TransferMarkersForRearrangement(List<SlotChange> changes)
    {
        if (changes == null || changes.Count == 0)
            return;

        GameObject[] sourceMarkers = new GameObject[changes.Count];
        bool[] consumed = new bool[changes.Count];

        for (int i = 0; i < changes.Count; i++)
        {
            int index = changes[i].index;
            if (index < 0 || index >= SlotCount)
                continue;

            sourceMarkers[i] = changeMarkers[index];
            changeMarkers[index] = null;
        }

        for (int target = 0; target < changes.Count; target++)
        {
            SlotChange targetChange = changes[target];
            if (targetChange.after.equipment == null ||
                targetChange.index < 0 || targetChange.index >= SlotCount ||
                slotRects[targetChange.index] == null)
                continue;

            for (int source = 0; source < changes.Count; source++)
            {
                if (consumed[source] || sourceMarkers[source] == null)
                    continue;
                if (!changes[source].before.SameAs(targetChange.after))
                    continue;

                GameObject marker = sourceMarkers[source];
                consumed[source] = true;
                marker.transform.DOKill();
                marker.transform.SetParent(slotRects[targetChange.index], false);

                RectTransform rect = marker.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = new Vector2(15f, 15f);
                    rect.anchoredPosition = new Vector2(-8f, -8f);
                    rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    rect.localScale = Vector3.one;
                }

                marker.SetActive(true);
                marker.transform.SetAsLastSibling();
                changeMarkers[targetChange.index] = marker;
                break;
            }
        }

        for (int i = 0; i < sourceMarkers.Length; i++)
        {
            GameObject marker = sourceMarkers[i];
            if (marker == null || consumed[i])
                continue;
            marker.transform.DOKill();
            Destroy(marker);
        }
    }

    private void RestoreSlotAlphas()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            CanvasGroup group = slotGroups[i];
            if (group == null)
                continue;

            group.DOKill();
            group.DOFade(1f, dimOutDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }
    }

    private void StopPresentation(bool clearMarkers, bool resetSnapshot)
    {
        if (presentationRoutine != null)
        {
            StopCoroutine(presentationRoutine);
            presentationRoutine = null;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (slotRects[i] != null)
            {
                slotRects[i].DOKill();
                slotRects[i].localScale = Vector3.one;
            }

            if (slotGroups[i] != null)
            {
                slotGroups[i].DOKill();
                slotGroups[i].alpha = 1f;
            }

            if (slotIcons[i] != null)
            {
                RectTransform iconRect = slotIcons[i].rectTransform;
                iconRect.DOKill();
                iconRect.localScale = Vector3.one;
                CanvasGroup iconGroup = slotIcons[i].GetComponent<CanvasGroup>();
                if (iconGroup != null)
                {
                    iconGroup.DOKill();
                    iconGroup.alpha = 1f;
                }
            }

            if (clearMarkers && changeMarkers[i] != null)
            {
                changeMarkers[i].transform.DOKill();
                Destroy(changeMarkers[i]);
                changeMarkers[i] = null;
            }
        }

        for (int i = transientVisuals.Count - 1; i >= 0; i--)
        {
            GameObject transient = transientVisuals[i];
            if (transient == null)
                continue;
            transient.transform.DOKill();
            CanvasGroup group = transient.GetComponent<CanvasGroup>();
            group?.DOKill();
            Destroy(transient);
        }
        transientVisuals.Clear();

        if (resetSnapshot)
            CaptureSnapshot();
    }

    private void DestroyTransient(GameObject target)
    {
        if (target == null)
            return;

        transientVisuals.Remove(target);
        target.transform.DOKill();
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        group?.DOKill();
        Destroy(target);
    }

    private void ResolveUi()
    {
        if (kineticLoadout == null)
            return;

        RectTransform nextBoard = kineticLoadout.GridBoard;
        if (nextBoard == null)
            return;

        if (boardRoot != nextBoard)
        {
            boardRoot = nextBoard;
            for (int i = 0; i < SlotCount; i++)
            {
                slotRects[i] = null;
                slotIcons[i] = null;
                slotGroups[i] = null;
                changeMarkers[i] = null;
            }
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (slotRects[i] == null)
                slotRects[i] = boardRoot.Find($"GridSlot_{i}") as RectTransform;
            if (slotRects[i] == null)
                continue;

            if (slotIcons[i] == null)
            {
                Transform iconTransform = slotRects[i].Find("Icon");
                if (iconTransform != null)
                    slotIcons[i] = iconTransform.GetComponent<Image>();
            }

            if (slotGroups[i] == null)
            {
                slotGroups[i] = slotRects[i].GetComponent<CanvasGroup>();
                if (slotGroups[i] == null)
                    slotGroups[i] = slotRects[i].gameObject.AddComponent<CanvasGroup>();
            }

            if (changeMarkers[i] == null)
            {
                Transform marker = slotRects[i].Find("PackRecentChangeMarker");
                if (marker != null)
                    changeMarkers[i] = marker.gameObject;
            }
        }
    }

    private void CaptureSnapshot()
    {
        if (equipmentSystem == null)
        {
            snapshotInitialized = false;
            return;
        }

        for (int i = 0; i < SlotCount; i++)
            snapshots[i] = ReadSlot(i);
        snapshotInitialized = true;
    }

    private SlotSnapshot ReadSlot(int index)
    {
        if (equipmentSystem == null || index < 0 || index >= equipmentSystem.Slots.Count)
            return default;

        BattleEquipmentSlot slot = equipmentSystem.Slots[index];
        if (slot == null || slot.equipment == null)
            return default;

        return new SlotSnapshot
        {
            equipment = slot.equipment,
            grade = slot.grade,
            copies = slot.copies
        };
    }

    private static ChangeKind? ResolveChangeKind(SlotSnapshot before, SlotSnapshot after)
    {
        if (before.equipment == null && after.equipment != null)
            return ChangeKind.Added;
        if (before.equipment != null && after.equipment == null)
            return ChangeKind.Removed;
        if (before.equipment != after.equipment)
            return ChangeKind.Replaced;
        if (before.equipment != null && (before.grade != after.grade || before.copies != after.copies))
            return ChangeKind.Progressed;
        return null;
    }

    private static bool IsPureRearrangement(List<SlotChange> changes)
    {
        if (changes == null || changes.Count < 2)
            return false;

        bool[] matchedAfter = new bool[changes.Count];
        for (int beforeIndex = 0; beforeIndex < changes.Count; beforeIndex++)
        {
            bool matched = false;
            SlotSnapshot before = changes[beforeIndex].before;
            for (int afterIndex = 0; afterIndex < changes.Count; afterIndex++)
            {
                if (matchedAfter[afterIndex])
                    continue;
                if (!before.SameAs(changes[afterIndex].after))
                    continue;

                matchedAfter[afterIndex] = true;
                matched = true;
                break;
            }

            if (!matched)
                return false;
        }

        return true;
    }

    private string BuildComparisonLabel(SlotChange change)
    {
        string beforeName = ShortName(change.before.equipment);
        string afterName = ShortName(change.after.equipment);

        return change.kind switch
        {
            ChangeKind.Added => $"NEW  //  {afterName}",
            ChangeKind.Removed => $"{beforeName}  >  EMPTY",
            ChangeKind.Replaced => $"{beforeName}  >  {afterName}",
            ChangeKind.Progressed when change.before.grade != change.after.grade =>
                $"{afterName}  G{change.before.grade} > G{change.after.grade}",
            ChangeKind.Progressed =>
                $"{afterName}  COPY {change.before.copies} > {change.after.copies}",
            _ => string.Empty
        };
    }

    private static string ShortName(BattleEquipmentSO equipment)
    {
        if (equipment == null)
            return "EMPTY";

        string value = equipment.GetDisplayName();
        if (string.IsNullOrWhiteSpace(value))
            value = equipment.name;
        value = value.ToUpperInvariant();
        return value.Length <= 15 ? value : value.Substring(0, 14) + "…";
    }

    private Color ResolveMarkerColor(ChangeKind kind)
    {
        return kind switch
        {
            ChangeKind.Added => addedColor,
            ChangeKind.Replaced => replacedColor,
            ChangeKind.Progressed => progressedColor,
            ChangeKind.Removed => removedColor,
            _ => addedColor
        };
    }

    private static void CopyRectGeometry(RectTransform source, RectTransform target)
    {
        if (source == null || target == null)
            return;

        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.anchoredPosition = source.anchoredPosition;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }
}
