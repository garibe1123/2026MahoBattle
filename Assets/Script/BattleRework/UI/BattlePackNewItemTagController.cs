using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// BattlePackChangeFeedbackController가 유지하는 PackRecentChangeMarker를 그대로 authoritative 상태로 사용해
/// Full PACK 슬롯 좌측 상단 바깥에 작은 [NEW!] 태그를 붙입니다.
///
/// 상태를 새로 추적하지 않으므로:
/// - Reward로 실제 생성된 recent-change marker가 있는 아이템에만 태그가 보입니다.
/// - 순수 Drag / Swap은 marker를 새로 만들지 않으므로 NEW 태그도 새로 생기지 않습니다.
/// - 기존 NEW 아이템을 옮기면 marker가 이동한 슬롯을 따라 태그도 다음 프레임에 이동합니다.
/// - PACK 종료로 marker가 제거되면 태그도 제거됩니다.
/// - 선택 중인 슬롯에서는 검정 Tag + 흰 글씨의 Negative 표현으로 뒤집어 Selection Stroke와 겹쳐도 읽힙니다.
///
/// Negative Stroke는 BattleMonochromeItemVisualController가 계속 소유합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(65060)]
public sealed class BattlePackNewItemTagController : MonoBehaviour
{
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const string MarkerName = "PackRecentChangeMarker";
    private const string TagName = "PackRecentChangeNewTag";

    [SerializeField] private Vector2 tagSize = new(52f, 20f);
    [SerializeField] private Vector2 tagOffset = new(-13f, 9f);
    [SerializeField, Range(-20f, 20f)] private float tagRotation = -8f;
    [SerializeField] private Color tagColor = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color tagTextColor = new(0.025f, 0.025f, 0.030f, 1f);
    [SerializeField] private Color selectedTagColor = new(0.020f, 0.022f, 0.026f, 1f);
    [SerializeField] private Color selectedTagTextColor = Color.white;
    [SerializeField, Range(8, 16)] private int tagFontSize = 10;

    private BattleKineticLoadoutUI kineticLoadout;
    private RectTransform boardRoot;
    private readonly RectTransform[] slots = new RectTransform[SlotCount];
    private float nextResolveTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallAfterSceneLoad()
    {
        EnsureInstalled();
    }

    private static void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        EnsureInstalled();
    }

    private static void EnsureInstalled()
    {
        if (FindFirstObjectByType<BattlePackNewItemTagController>(FindObjectsInactive.Include) != null)
            return;

        BattlePackChangeFeedbackController feedback =
            FindFirstObjectByType<BattlePackChangeFeedbackController>(FindObjectsInactive.Include);
        if (feedback != null)
        {
            feedback.gameObject.AddComponent<BattlePackNewItemTagController>();
            return;
        }

        BattleUnifiedInventoryInspectController inventory =
            FindFirstObjectByType<BattleUnifiedInventoryInspectController>(FindObjectsInactive.Include);
        if (inventory != null)
        {
            inventory.gameObject.AddComponent<BattlePackNewItemTagController>();
            return;
        }

        // Installer 순서가 늦는 특수 씬도 지원하되 DDOL host는 만들지 않습니다.
        GameObject host = new("BattlePackNewItemTagPresentation");
        host.AddComponent<BattlePackNewItemTagController>();
    }

    private void OnEnable()
    {
        nextResolveTime = 0f;
        ResolveUi();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.12f;
            ResolveUi();
        }

        if (boardRoot == null)
            return;

        for (int i = 0; i < SlotCount; i++)
            SyncSlotTag(i, slots[i]);
    }

    private void ResolveUi()
    {
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);

        RectTransform nextBoard = kineticLoadout != null ? kineticLoadout.GridBoard : null;
        if (nextBoard == null)
            return;

        if (boardRoot != nextBoard)
        {
            boardRoot = nextBoard;
            for (int i = 0; i < SlotCount; i++)
                slots[i] = null;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (slots[i] == null)
                slots[i] = boardRoot.Find($"GridSlot_{i}") as RectTransform;
        }
    }

    private void SyncSlotTag(int slotIndex, RectTransform slot)
    {
        if (slot == null)
            return;

        Transform marker = slot.Find(MarkerName);
        RectTransform tag = slot.Find(TagName) as RectTransform;
        bool shouldShow = marker != null && marker.gameObject.activeSelf;

        if (!shouldShow)
        {
            if (tag != null)
            {
                tag.DOKill();
                Destroy(tag.gameObject);
            }
            return;
        }

        if (tag == null)
            tag = CreateTag(slot);

        // 좌측 상단 모서리에 걸치되 절반가량을 슬롯 밖으로 빼 실제 Sticker/Tag처럼 보이게 합니다.
        tag.anchorMin = tag.anchorMax = new Vector2(0f, 1f);
        tag.pivot = new Vector2(0.5f, 0.5f);
        tag.sizeDelta = tagSize;
        tag.anchoredPosition = tagOffset;
        tag.localRotation = Quaternion.Euler(0f, 0f, tagRotation);
        tag.SetAsLastSibling();

        Transform selectionFrame = slot.Find("UnifiedSelectionFrame");
        bool selected = selectionFrame != null && selectionFrame.gameObject.activeSelf;

        Image background = tag.GetComponent<Image>();
        if (background != null)
            background.color = selected ? selectedTagColor : tagColor;

        Outline outline = tag.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = selected
                ? new Color(1f, 1f, 1f, 0.94f)
                : new Color(0.02f, 0.02f, 0.025f, 0.92f);
        }

        Text text = tag.Find("Text")?.GetComponent<Text>();
        if (text != null)
        {
            text.text = "[NEW!]";
            text.fontSize = tagFontSize;
            text.color = selected ? selectedTagTextColor : tagTextColor;
        }
    }

    private RectTransform CreateTag(RectTransform slot)
    {
        GameObject tagObject = new(TagName);
        tagObject.transform.SetParent(slot, false);
        RectTransform tag = tagObject.AddComponent<RectTransform>();
        tag.anchorMin = tag.anchorMax = new Vector2(0f, 1f);
        tag.pivot = new Vector2(0.5f, 0.5f);
        tag.sizeDelta = tagSize;
        tag.anchoredPosition = tagOffset;
        tag.localRotation = Quaternion.Euler(0f, 0f, tagRotation);

        Image background = tagObject.AddComponent<Image>();
        background.color = tagColor;
        background.raycastTarget = false;

        Outline outline = tagObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.02f, 0.02f, 0.025f, 0.92f);
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = false;

        GameObject textObject = new("Text");
        textObject.transform.SetParent(tag, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(3f, 1f);
        textRect.offsetMax = new Vector2(-3f, -1f);

        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = tagFontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = tagTextColor;
        text.raycastTarget = false;
        text.text = "[NEW!]";

        tag.localScale = Vector3.one * 0.72f;
        tag.DOScale(Vector3.one, 0.14f)
            .SetEase(Ease.OutBack)
            .SetUpdate(true);

        return tag;
    }
}
