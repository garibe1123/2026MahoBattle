using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reward PACK 편집 중의 절대 Sorting 계약.
/// World-space TV는 20000+, Presenter는 최대 32000까지 사용하므로
/// Reward 조작 UI는 32100~32600 보호 구간을 사용합니다.
/// 이 범위 안에서는 PACK < transient/drag < cancel < detail 순서를 고정합니다.
/// </summary>
public static class BattleUiSortingContract
{
    public const int InventoryInteractionBase = 1550;
    public const int RewardPack = 32100;
    public const int RewardTransientBase = 32300;
    public const int RewardDragGhost = 32400;
    public const int RewardSelectionCancel = 32500;
    public const int RewardDetail = 32600;
    public const int RewardModal = 32700;
}

/// <summary>
/// Battle UI에서 공통으로 쓰는 Unscaled-Time 홀로그램 Stroke Material 공급자.
/// BattleShowPresentationManager가 씬에 없더라도 Runtime fallback Material을 보장합니다.
/// </summary>
public static class BattleUiHologramMaterialProvider
{
    private static Material runtimeMaterial;
    private static BattleUiHologramMaterialDriver driver;
    private static readonly HashSet<Material> TrackedMaterials = new();

    public static Material Resolve(
        Material preferred = null)
    {
        EnsureDriver();

        // Inspector에서 지정한 Material은 Shader/Texture/Keyword/Property를
        // 포함한 "완성된 외형"이므로 절대 다른 Shader Material로 바꾸지 않습니다.
        // Unscaled Time 프로퍼티를 지원하는 경우에만 시간값을 추가로 갱신합니다.
        if (preferred != null)
        {
            if (preferred.HasProperty(
                    "_BattleUiUnscaledTime"))
            {
                TrackedMaterials.Add(
                    preferred);

                ApplyTime(
                    preferred);
            }

            return preferred;
        }

        // Material이 아예 지정되지 않은 경우에만 공용 Hologram fallback을 사용합니다.
        if (runtimeMaterial == null)
        {
            Shader shader =
                Shader.Find(
                    "UI/BattleUiHologramStroke");

            if (shader != null)
            {
                runtimeMaterial =
                    new Material(
                        shader)
                    {
                        name = "BattleUiHologramStroke_SharedRuntime",
                        hideFlags = HideFlags.HideAndDontSave
                    };
            }
        }

        if (runtimeMaterial != null)
        {
            TrackedMaterials.Add(
                runtimeMaterial);

            ApplyTime(
                runtimeMaterial);

            return runtimeMaterial;
        }

        return preferred;
    }

    public static void Tick()
    {
        TrackedMaterials.RemoveWhere(
            material =>
                material == null);

        foreach (Material material in TrackedMaterials)
            ApplyTime(
                material);
    }

    private static void EnsureDriver()
    {
        if (driver != null)
            return;

        GameObject go =
            new(
                "BattleUiHologramMaterialDriver");

        UnityEngine.Object.DontDestroyOnLoad(
            go);

        go.hideFlags =
            HideFlags.HideAndDontSave;

        driver =
            go.AddComponent<
                BattleUiHologramMaterialDriver>();
    }

    private static void ApplyTime(
        Material material)
    {
        if (material == null)
            return;

        if (material.HasProperty(
                "_BattleUiUnscaledTime"))
        {
            material.SetFloat(
                "_BattleUiUnscaledTime",
                Time.unscaledTime);
        }
    }
}

public sealed class BattleUiHologramMaterialDriver : MonoBehaviour
{
    private void Update()
    {
        BattleUiHologramMaterialProvider.Tick();
    }
}

/// <summary>
/// 단순 Rect 패널에 Fill과 분리된 홀로그램 Border를 얹습니다.
/// </summary>
public sealed class BattleUiHologramBorder : MonoBehaviour
{
    private static Sprite borderSprite;

    private Image borderImage;
    private Color baseColor = Color.white;

