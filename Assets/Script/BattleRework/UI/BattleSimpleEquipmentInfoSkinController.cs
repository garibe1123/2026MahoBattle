using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 장비 설명 UI의 시각 복잡도만 낮추는 Presentation Skin입니다.
///
/// 데이터/선택/Reward 규칙은 건드리지 않습니다.
/// - BattleEquipmentDetailPanelController가 만든 PACK 상세 패널의 장식을 정리합니다.
/// - BattleRewardCardActionController가 만든 선택 카드 내부 설명을 더 단순한 정보 블록으로 정리합니다.
///
/// 별도 DDOL Host를 만들지 않고 현재 BattleSceneManager GameObject에만 설치합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(65020)]
public sealed class BattleSimpleEquipmentInfoSkinController : MonoBehaviour
{
    [Header("Simple Theme")]
    [SerializeField] private Color panelColor = new(0.032f, 0.034f, 0.041f, 0.965f);
    [SerializeField] private Color softPanelColor = new(1f, 1f, 1f, 0.045f);
    [SerializeField] private Color outlineColor = new(1f, 1f, 1f, 0.13f);
    [SerializeField] private Color primaryText = new(0.92f, 0.92f, 0.92f, 1f);
    [SerializeField] private Color secondaryText = new(0.61f, 0.63f, 0.68f, 1f);

    private BattleEquipmentDetailPanelController detailController;
    private RectTransform detailRoot;
    private Image detailBack;
    private Outline detailOutline;
    private Image descriptionBack;

    private Text detailSlot;
    private Text detailTitle;
    private Text detailRarity;
    private Text detailState;
    private Text detailDescription;
    private Text detailStatHeader;
    private Text detailStats;
    private Text detailTagHeader;
    private Text detailTags;
    private Text detailSynergyTitle;
    private Text detailSynergy;

    private RectTransform rewardInlineRoot;
    private Image rewardInlineBack;
    private Text rewardDescriptionHeader;
    private Text rewardDescription;
    private Text rewardEffectsHeader;
    private Text rewardEffects;
    private Text rewardTags;

    private int styledDetailRootId;
    private int styledRewardInlineId;
    private float nextResolveTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallIntoCurrentBattleScene()
    {
        EnsureInstalled();
    }

