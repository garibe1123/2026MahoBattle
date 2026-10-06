using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space popup 배치를 한 곳에서 결정합니다.
///
/// - 화면 Safe Area를 항상 보장합니다.
/// - Host Dialogue / Confirm / Mission 등 등록된 Avoid Zone을 공통으로 피합니다.
/// - 각 Tooltip Controller가 개별 AvoidHostDialogue 코드를 갖지 않도록 합니다.
/// </summary>
public static class BattleUiAvoidanceResolver
{
    private sealed class AvoidZone
    {
        public RectTransform rect;
        public int priority;
        public float padding;
        public Func<bool> visible;
    }

    private static readonly List<AvoidZone> Zones = new();
    private static readonly List<Vector2> CandidateScreens = new(24);

    public static void RegisterZone(
        RectTransform rect,
        int priority,
        float padding,
        Func<bool> visible = null)
    {
        if (rect == null)
            return;

        UnregisterZone(rect);

        Zones.Add(new AvoidZone
        {
            rect = rect,
            priority = Mathf.Max(0, priority),
            padding = Mathf.Max(0f, padding),
            visible = visible
        });
    }

    public static void UnregisterZone(RectTransform rect)
    {
        if (rect == null)
            return;

        for (int i = Zones.Count - 1; i >= 0; i--)
        {
            if (Zones[i] == null ||
                Zones[i].rect == null ||
                Zones[i].rect == rect)
            {
                Zones.RemoveAt(i);
            }
        }
    }

    public static Vector2 Resolve(
        RectTransform parent,
        Vector2 desiredLocal,
        Vector2 size,
        Vector2 pivot,
        float screenMargin,
        Vector2 targetScreenPoint)
    {
        if (parent == null)
            return desiredLocal;

        CleanupDeadZones();

        Canvas canvas =
            parent.GetComponentInParent<Canvas>();

        Camera eventCamera =
            canvas != null &&
            canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

        Rect viewport =
            eventCamera != null
                ? eventCamera.pixelRect
                : new Rect(
                    0f,
                    0f,
                    Screen.width,
                    Screen.height);

        float margin =
            Mathf.Max(
                0f,
                screenMargin);

        Rect safeViewport =
            new(
                viewport.xMin + margin,
                viewport.yMin + margin,
                Mathf.Max(
                    1f,
                    viewport.width - margin * 2f),
                Mathf.Max(
                    1f,
                    viewport.height - margin * 2f));

        Vector2 desiredScreen =
            RectTransformUtility.WorldToScreenPoint(
                eventCamera,
                parent.TransformPoint(
                    desiredLocal));

        Rect desiredRect =
            GetScreenRectForLocalRect(
                parent,
                desiredLocal,
                size,
                pivot,
                eventCamera);

        float popupWidth =
            Mathf.Max(
                1f,
                desiredRect.width);

        float popupHeight =
            Mathf.Max(
                1f,
                desiredRect.height);

        CandidateScreens.Clear();
        AddCandidate(
            ClampPivotScreen(
                desiredScreen,
                popupWidth,
                popupHeight,
                pivot,
                safeViewport));

        for (int i = 0; i < Zones.Count; i++)
        {
            AvoidZone zone =
                Zones[i];

            if (!IsActive(zone))
                continue;

            Rect blocked =
                ExpandRect(
                    GetScreenRect(
                        zone.rect),
                    zone.padding);

            Vector2 baseScreen =
                desiredScreen;

            AddCandidate(
                ClampPivotScreen(
                    new Vector2(
                        baseScreen.x,
                        blocked.yMax +
                        popupHeight * pivot.y),
                    popupWidth,
                    popupHeight,
                    pivot,
                    safeViewport));

            AddCandidate(
                ClampPivotScreen(
                    new Vector2(
                        baseScreen.x,
                        blocked.yMin -
                        popupHeight * (1f - pivot.y)),
                    popupWidth,
                    popupHeight,
                    pivot,
                    safeViewport));

            AddCandidate(
                ClampPivotScreen(
                    new Vector2(
                        blocked.xMin -
                        popupWidth * (1f - pivot.x),
                        baseScreen.y),
                    popupWidth,
                    popupHeight,
                    pivot,
                    safeViewport));

            AddCandidate(
                ClampPivotScreen(
                    new Vector2(
                        blocked.xMax +
                        popupWidth * pivot.x,
                        baseScreen.y),
                    popupWidth,
                    popupHeight,
                    pivot,
                    safeViewport));
        }

        Vector2 best =
            CandidateScreens.Count > 0
                ? CandidateScreens[0]
                : desiredScreen;

        float bestScore =
            ScoreCandidate(
                best,
                desiredScreen,
                targetScreenPoint,
                popupWidth,
                popupHeight,
                pivot);

        for (int i = 1; i < CandidateScreens.Count; i++)
        {
            Vector2 candidate =
                CandidateScreens[i];

            float score =
                ScoreCandidate(
                    candidate,
                    desiredScreen,
                    targetScreenPoint,
                    popupWidth,
                    popupHeight,
                    pivot);

            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent,
                best,
                eventCamera,
                out Vector2 bestLocal))
        {
            return desiredLocal;
        }

