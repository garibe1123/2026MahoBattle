using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 기존 BattleHUD의 Reward/Item 선택 기능은 그대로 두고, 외형만 현재 Kinetic 3x3 UI와 같은 테마로 맞춥니다.
/// 사용자 SO/Sprite/Scene 직렬화 데이터는 변경하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30600)]
public sealed class BattleRewardKineticThemeController : MonoBehaviour
{
    [SerializeField] private BattleRunManager runManager;

    [Header("Theme")]
    [SerializeField] private Color inkColor = new(0.035f, 0.030f, 0.055f, 0.985f);
    [SerializeField] private Color paperColor = new(0.94f, 0.90f, 0.76f, 1f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color accentCyan = new(0.15f, 0.88f, 0.92f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.52f, 1f);
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.12f;

    private float nextRefresh;
    private int lastPrizeCount = -1;

    private void Update()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();

        if (runManager == null || !runManager.RunActive || runManager.State != BattleRunState.Reward)
            return;

        RectTransform prizeRoot = FindRect("PrizeChoices");
        int prizeCount = prizeRoot != null ? prizeRoot.childCount : 0;
        if (Time.unscaledTime < nextRefresh && prizeCount == lastPrizeCount)
            return;

        nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
        lastPrizeCount = prizeCount;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        RectTransform screen = FindRect("PrizeSelectionScreen");
        if (screen != null)
        {
            screen.localRotation = Quaternion.Euler(0f, 0f, -1.2f);
            StylePanel(screen, inkColor, accentYellow, new Vector2(6f, -6f));

            RectTransform inner = screen.Find("ScreenInner") as RectTransform;
            if (inner != null)
            {
                StylePanel(inner, new Color(0.055f, 0.050f, 0.075f, 0.995f), accentCyan, new Vector2(3f, -3f));
                EnsureAccentLayer(inner);
                StyleScreenTexts(inner);
            }
        }

        StylePrizeCards();
        StyleRewardInventory();
        StylePlacementNotice();
    }

    private void EnsureAccentLayer(RectTransform inner)
    {
        Transform existing = inner.Find("RewardKineticAccentLayer");
        if (existing != null)
            return;

        RectTransform layer = CreateRect(inner, "RewardKineticAccentLayer", Vector2.zero);
        Stretch(layer);
        layer.SetAsFirstSibling();

        RectTransform yellowTag = CreateRect(layer, "YellowHeaderCut", new Vector2(360f, 58f));
        yellowTag.anchorMin = yellowTag.anchorMax = new Vector2(0f, 1f);
        yellowTag.pivot = new Vector2(0f, 1f);
        yellowTag.anchoredPosition = new Vector2(22f, -18f);
        yellowTag.localRotation = Quaternion.Euler(0f, 0f, -3f);
        SetImage(yellowTag, new Color(accentYellow.r, accentYellow.g, accentYellow.b, 0.95f));

        RectTransform pinkSlash = CreateRect(layer, "PinkSlash", new Vector2(24f, 220f));
        pinkSlash.anchorMin = pinkSlash.anchorMax = new Vector2(0f, 0.5f);
        pinkSlash.anchoredPosition = new Vector2(18f, 75f);
        pinkSlash.localRotation = Quaternion.Euler(0f, 0f, 11f);
        SetImage(pinkSlash, accentPink);

        RectTransform cyanRule = CreateRect(layer, "CyanRule", new Vector2(460f, 5f));
        cyanRule.anchorMin = cyanRule.anchorMax = new Vector2(1f, 1f);
        cyanRule.pivot = new Vector2(1f, 1f);
        cyanRule.anchoredPosition = new Vector2(-28f, -76f);
        cyanRule.localRotation = Quaternion.Euler(0f, 0f, 1.2f);
        SetImage(cyanRule, accentCyan);

        RectTransform paperCut = CreateRect(layer, "PaperCut", new Vector2(300f, 30f));
        paperCut.anchorMin = paperCut.anchorMax = new Vector2(0f, 0f);
        paperCut.pivot = new Vector2(0f, 0f);
        paperCut.anchoredPosition = new Vector2(30f, 24f);
        paperCut.localRotation = Quaternion.Euler(0f, 0f, 2.4f);
        SetImage(paperCut, new Color(paperColor.r, paperColor.g, paperColor.b, 0.18f));
    }

    private void StyleScreenTexts(RectTransform inner)
    {
        Text[] texts = inner.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null)
                continue;