    public static BattleUiHologramBorder Attach(
        RectTransform parent,
        Color color)
    {
        if (parent == null)
            return null;

        Transform existing =
            parent.Find(
                "HologramBorder");

        GameObject go;

        if (existing != null)
        {
            go =
                existing.gameObject;
        }
        else
        {
            go =
                new GameObject(
                    "HologramBorder",
                    typeof(RectTransform));
            go.transform.SetParent(
                parent,
                false);
        }

        RectTransform rect =
            go.GetComponent<RectTransform>();

        rect.anchorMin =
            Vector2.zero;
        rect.anchorMax =
            Vector2.one;
        rect.offsetMin =
            Vector2.zero;
        rect.offsetMax =
            Vector2.zero;
        rect.pivot =
            new Vector2(
                0.5f,
                0.5f);

        Image image =
            go.GetComponent<Image>();

        if (image == null)
            image =
                go.AddComponent<Image>();

        image.sprite =
            GetBorderSprite();

        image.type =
            Image.Type.Sliced;

        image.color =
            color;

        image.material =
            BattleUiHologramMaterialProvider.Resolve();

        image.raycastTarget =
            false;

        go.transform.SetAsLastSibling();

        BattleUiHologramBorder border =
            go.GetComponent<BattleUiHologramBorder>();

        if (border == null)
            border =
                go.AddComponent<BattleUiHologramBorder>();

        border.borderImage =
            image;

        border.baseColor =
            color;

        return border;
    }

    public void SetAlpha(float alpha)
    {
        if (borderImage == null)
            borderImage =
                GetComponent<Image>();

        if (borderImage == null)
            return;

        Color color =
            baseColor;

        color.a *=
            Mathf.Clamp01(
                alpha);

        borderImage.color =
            color;
    }

    private void LateUpdate()
    {
        if (borderImage == null)
            borderImage =
                GetComponent<Image>();

        if (borderImage != null)
        {
            borderImage.material =
                BattleUiHologramMaterialProvider.Resolve(
                    borderImage.material);
        }
    }

    private static Sprite GetBorderSprite()
    {
        if (borderSprite != null)
            return borderSprite;

        const int size = 16;
        const int edge = 3;

        Texture2D texture =
            new(
                size,
                size,
                TextureFormat.RGBA32,
                false);

        texture.name =
            "BattleUiHologramBorder_Runtime";

        texture.wrapMode =
            TextureWrapMode.Clamp;

        texture.filterMode =
            FilterMode.Bilinear;

        Color32[] pixels =
            new Color32[
                size *
                size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool border =
                    x < edge ||
                    y < edge ||
                    x >= size - edge ||
                    y >= size - edge;

                pixels[
                    y * size +
                    x] =
                    border
                        ? new Color32(
                            255,
                            255,
                            255,
                            255)
                        : new Color32(
                            255,
                            255,
                            255,
                            0);
            }
        }

        texture.SetPixels32(
            pixels);

        texture.Apply(
            false,
            true);

        borderSprite =
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
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(
                    edge,
                    edge,
                    edge,
                    edge));

        borderSprite.name =
            "BattleUiHologramBorder_RuntimeSprite";

        return borderSprite;
    }
}

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
/// 전용 일러스트가 없어도 아이템 Sprite 하나를 단정한 단일 프리뷰로 보여줍니다.
/// 장식용 색 띠 / 대각선 Slash / Ghost 복제 이미지는 사용하지 않습니다.
/// </summary>
public sealed class BattleItemHeroThumbnail : MonoBehaviour
{
    private RectTransform rect;
    private RectTransform foregroundRect;
    private Image foregroundImage;
    private Vector2 lastLayoutSize;
    private bool built;