        return bestLocal;
    }

    private static float ScoreCandidate(
        Vector2 pivotScreen,
        Vector2 desiredScreen,
        Vector2 targetScreenPoint,
        float popupWidth,
        float popupHeight,
        Vector2 pivot)
    {
        Rect popup =
            Rect.MinMaxRect(
                pivotScreen.x - popupWidth * pivot.x,
                pivotScreen.y - popupHeight * pivot.y,
                pivotScreen.x + popupWidth * (1f - pivot.x),
                pivotScreen.y + popupHeight * (1f - pivot.y));

        float score =
            Vector2.SqrMagnitude(
                pivotScreen -
                desiredScreen) *
            0.015f;

        if (popup.Contains(targetScreenPoint))
            score += 250000f;

        for (int i = 0; i < Zones.Count; i++)
        {
            AvoidZone zone =
                Zones[i];

            if (!IsActive(zone))
                continue;

            Rect blocked =
                ExpandRect(
                    GetScreenRect(
                        zone.rect),
                    zone.padding);

            Rect overlap =
                Intersect(
                    popup,
                    blocked);

            if (overlap.width <= 0f ||
                overlap.height <= 0f)
            {
                continue;
            }

            float overlapRatio =
                (overlap.width *
                 overlap.height) /
                Mathf.Max(
                    1f,
                    popup.width *
                    popup.height);

            score +=
                overlapRatio *
                Mathf.Max(
                    1,
                    zone.priority) *
                100000f;
        }

        return score;
    }

    private static void AddCandidate(Vector2 value)
    {
        for (int i = 0; i < CandidateScreens.Count; i++)
        {
            if ((CandidateScreens[i] - value).sqrMagnitude < 1f)
                return;
        }

        CandidateScreens.Add(value);
    }

    private static Vector2 ClampPivotScreen(
        Vector2 pivotScreen,
        float width,
        float height,
        Vector2 pivot,
        Rect safe)
    {
        pivotScreen.x =
            Mathf.Clamp(
                pivotScreen.x,
                safe.xMin + width * pivot.x,
                safe.xMax - width * (1f - pivot.x));

        pivotScreen.y =
            Mathf.Clamp(
                pivotScreen.y,
                safe.yMin + height * pivot.y,
                safe.yMax - height * (1f - pivot.y));

        return pivotScreen;
    }

    private static Rect GetScreenRectForLocalRect(
        RectTransform parent,
        Vector2 pivotLocal,
        Vector2 size,
        Vector2 pivot,
        Camera eventCamera)
    {
        Vector2[] localCorners =
        {
            pivotLocal + new Vector2(-size.x * pivot.x, -size.y * pivot.y),
            pivotLocal + new Vector2(size.x * (1f - pivot.x), -size.y * pivot.y),
            pivotLocal + new Vector2(size.x * (1f - pivot.x), size.y * (1f - pivot.y)),
            pivotLocal + new Vector2(-size.x * pivot.x, size.y * (1f - pivot.y))
        };

        Vector2 min =
            new(
                float.PositiveInfinity,
                float.PositiveInfinity);

        Vector2 max =
            new(
                float.NegativeInfinity,
                float.NegativeInfinity);

        for (int i = 0; i < localCorners.Length; i++)
        {
            Vector2 point =
                RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    parent.TransformPoint(
                        localCorners[i]));

            min =
                Vector2.Min(
                    min,
                    point);

            max =
                Vector2.Max(
                    max,
                    point);
        }

        return Rect.MinMaxRect(
            min.x,
            min.y,
            max.x,
            max.y);
    }

    private static Rect GetScreenRect(RectTransform rect)
    {
        if (rect == null)
            return default;

        Vector3[] corners =
            new Vector3[4];

        rect.GetWorldCorners(
            corners);

        Canvas canvas =
            rect.GetComponentInParent<Canvas>();

        Camera eventCamera =
            canvas != null &&
            canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

        Vector2 min =
            RectTransformUtility.WorldToScreenPoint(
                eventCamera,
                corners[0]);

        Vector2 max =
            min;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 point =
                RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    corners[i]);

            min =
                Vector2.Min(
                    min,
                    point);

            max =
                Vector2.Max(
                    max,
                    point);
        }

        return Rect.MinMaxRect(
            min.x,
            min.y,
            max.x,
            max.y);
    }

    private static Rect ExpandRect(
        Rect rect,
        float padding)
    {
        return new Rect(
            rect.xMin - padding,
            rect.yMin - padding,
            rect.width + padding * 2f,
            rect.height + padding * 2f);
    }

    private static Rect Intersect(
        Rect a,
        Rect b)
    {
        float xMin =
            Mathf.Max(
                a.xMin,
                b.xMin);

        float yMin =
            Mathf.Max(
                a.yMin,
                b.yMin);

        float xMax =
            Mathf.Min(
                a.xMax,
                b.xMax);

        float yMax =
            Mathf.Min(
                a.yMax,
                b.yMax);

        if (xMax <= xMin ||
            yMax <= yMin)
        {
            return default;
        }

        return Rect.MinMaxRect(
            xMin,
            yMin,
            xMax,
            yMax);
    }

    private static bool IsActive(AvoidZone zone)
    {
        if (zone == null ||
            zone.rect == null ||
            !zone.rect.gameObject.activeInHierarchy)
        {
            return false;
        }

        return zone.visible == null ||
               zone.visible();
    }

    private static void CleanupDeadZones()
    {
        for (int i = Zones.Count - 1; i >= 0; i--)
        {
            if (Zones[i] == null ||
                Zones[i].rect == null)
            {
                Zones.RemoveAt(i);
            }
        }
    }
}