            string value = text.text ?? string.Empty;
            if (value.Contains("CHOOSE YOUR PRIZE"))
            {
                text.fontStyle = FontStyle.Bold;
                text.fontSize = 32;
                text.color = inkColor;
                text.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2.5f);
            }
            else if (value.Contains("SELECT") && value.Contains("DRAG") && value.Contains("DROP"))
            {
                text.fontStyle = FontStyle.Bold;
                text.fontSize = 10;
                text.color = accentCyan;
            }
            else if (value.Contains("ON LIVE"))
            {
                text.fontStyle = FontStyle.Bold;
                text.color = accentPink;
            }
            else if (value == "SELECT A PRIZE")
            {
                text.fontStyle = FontStyle.Bold;
                text.color = accentYellow;
            }
        }
    }

    private void StylePrizeCards()
    {
        RectTransform root = FindRect("PrizeChoices");
        if (root == null)
            return;

        for (int i = 0; i < root.childCount; i++)
        {
            RectTransform card = root.GetChild(i) as RectTransform;
            if (card == null)
                continue;

            card.localRotation = Quaternion.Euler(0f, 0f, (i % 3 - 1) * 2.1f);
            Image image = card.GetComponent<Image>();
            if (image != null)
                image.color = new Color(0.050f, 0.045f, 0.070f, 0.995f);

            Outline outline = card.GetComponent<Outline>();
            if (outline == null)
                outline = card.gameObject.AddComponent<Outline>();
            outline.effectColor = AccentForIndex(i);
            outline.effectDistance = new Vector2(5f, -5f);

            Transform icon = card.Find("PrizeIcon");
            if (icon is RectTransform iconRect)
            {
                iconRect.sizeDelta = new Vector2(118f, 118f);
                iconRect.localRotation = Quaternion.identity;
            }

            Text[] texts = card.GetComponentsInChildren<Text>(true);
            for (int t = 0; t < texts.Length; t++)
            {
                Text text = texts[t];
                if (text == null)
                    continue;

                string value = text.text ?? string.Empty;
                text.fontStyle = FontStyle.Bold;
                if (value.Contains("CLICK") || value.Contains("DRAG"))
                    text.color = accentPink;
                else if (text.fontSize >= 13)
                    text.color = paperColor;
                else if (text.fontSize == 9)
                    text.color = accentYellow;
                else
                    text.color = accentCyan;
            }
        }
    }

    private void StyleRewardInventory()
    {
        RectTransform strip = FindRect("RewardLoadoutStrip");
        if (strip != null)
        {
            StylePanel(strip, inkColor, accentPink, new Vector2(5f, -5f));
            Text[] stripTexts = strip.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < stripTexts.Length; i++)
            {
                Text text = stripTexts[i];
                if (text != null && (text.text ?? string.Empty).Contains("CURRENT LOADOUT"))
                {
                    text.fontStyle = FontStyle.Bold;
                    text.color = accentYellow;
                    text.fontSize = 11;
                }
            }
        }

        RectTransform inventory = FindRect("RewardInventory");
        if (inventory == null)
            return;

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            Transform child = inventory.Find($"RewardLoadoutSlot_{i + 1}");
            if (child is not RectTransform slot)
                continue;

            Outline outline = slot.GetComponent<Outline>();
            if (outline == null)
                outline = slot.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(AccentForIndex(i).r, AccentForIndex(i).g, AccentForIndex(i).b, 0.75f);
            outline.effectDistance = new Vector2(3f, -3f);
        }
    }

    private void StylePlacementNotice()
    {
        RectTransform notice = FindRect("PlacementNotice");
        if (notice == null)
            return;

        StylePanel(notice, new Color(0.045f, 0.040f, 0.065f, 0.98f), accentYellow, new Vector2(3f, -3f));
        Text[] texts = notice.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] == null)
                continue;
            texts[i].fontStyle = FontStyle.Bold;
            texts[i].color = paperColor;
        }
    }

    private void StylePanel(RectTransform rect, Color fill, Color outlineColor, Vector2 outlineDistance)
    {
        Image image = rect.GetComponent<Image>();
        if (image != null)
            image.color = fill;

        Outline outline = rect.GetComponent<Outline>();
        if (outline == null)
            outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = outlineDistance;
    }

    private Color AccentForIndex(int index)
    {
        return index % 3 switch
        {
            0 => accentYellow,
            1 => accentCyan,
            _ => accentPink
        };
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

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return rect;
    }

    private static void SetImage(RectTransform rect, Color color)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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
        EditorApplication.delayCall += EnsureEditorComponents;
    }

    private static void EnsureEditorComponents()
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

            if (manager.GetComponent<BattleRewardKineticThemeController>() == null)
            {
                Undo.AddComponent<BattleRewardKineticThemeController>(manager.gameObject);
                EditorUtility.SetDirty(manager.gameObject);
                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            }
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponents()
    {
        BattleSceneManager[] managers = Object.FindObjectsByType<BattleSceneManager>(
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