    private static void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        EnsureInstalled();
    }

    private static void EnsureInstalled()
    {
        if (FindFirstObjectByType<BattleSimpleEquipmentInfoSkinController>(FindObjectsInactive.Include) != null)
            return;

        BattleSceneManager sceneManager = FindFirstObjectByType<BattleSceneManager>(FindObjectsInactive.Include);
        if (sceneManager != null)
            sceneManager.gameObject.AddComponent<BattleSimpleEquipmentInfoSkinController>();
    }

    private void Awake()
    {
        ResolveAndStyle();
    }

    private void OnEnable()
    {
        nextResolveTime = 0f;
        ResolveAndStyle();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.20f;
            ResolveAndStyle();
        }

        MaintainDetailStyle();
        MaintainRewardInlineStyle();
    }

    private void ResolveAndStyle()
    {
        ResolveDetailPanel();
        ResolveRewardInline();
    }

    private void ResolveDetailPanel()
    {
        if (detailController == null)
            detailController = FindFirstObjectByType<BattleEquipmentDetailPanelController>(FindObjectsInactive.Include);

        RectTransform resolved = detailController != null ? detailController.Root : null;
        if (resolved == null)
            return;

        int id = resolved.GetInstanceID();
        if (detailRoot == resolved && styledDetailRootId == id)
            return;

        detailRoot = resolved;
        styledDetailRootId = id;

        detailBack = detailRoot.GetComponent<Image>();
        detailOutline = detailRoot.GetComponent<Outline>();

        Transform desc = detailRoot.Find("DescriptionBack");
        descriptionBack = desc != null ? desc.GetComponent<Image>() : null;
        detailDescription = desc != null ? desc.GetComponentInChildren<Text>(true) : null;

        detailSlot = null;
        detailTitle = null;
        detailRarity = null;
        detailState = null;
        detailStatHeader = null;
        detailStats = null;
        detailTagHeader = null;
        detailTags = null;
        detailSynergyTitle = null;
        detailSynergy = null;

        CacheDetailTexts();
        HideDetailDecoration();
        ApplyDetailLayout();
        MaintainDetailStyle();
    }

    private void CacheDetailTexts()
    {
        if (detailRoot == null)
            return;

        Text[] texts = detailRoot.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null || text == detailDescription)
                continue;

            string value = text.text ?? string.Empty;
            bool direct = text.transform.parent == detailRoot;

            if (detailStatHeader == null && value == "PARAMETERS")
            {
                detailStatHeader = text;
                continue;
            }
            if (detailTagHeader == null && value == "TAGS")
            {
                detailTagHeader = text;
                continue;
            }
            if (detailSynergyTitle == null && (value == "GRID LINK" || value == "PACK / MERGE"))
            {
                detailSynergyTitle = text;
                continue;
            }
            if (!direct)
                continue;

            RectTransform rect = text.rectTransform;
            if (detailTitle == null && text.fontSize >= 20)
            {
                detailTitle = text;
                continue;
            }
            if (detailSlot == null && text.alignment == TextAnchor.MiddleRight && rect.anchorMin.y >= 0.88f)
            {
                detailSlot = text;
                continue;
            }
            if (detailStats == null && value.Contains("DMG") && value.Contains("MOVE") && value.Contains("RANGE"))
            {
                detailStats = text;
                continue;
            }
            if (detailTags == null && (value == "NO TAG" || (rect.anchorMin.x >= 0.45f && rect.anchorMin.y >= 0.20f && rect.anchorMin.y <= 0.30f)))
            {
                detailTags = text;
                continue;
            }
            if (detailSynergy == null && rect.anchorMin.y < 0.16f)
            {
                detailSynergy = text;
                continue;
            }
            if (detailRarity == null && rect.anchorMin.y >= 0.63f && rect.anchorMin.y <= 0.69f)
            {
                detailRarity = text;
                continue;
            }
            if (detailState == null && rect.anchorMin.y >= 0.57f && rect.anchorMin.y < 0.64f)
                detailState = text;
        }
    }

    private void HideDetailDecoration()
    {
        SetChildrenActive(detailRoot, "PinkSlash", false);
        SetChildrenActive(detailRoot, "CyanStroke", false);
        SetChildrenActive(detailRoot, "CornerTab", false);
        SetChildrenActive(detailRoot, "HeaderTag", false);
        SetChildrenActive(detailRoot, "IconInkBack", false);
        SetChildrenActive(detailRoot, "BottomStroke", false);

        if (detailStatHeader != null)
            detailStatHeader.gameObject.SetActive(false);
        if (detailTagHeader != null)
            detailTagHeader.gameObject.SetActive(false);
    }

    private void ApplyDetailLayout()
    {
        if (detailRoot == null)
            return;

        RectTransform icon = detailRoot.Find("EquipmentIcon") as RectTransform;
        if (icon != null)
        {
            icon.anchorMin = icon.anchorMax = new Vector2(0.16f, 0.79f);
            icon.sizeDelta = new Vector2(88f, 88f);
            icon.anchoredPosition = Vector2.zero;
            icon.localRotation = Quaternion.identity;
        }

        SetTextLayout(detailSlot, new Vector2(0.61f, 0.91f), new Vector2(0.93f, 0.96f), 9, FontStyle.Normal, secondaryText);
        SetTextLayout(detailTitle, new Vector2(0.31f, 0.77f), new Vector2(0.93f, 0.87f), 22, FontStyle.Bold, primaryText);
        SetTextLayout(detailRarity, new Vector2(0.31f, 0.71f), new Vector2(0.93f, 0.77f), 10, FontStyle.Normal, secondaryText);

        if (detailState != null)
        {
            SetAnchors(detailState.rectTransform, new Vector2(0.31f, 0.65f), new Vector2(0.93f, 0.71f));
            detailState.fontSize = 10;
            detailState.fontStyle = FontStyle.Bold;
        }

        RectTransform desc = descriptionBack != null ? descriptionBack.rectTransform : null;
        if (desc != null)
            SetAnchors(desc, new Vector2(0.07f, 0.40f), new Vector2(0.93f, 0.62f));

        if (detailDescription != null)
        {
            SetAnchors(detailDescription.rectTransform, new Vector2(0.04f, 0.10f), new Vector2(0.96f, 0.90f));
            detailDescription.fontSize = 12;
            detailDescription.fontStyle = FontStyle.Normal;
            detailDescription.color = primaryText;
            detailDescription.lineSpacing = 1.08f;
        }

        SetTextLayout(detailStats, new Vector2(0.07f, 0.31f), new Vector2(0.93f, 0.37f), 10, FontStyle.Normal, primaryText);
        SetTextLayout(detailTags, new Vector2(0.07f, 0.23f), new Vector2(0.93f, 0.29f), 9, FontStyle.Normal, secondaryText);
        SetTextLayout(detailSynergyTitle, new Vector2(0.07f, 0.16f), new Vector2(0.93f, 0.20f), 9, FontStyle.Bold, secondaryText);
        SetTextLayout(detailSynergy, new Vector2(0.07f, 0.055f), new Vector2(0.93f, 0.155f), 10, FontStyle.Normal, primaryText);
    }

    private void MaintainDetailStyle()
    {
        if (detailRoot == null)
            return;

        if (detailBack != null)
            detailBack.color = panelColor;
        if (detailOutline != null)
        {
            detailOutline.effectColor = outlineColor;
            detailOutline.effectDistance = new Vector2(1f, -1f);
        }
        if (descriptionBack != null)
            descriptionBack.color = softPanelColor;

        if (detailStats != null && detailStats.text.Contains("\n"))
            detailStats.text = FlattenStatLine(detailStats.text);
    }

    private void ResolveRewardInline()
    {
        RectTransform resolved = FindRect("RewardSelectedInlineDetail");
        if (resolved == null)
            return;

        int id = resolved.GetInstanceID();
        if (rewardInlineRoot == resolved && styledRewardInlineId == id)
            return;

        rewardInlineRoot = resolved;
        styledRewardInlineId = id;
        rewardInlineBack = rewardInlineRoot.GetComponent<Image>();
        if (rewardInlineBack == null)
            rewardInlineBack = rewardInlineRoot.gameObject.AddComponent<Image>();
        rewardInlineBack.raycastTarget = false;

        rewardDescriptionHeader = null;
        rewardDescription = null;
        rewardEffectsHeader = null;
        rewardEffects = null;
        rewardTags = null;

        Text[] texts = rewardInlineRoot.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null || text.transform.parent != rewardInlineRoot)
                continue;

            string value = text.text ?? string.Empty;
            if (value == "DESCRIPTION")
                rewardDescriptionHeader = text;
            else if (value == "EFFECTS")
                rewardEffectsHeader = text;
            else if (text.rectTransform.anchorMin.y >= 0.55f)
                rewardDescription = text;
            else if (text.rectTransform.anchorMin.y >= 0.12f)
                rewardEffects = text;
            else
                rewardTags = text;
        }

        if (rewardDescriptionHeader != null)
            rewardDescriptionHeader.gameObject.SetActive(false);
        if (rewardEffectsHeader != null)
            rewardEffectsHeader.gameObject.SetActive(false);

        SetTextLayout(rewardDescription, new Vector2(0.04f, 0.52f), new Vector2(0.96f, 0.94f), 10, FontStyle.Normal, primaryText);
        SetTextLayout(rewardEffects, new Vector2(0.04f, 0.22f), new Vector2(0.96f, 0.48f), 9, FontStyle.Normal, primaryText);
        SetTextLayout(rewardTags, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.18f), 8, FontStyle.Normal, secondaryText);

        MaintainRewardInlineStyle();
    }

    private void MaintainRewardInlineStyle()
    {
        if (rewardInlineRoot == null)
            return;

        if (rewardInlineBack != null)
            rewardInlineBack.color = new Color(0f, 0f, 0f, 0.16f);

        if (rewardDescriptionHeader != null && rewardDescriptionHeader.gameObject.activeSelf)
            rewardDescriptionHeader.gameObject.SetActive(false);
        if (rewardEffectsHeader != null && rewardEffectsHeader.gameObject.activeSelf)
            rewardEffectsHeader.gameObject.SetActive(false);

        if (rewardEffects != null && rewardEffects.text.Contains("\n"))
            rewardEffects.text = FlattenRewardEffects(rewardEffects.text);
    }

    private static string FlattenStatLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        string[] lines = value.Split('\n');
        List<string> compact = new();
        for (int i = 0; i < lines.Length; i++)
        {
            string line = CollapseSpaces(lines[i].Trim());
            if (!string.IsNullOrWhiteSpace(line))
                compact.Add(line);
        }
        return string.Join("   ·   ", compact);
    }

    private static string FlattenRewardEffects(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        string[] lines = value.Split('\n');
        List<string> compact = new();
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i]
                .Replace("• DAMAGE", "DMG")
                .Replace("• MOVE SPEED", "MOVE")
                .Replace("• RANGE", "RANGE")
                .Trim();
            line = CollapseSpaces(line);
            if (!string.IsNullOrWhiteSpace(line))
                compact.Add(line);
        }
        return string.Join("   ·   ", compact);
    }

    private static string CollapseSpaces(string value)
    {
        while (value.Contains("  "))
            value = value.Replace("  ", " ");
        return value;
    }

    private static void SetTextLayout(Text text, Vector2 min, Vector2 max, int fontSize, FontStyle style, Color color)
    {
        if (text == null)
            return;

        SetAnchors(text.rectTransform, min, max);
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
    }

    private static void SetChildrenActive(RectTransform root, string childName, bool active)
    {
        if (root == null)
            return;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child != null && child.name == childName)
                child.gameObject.SetActive(active);
        }
    }

    private static RectTransform FindRect(string targetName)
    {
        RectTransform[] all = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == targetName)
                return rect;
        }
        return null;
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        if (rect == null)
            return;

        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