/// <summary>
/// 아이템 상세 카드 하단에 반쯤 걸치는 원형 속성 배지 스트립.
/// 현재 EquipmentTag를 코드 심볼로 보여주고, 추후 Sprite 아이콘으로 교체할 수 있습니다.
/// </summary>
public sealed class BattleEquipmentBadgeStrip : MonoBehaviour
{
    private const int MaxBadges = 4;

    private readonly RectTransform[] badgeRoots =
        new RectTransform[MaxBadges];

    private readonly Text[] symbols =
        new Text[MaxBadges];

    private readonly Text[] labels =
        new Text[MaxBadges];

    private RectTransform rect;
    private Color frameColor;
    private Color fillColor;
    private Color textColor;
    private float badgeSize = 46f;
    private float spacing = 56f;
    private bool built;

    private static Sprite circleSprite;

    public static BattleEquipmentBadgeStrip Attach(
        RectTransform parent,
        Color frame,
        Color fill,
        Color text,
        float size = 46f,
        float gap = 56f)
    {
        if (parent == null)
            return null;

        GameObject go =
            new(
                "ItemAttributeBadges",
                typeof(RectTransform));

        go.transform.SetParent(
            parent,
            false);

        BattleEquipmentBadgeStrip strip =
            go.AddComponent<BattleEquipmentBadgeStrip>();

        strip.Configure(
            frame,
            fill,
            text,
            size,
            gap);

        return strip;
    }

    public void Configure(
        Color frame,
        Color fill,
        Color text,
        float size,
        float gap)
    {
        frameColor = frame;
        fillColor = fill;
        textColor = text;
        badgeSize = Mathf.Max(28f, size);
        spacing = Mathf.Max(badgeSize + 4f, gap);

        EnsureBuilt();
    }

    public void Show(BattleEquipmentSO equipment)
    {
        EnsureBuilt();

        List<EquipmentTag> tags =
            CollectDisplayTags(
                equipment);

        for (int i = 0; i < MaxBadges; i++)
        {
            bool visible =
                i < tags.Count;

            if (badgeRoots[i] != null)
                badgeRoots[i].gameObject.SetActive(visible);

            if (!visible)
                continue;

            EquipmentTag tag =
                tags[i];

            if (symbols[i] != null)
                symbols[i].text =
                    GetSymbol(
                        tag);

            if (labels[i] != null)
                labels[i].text =
                    GetLabel(
                        tag);
        }

        gameObject.SetActive(
            tags.Count > 0);
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(
            visible);
    }