    public static BattleItemHeroThumbnail Attach(
        RectTransform parent,
        Color paper,
        Color ink,
        Color accent)
    {
        if (parent == null)
            return null;

        GameObject go =
            new(
                "HeroThumbnailComposition",
                typeof(RectTransform));

        go.transform.SetParent(
            parent,
            false);

        RectTransform root =
            go.GetComponent<RectTransform>();

        Stretch(root);

        if (parent.GetComponent<RectMask2D>() == null)
            parent.gameObject.AddComponent<RectMask2D>();

        BattleItemHeroThumbnail hero =
            go.AddComponent<BattleItemHeroThumbnail>();

        hero.EnsureBuilt();

        return hero;
    }

    public void Show(BattleEquipmentSO equipment)
    {
        EnsureBuilt();
        UpdateResponsiveLayout(force: true);

        bool hasImage =
            equipment != null &&
            equipment.icon != null;

        Sprite sprite =
            hasImage
                ? equipment.icon
                : BattleHudSpriteCache.DefaultSprite;

        if (foregroundImage != null)
        {
            foregroundImage.sprite =
                sprite;

            foregroundImage.preserveAspect =
                hasImage;

            foregroundImage.color =
                Color.white;

            foregroundImage.enabled =
                equipment != null;
        }
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

        Stretch(rect);

        foregroundRect =
            CreateRect(
                rect,
                "ForegroundIcon",
                new Vector2(
                    132f,
                    132f));

        foregroundRect.anchorMin =
            foregroundRect.anchorMax =
                new Vector2(
                    0.5f,
                    0.5f);

        foregroundRect.pivot =
            new Vector2(
                0.5f,
                0.5f);

        foregroundRect.anchoredPosition =
            Vector2.zero;

        foregroundImage =
            foregroundRect.gameObject.AddComponent<Image>();

        foregroundImage.raycastTarget =
            false;

        foregroundImage.color =
            Color.white;

        UpdateResponsiveLayout(force: true);
    }

    private void LateUpdate()
    {
        UpdateResponsiveLayout(force: false);
    }

    private void OnRectTransformDimensionsChange()
    {
        UpdateResponsiveLayout(force: true);
    }

    private void UpdateResponsiveLayout(bool force)
    {
        if (rect == null)
            return;

        Vector2 size =
            rect.rect.size;

        if (size.x <= 1f ||
            size.y <= 1f)
        {
            return;
        }

        if (!force &&
            (size - lastLayoutSize).sqrMagnitude < 0.25f)
        {
            return;
        }

        lastLayoutSize =
            size;

        float foregroundSize =
            Mathf.Clamp(
                Mathf.Min(
                    size.x * 0.40f,
                    size.y * 0.80f),
                42f,
                150f);

        if (foregroundRect != null)
        {
            foregroundRect.sizeDelta =
                Vector2.one *
                foregroundSize;

            foregroundRect.anchoredPosition =
                Vector2.zero;

            foregroundRect.localRotation =
                Quaternion.identity;
        }
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
    }
}

/// <summary>
/// 아이템 상세 카드 하단에 반쯤 걸치는 원형 속성 배지 스트립.
/// EquipmentTag를 런타임 생성 아이콘으로 표시하므로 별도 임시 문자 심볼이 필요 없습니다.
/// </summary>
public sealed class BattleEquipmentBadgeStrip : MonoBehaviour
{
    private const int MaxBadges = 4;

    private readonly RectTransform[] badgeRoots =
        new RectTransform[MaxBadges];

    private readonly Image[] icons =
        new Image[MaxBadges];

    private RectTransform rect;
    private Color frameColor;
    private Color fillColor;
    private Color iconColor;
    private float badgeSize = 46f;
    private float spacing = 56f;
    private bool built;

    private static Sprite circleSprite;
    private static readonly Dictionary<EquipmentTag, Sprite> TagSprites = new();

    public static BattleEquipmentBadgeStrip Attach(
        RectTransform parent,
        Color frame,
        Color fill,
        Color icon,
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
            icon,
            size,
            gap);