    private void EnsureBuilt()
    {
        if (built)
            return;

        built = true;

        rect =
            transform as RectTransform;

        if (rect == null)
            return;

        rect.anchorMin =
            rect.anchorMax =
                new Vector2(
                    0.5f,
                    0f);

        rect.pivot =
            new Vector2(
                0.5f,
                0.5f);

        rect.anchoredPosition =
            new Vector2(
                0f,
                0f);

        rect.sizeDelta =
            new Vector2(
                spacing * MaxBadges,
                badgeSize + 28f);

        float total =
            spacing *
            (MaxBadges - 1);

        for (int i = 0; i < MaxBadges; i++)
        {
            RectTransform badge =
                CreateRect(
                    rect,
                    "Badge_" + i,
                    new Vector2(
                        badgeSize,
                        badgeSize));

            badge.anchorMin =
                badge.anchorMax =
                    new Vector2(
                        0.5f,
                        0.5f);

            badge.pivot =
                new Vector2(
                    0.5f,
                    0.5f);

            badge.anchoredPosition =
                new Vector2(
                    -total * 0.5f +
                    spacing * i,
                    0f);

            Image outer =
                badge.gameObject.AddComponent<Image>();

            outer.sprite =
                GetCircleSprite();

            outer.color =
                frameColor;

            outer.raycastTarget =
                false;

            RectTransform inner =
                CreateRect(
                    badge,
                    "Inner",
                    Vector2.one *
                    (badgeSize - 7f));

            inner.anchorMin =
                inner.anchorMax =
                    new Vector2(
                        0.5f,
                        0.5f);

            inner.pivot =
                new Vector2(
                    0.5f,
                    0.5f);

            Image innerImage =
                inner.gameObject.AddComponent<Image>();

            innerImage.sprite =
                GetCircleSprite();

            innerImage.color =
                fillColor;

            innerImage.raycastTarget =
                false;

            Text symbol =
                CreateText(
                    inner,
                    "Symbol",
                    string.Empty,
                    Mathf.RoundToInt(
                        badgeSize * 0.30f),
                    FontStyle.Bold,
                    TextAnchor.MiddleCenter,
                    textColor);

            Stretch(
                symbol.rectTransform);

            Text label =
                CreateText(
                    badge,
                    "Label",
                    string.Empty,
                    8,
                    FontStyle.Bold,
                    TextAnchor.UpperCenter,
                    frameColor);

            label.rectTransform.anchorMin =
                label.rectTransform.anchorMax =
                    new Vector2(
                        0.5f,
                        0f);

            label.rectTransform.pivot =
                new Vector2(
                    0.5f,
                    1f);

            label.rectTransform.sizeDelta =
                new Vector2(
                    spacing,
                    18f);

            label.rectTransform.anchoredPosition =
                new Vector2(
                    0f,
                    -badgeSize * 0.5f - 4f);

            badgeRoots[i] =
                badge;

            symbols[i] =
                symbol;

            labels[i] =
                label;

            badge.gameObject.SetActive(
                false);
        }
    }

    private static List<EquipmentTag> CollectDisplayTags(
        BattleEquipmentSO equipment)
    {
        List<EquipmentTag> result =
            new(
                MaxBadges);

        if (equipment == null ||
            equipment.tags == null)
        {
            return result;
        }

        EquipmentTag[] priority =
        {
            EquipmentTag.Burn,
            EquipmentTag.Shock,
            EquipmentTag.Explosion,
            EquipmentTag.Area,
            EquipmentTag.Projectile,
            EquipmentTag.Melee,
            EquipmentTag.Dash,
            EquipmentTag.Summon,
            EquipmentTag.Control,
            EquipmentTag.Defense,
            EquipmentTag.Heal,
            EquipmentTag.Resource,
            EquipmentTag.Sustain,
            EquipmentTag.Critical,
            EquipmentTag.Precision,
            EquipmentTag.Break,
            EquipmentTag.OddWeapon
        };

        for (int i = 0;
             i < priority.Length &&
             result.Count < MaxBadges;
             i++)
        {
            if (equipment.tags.Contains(
                    priority[i]))
            {
                result.Add(
                    priority[i]);
            }
        }

        return result;
    }

    private static string GetSymbol(
        EquipmentTag tag)
    {
        return tag switch
        {
            EquipmentTag.Burn => "F",
            EquipmentTag.Shock => "E",
            EquipmentTag.Explosion => "*",
            EquipmentTag.Area => "O",
            EquipmentTag.Projectile => ">",
            EquipmentTag.Melee => "X",
            EquipmentTag.Dash => ">>",
            EquipmentTag.Summon => "+",
            EquipmentTag.Control => "C",
            EquipmentTag.Defense => "D",
            EquipmentTag.Heal => "H",
            EquipmentTag.Resource => "R",
            EquipmentTag.Sustain => "S",
            EquipmentTag.Critical => "!",
            EquipmentTag.Precision => ".",
            EquipmentTag.Break => "#",
            EquipmentTag.OddWeapon => "?",
            _ => "-"
        };
    }

    private static string GetLabel(
        EquipmentTag tag)
    {
        return tag switch
        {
            EquipmentTag.Projectile => "SHOT",
            EquipmentTag.Melee => "MELEE",
            EquipmentTag.Break => "BREAK",
            EquipmentTag.Explosion => "BLAST",
            EquipmentTag.Precision => "AIM",
            EquipmentTag.Critical => "CRIT",
            EquipmentTag.Area => "AREA",
            EquipmentTag.Dash => "DASH",
            EquipmentTag.Burn => "BURN",
            EquipmentTag.Shock => "SHOCK",
            EquipmentTag.Summon => "SUMMON",
            EquipmentTag.Sustain => "SUSTAIN",
            EquipmentTag.Defense => "DEF",
            EquipmentTag.Heal => "HEAL",
            EquipmentTag.Resource => "RESOURCE",
            EquipmentTag.Control => "CONTROL",
            EquipmentTag.OddWeapon => "ODD",
            _ => tag.ToString().ToUpperInvariant()
        };
    }

    private static Sprite GetCircleSprite()
    {
        if (circleSprite != null)
            return circleSprite;

        const int size = 64;

        Texture2D texture =
            new(
                size,
                size,
                TextureFormat.RGBA32,
                false);

        texture.name =
            "BattleUiBadgeCircle_Runtime";

        texture.wrapMode =
            TextureWrapMode.Clamp;

        texture.filterMode =
            FilterMode.Bilinear;

        Color32[] pixels =
            new Color32[size * size];

        float center =
            (size - 1) * 0.5f;

        float radius =
            center - 1f;

        float feather =
            1.25f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx =
                    x - center;

                float dy =
                    y - center;

                float distance =
                    Mathf.Sqrt(
                        dx * dx +
                        dy * dy);

                float alpha =
                    Mathf.Clamp01(
                        (radius -
                         distance) /
                        feather +
                        0.5f);

                pixels[y * size + x] =
                    new Color(
                        1f,
                        1f,
                        1f,
                        alpha);
            }
        }

        texture.SetPixels32(
            pixels);

        texture.Apply(
            false,
            true);

        circleSprite =
            Sprite.Create(
                texture,
                new Rect(
                    0f,
                    0f,
                    size,
                    size),
                new Vector2(
                    0.5f,
                    0.5f),
                64f);

        circleSprite.name =
            "BattleUiBadgeCircle_RuntimeSprite";

        return circleSprite;
    }

    private static RectTransform CreateRect(
        Transform parent,
        string name,
        Vector2 size)
    {
        GameObject go =
            new(
                name,
                typeof(RectTransform));

        go.transform.SetParent(
            parent,
            false);

        RectTransform rect =
            go.GetComponent<RectTransform>();

        rect.sizeDelta =
            size;

        return rect;
    }

    private static Text CreateText(
        Transform parent,
        string name,
        string value,
        int fontSize,
        FontStyle style,
        TextAnchor alignment,
        Color color)
    {
        RectTransform rect =
            CreateRect(
                parent,
                name,
                Vector2.zero);

        Text text =
            rect.gameObject.AddComponent<Text>();

        text.font =
            Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");

        text.text =
            value;

        text.fontSize =
            fontSize;

        text.fontStyle =
            style;

        text.alignment =
            alignment;

        text.color =
            color;

        text.raycastTarget =
            false;

        return text;
    }

    private static void Stretch(
        RectTransform rect)
    {
        rect.anchorMin =
            Vector2.zero;

        rect.anchorMax =
            Vector2.one;

        rect.offsetMin =
            Vector2.zero;

        rect.offsetMax =
            Vector2.zero;
    }
}