        return strip;
    }

    public void Configure(
        Color frame,
        Color fill,
        Color icon,
        float size,
        float gap)
    {
        frameColor = frame;
        fillColor = fill;
        iconColor = icon;
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

            if (icons[i] != null)
            {
                icons[i].sprite =
                    GetTagSprite(
                        tag);

                icons[i].color =
                    iconColor;
            }


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

        // 원 중심이 카드 하단선에 정확히 걸립니다.
        rect.anchoredPosition =
            Vector2.zero;

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

            RectTransform iconRect =
                CreateRect(
                    inner,
                    "Icon",
                    Vector2.one *
                    Mathf.Max(
                        15f,
                        badgeSize * 0.46f));

            iconRect.anchorMin =
                iconRect.anchorMax =
                    new Vector2(
                        0.5f,
                        0.5f);

            iconRect.pivot =
                new Vector2(
                    0.5f,
                    0.5f);

            Image iconImage =
                iconRect.gameObject.AddComponent<Image>();

            iconImage.raycastTarget =
                false;

            iconImage.preserveAspect =
                true;

            iconImage.color =
                iconColor;

            badgeRoots[i] =
                badge;

            icons[i] =
                iconImage;

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

    private static Sprite GetTagSprite(
        EquipmentTag tag)
    {
        if (TagSprites.TryGetValue(
                tag,
                out Sprite cached) &&
            cached != null)
        {
            return cached;
        }

        const int size = 64;

        Texture2D texture =
            new(
                size,
                size,
                TextureFormat.RGBA32,
                false);

        texture.name =
            "BattleUiTag_" +
            tag +
            "_Runtime";

        texture.wrapMode =
            TextureWrapMode.Clamp;

        texture.filterMode =
            FilterMode.Bilinear;

        Color32[] pixels =
            new Color32[
                size *
                size];

        DrawTagIcon(
            pixels,
            size,
            tag);

        texture.SetPixels32(
            pixels);

        texture.Apply(
            false,
            true);

        Sprite sprite =
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

        sprite.name =
            texture.name +
            "_Sprite";

        TagSprites[tag] =
            sprite;

        return sprite;
    }

    private static void DrawTagIcon(
        Color32[] pixels,
        int size,
        EquipmentTag tag)
    {
        int c =
            size / 2;

        switch (tag)
        {
            case EquipmentTag.Shock:
                DrawLine(pixels, size, c + 7, 7, c - 6, 29, 5);
                DrawLine(pixels, size, c - 6, 29, c + 3, 29, 5);
                DrawLine(pixels, size, c + 3, 29, c - 8, 57, 5);
                DrawLine(pixels, size, c - 8, 57, c + 11, 34, 5);
                DrawLine(pixels, size, c + 11, 34, c + 2, 34, 5);
                DrawLine(pixels, size, c + 2, 34, c + 7, 7, 5);
                break;

            case EquipmentTag.Burn:
                DrawLine(pixels, size, c, 7, c - 11, 27, 5);
                DrawLine(pixels, size, c - 11, 27, c - 7, 48, 5);
                DrawLine(pixels, size, c - 7, 48, c, 57, 5);
                DrawLine(pixels, size, c, 57, c + 11, 45, 5);
                DrawLine(pixels, size, c + 11, 45, c + 8, 25, 5);
                DrawLine(pixels, size, c + 8, 25, c, 7, 5);
                DrawLine(pixels, size, c, 25, c - 3, 43, 4);
                break;

            case EquipmentTag.Explosion:
                DrawFilledCircle(pixels, size, c, c, 6);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI * 0.25f;
                    DrawLine(
                        pixels,
                        size,
                        c + Mathf.RoundToInt(Mathf.Cos(a) * 12f),
                        c + Mathf.RoundToInt(Mathf.Sin(a) * 12f),
                        c + Mathf.RoundToInt(Mathf.Cos(a) * 26f),
                        c + Mathf.RoundToInt(Mathf.Sin(a) * 26f),
                        4);
                }
                break;

            case EquipmentTag.Area:
                DrawCircle(pixels, size, c, c, 22, 4);
                DrawCircle(pixels, size, c, c, 10, 3);
                break;

            case EquipmentTag.Projectile:
                DrawLine(pixels, size, 8, c, 51, c, 5);
                DrawLine(pixels, size, 51, c, 38, c + 12, 5);
                DrawLine(pixels, size, 51, c, 38, c - 12, 5);
                break;

            case EquipmentTag.Melee:
                DrawLine(pixels, size, 13, 12, 51, 50, 5);
                DrawLine(pixels, size, 51, 12, 13, 50, 5);
                DrawLine(pixels, size, 10, 47, 18, 55, 5);
                DrawLine(pixels, size, 54, 47, 46, 55, 5);
                break;

            case EquipmentTag.Dash:
                DrawChevron(pixels, size, 15, c, 12, 5);
                DrawChevron(pixels, size, 33, c, 12, 5);
                break;

            case EquipmentTag.Summon:
                DrawCircle(pixels, size, c, c, 20, 4);
                DrawLine(pixels, size, c, 16, c, 48, 4);
                DrawLine(pixels, size, 16, c, 48, c, 4);
                break;

            case EquipmentTag.Control:
                DrawLine(pixels, size, 13, 14, 13, 50, 4);
                DrawLine(pixels, size, 13, 14, 23, 14, 4);
                DrawLine(pixels, size, 13, 50, 23, 50, 4);
                DrawLine(pixels, size, 51, 14, 51, 50, 4);
                DrawLine(pixels, size, 51, 14, 41, 14, 4);
                DrawLine(pixels, size, 51, 50, 41, 50, 4);
                DrawFilledCircle(pixels, size, c, c, 5);
                break;

            case EquipmentTag.Defense:
                DrawLine(pixels, size, c, 7, 13, 16, 5);
                DrawLine(pixels, size, 13, 16, 16, 39, 5);
                DrawLine(pixels, size, 16, 39, c, 56, 5);
                DrawLine(pixels, size, c, 56, 48, 39, 5);
                DrawLine(pixels, size, 48, 39, 51, 16, 5);
                DrawLine(pixels, size, 51, 16, c, 7, 5);
                break;

            case EquipmentTag.Heal:
                FillRect(pixels, size, c - 4, 11, 9, 42);
                FillRect(pixels, size, 11, c - 4, 42, 9);
                break;

            case EquipmentTag.Resource:
                DrawLine(pixels, size, c, 7, 53, c, 5);
                DrawLine(pixels, size, 53, c, c, 57, 5);
                DrawLine(pixels, size, c, 57, 11, c, 5);
                DrawLine(pixels, size, 11, c, c, 7, 5);
                break;

            case EquipmentTag.Sustain:
                DrawCircle(pixels, size, 22, c, 11, 4);
                DrawCircle(pixels, size, 42, c, 11, 4);
                DrawLine(pixels, size, 28, 24, 36, 40, 3);
                DrawLine(pixels, size, 28, 40, 36, 24, 3);
                break;

            case EquipmentTag.Critical:
                DrawLine(pixels, size, c, 8, c, 42, 6);
                DrawFilledCircle(pixels, size, c, 53, 4);
                DrawLine(pixels, size, 14, 14, 21, 21, 3);
                DrawLine(pixels, size, 50, 14, 43, 21, 3);
                break;

            case EquipmentTag.Precision:
                DrawCircle(pixels, size, c, c, 18, 4);
                DrawCircle(pixels, size, c, c, 6, 3);
                DrawLine(pixels, size, c, 4, c, 18, 3);
                DrawLine(pixels, size, c, 46, c, 60, 3);
                DrawLine(pixels, size, 4, c, 18, c, 3);
                DrawLine(pixels, size, 46, c, 60, c, 3);
                break;

            case EquipmentTag.Break:
                DrawLine(pixels, size, 29, 6, 21, 27, 5);
                DrawLine(pixels, size, 21, 27, 33, 34, 5);
                DrawLine(pixels, size, 33, 34, 25, 58, 5);
                DrawLine(pixels, size, 33, 34, 47, 24, 4);
                break;

            case EquipmentTag.OddWeapon:
                DrawCircle(pixels, size, c, 22, 13, 4);
                DrawLine(pixels, size, 44, 22, 44, 29, 4);
                DrawLine(pixels, size, 44, 29, c, 38, 4);
                DrawLine(pixels, size, c, 38, c, 44, 4);
                DrawFilledCircle(pixels, size, c, 54, 4);
                break;

            default:
                DrawCircle(pixels, size, c, c, 18, 4);
                break;
        }
    }

    private static void DrawChevron(
        Color32[] pixels,
        int size,
        int x,
        int y,
        int half,
        int thickness)
    {
        DrawLine(
            pixels,
            size,
            x,
            y - half,
            x + half,
            y,
            thickness);

        DrawLine(
            pixels,
            size,
            x + half,
            y,
            x,
            y + half,
            thickness);
    }

    private static void DrawLine(
        Color32[] pixels,
        int size,
        int x0,
        int y0,
        int x1,
        int y1,
        int thickness)
    {
        int dx =
            Mathf.Abs(
                x1 - x0);

        int dy =
            Mathf.Abs(
                y1 - y0);

        int steps =
            Mathf.Max(
                1,
                Mathf.Max(
                    dx,
                    dy));

        for (int i = 0; i <= steps; i++)
        {
            float t =
                i /
                (float)steps;

            int x =
                Mathf.RoundToInt(
                    Mathf.Lerp(
                        x0,
                        x1,
                        t));

            int y =
                Mathf.RoundToInt(
                    Mathf.Lerp(
                        y0,
                        y1,
                        t));

            DrawFilledCircle(
                pixels,
                size,
                x,
                y,
                Mathf.Max(
                    1,
                    thickness / 2));
        }
    }

    private static void DrawCircle(
        Color32[] pixels,
        int size,
        int cx,
        int cy,
        int radius,
        int thickness)
    {
        int inner =
            Mathf.Max(
                0,
                radius - thickness);

        int outer2 =
            radius *
            radius;

        int inner2 =
            inner *
            inner;

        for (int y = cy - radius; y <= cy + radius; y++)
        {
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                int dx =
                    x - cx;

                int dy =
                    y - cy;

                int d2 =
                    dx * dx +
                    dy * dy;

                if (d2 <= outer2 &&
                    d2 >= inner2)
                {
                    SetPixel(
                        pixels,
                        size,
                        x,
                        y);
                }
            }
        }
    }

    private static void DrawFilledCircle(
        Color32[] pixels,
        int size,
        int cx,
        int cy,
        int radius)
    {
        int r2 =
            radius *
            radius;

        for (int y = cy - radius; y <= cy + radius; y++)
        {
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                int dx =
                    x - cx;

                int dy =
                    y - cy;

                if (dx * dx +
                    dy * dy <= r2)
                {
                    SetPixel(
                        pixels,
                        size,
                        x,
                        y);
                }
            }
        }
    }

    private static void FillRect(
        Color32[] pixels,
        int size,
        int x,
        int y,
        int width,
        int height)
    {
        for (int yy = y; yy < y + height; yy++)
        {
            for (int xx = x; xx < x + width; xx++)
            {
                SetPixel(
                    pixels,
                    size,
                    xx,
                    yy);
            }
        }
    }

    private static void SetPixel(
        Color32[] pixels,
        int size,
        int x,
        int y)
    {
        if (x < 0 ||
            y < 0 ||
            x >= size ||
            y >= size)
        {
            return;
        }

        pixels[
            y * size +
            x] =
            new Color32(
                255,
                255,
                255,
                255);
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
            new Color32[
                size *
                size];

        float center =
            (size - 1) *
            0.5f;

        float radius =
            center -
            1f;

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

                pixels[
                    y * size +
                    x] =
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
