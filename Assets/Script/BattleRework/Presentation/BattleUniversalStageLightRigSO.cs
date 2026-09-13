using System;
using UnityEngine;

/// <summary>
/// 2x2 Universal Stage Decor용 조명 Rig 템플릿입니다.
///
/// 핵심 규칙:
/// - Light Base는 필수입니다.
/// - Light Head는 Base의 Mount Anchor와 Head의 Connector Anchor가 정확히 맞닿도록 자동 배치됩니다.
/// - Sprite Pivot과 무관하게 Sprite bounds + normalized anchor를 사용합니다.
/// - 상하 반전/180도 회전은 사용하지 않습니다.
/// - 필요하면 좌우 Mirror만 허용합니다.
/// </summary>
[CreateAssetMenu(
    fileName = "BattleUniversalStageLightRig_Default",
    menuName = "MahoBattle/Presentation/Universal Stage Light Rig",
    order = 31)]
public sealed class BattleUniversalStageLightRigSO : ScriptableObject
{
    [Header("REQUIRED BASE")]
    [Tooltip("조명 스탠드/베이스 Sprite입니다. Light Rig에는 반드시 필요합니다.")]
    [SerializeField] private Sprite baseSprite;
    [SerializeField] private Vector2 baseScale = Vector2.one;
    [SerializeField] private Vector2 baseLocalOffset = Vector2.zero;

    [Header("BASE CONNECTION ANCHOR")]
    [Tooltip("Base Sprite 내부에서 Light Head가 연결될 위치입니다. (0,0)=좌하단, (1,1)=우상단")]
    [SerializeField] private Vector2 baseMountAnchor = new(0.5f, 0.78f);

    [Header("LIGHT HEAD VARIANTS")]
    [Tooltip("Base 위에 연결될 조명 Head Sprite 후보입니다. 하나 이상 지정합니다.")]
    [SerializeField] private Sprite[] lightHeadVariants;
    [SerializeField] private Vector2 headScale = Vector2.one;
    [Tooltip("Light Head Sprite 내부에서 Base Mount와 맞닿을 연결점입니다. (0,0)=좌하단, (1,1)=우상단")]
    [SerializeField] private Vector2 headConnectorAnchor = new(0.5f, 0.10f);
    [Tooltip("Anchor 결합 후 Head만 미세 조정하는 Offset입니다.")]
    [SerializeField] private Vector2 headFineOffset = Vector2.zero;

    [Header("RIG")]
    [SerializeField] private Vector2 overallScale = Vector2.one;
    [Tooltip("Rig 전체를 좌우 반전할 수 있습니다. 상하 반전이나 180도 회전은 하지 않습니다.")]
    [SerializeField] private bool allowRandomMirrorX;
    [Tooltip("Stage Decor 기본 Sorting Offset에 더해지는 Base 상대값입니다.")]
    [SerializeField] private int baseSortingOffset = 0;
    [Tooltip("Base보다 Light Head가 앞에 보이도록 하는 상대값입니다.")]
    [SerializeField] private int headSortingOffset = 2;

    public Sprite BaseSprite => baseSprite;
    public Sprite[] LightHeadVariants => lightHeadVariants;
    public Vector2 BaseMountAnchor => baseMountAnchor;
    public Vector2 HeadConnectorAnchor => headConnectorAnchor;
    public bool AllowRandomMirrorX => allowRandomMirrorX;

    public bool IsValid
    {
        get
        {
            if (baseSprite == null || lightHeadVariants == null)
                return false;
            for (int i = 0; i < lightHeadVariants.Length; i++)
                if (lightHeadVariants[i] != null)
                    return true;
            return false;
        }
    }

    public Transform BuildRuntime(
        Transform parent,
        SpriteRenderer floorReference,
        int entrySortingOffset,
        System.Random random)
    {
        if (parent == null || !IsValid)
            return null;

        Sprite headSprite = PickHead(random);
        if (headSprite == null)
            return null;

        GameObject rigObject = new($"DecorObject_LIGHTRIG_{SafeName(name)}");
        rigObject.transform.SetParent(parent, false);
        Transform rigRoot = rigObject.transform;

        GameObject baseObject = new("LightBase");
        baseObject.transform.SetParent(rigRoot, false);
        Transform baseTransform = baseObject.transform;
        baseTransform.localPosition = new Vector3(baseLocalOffset.x, baseLocalOffset.y, 0f);
        baseTransform.localScale = new Vector3(
            SafeScale(baseScale.x) * SafeScale(overallScale.x),
            SafeScale(baseScale.y) * SafeScale(overallScale.y),
            1f);

        SpriteRenderer baseRenderer = baseObject.AddComponent<SpriteRenderer>();
        baseRenderer.sprite = baseSprite;
        ApplySorting(baseRenderer, floorReference, entrySortingOffset + baseSortingOffset);

        GameObject headObject = new("LightHead");
        headObject.transform.SetParent(rigRoot, false);
        Transform headTransform = headObject.transform;
        headTransform.localScale = new Vector3(
            SafeScale(headScale.x) * SafeScale(overallScale.x),
            SafeScale(headScale.y) * SafeScale(overallScale.y),
            1f);

        SpriteRenderer headRenderer = headObject.AddComponent<SpriteRenderer>();
        headRenderer.sprite = headSprite;
        ApplySorting(headRenderer, floorReference, entrySortingOffset + headSortingOffset);

        Vector2 baseMount = GetScaledAnchorPoint(
            baseSprite,
            baseMountAnchor,
            new Vector2(baseTransform.localScale.x, baseTransform.localScale.y));
        Vector2 headConnector = GetScaledAnchorPoint(
            headSprite,
            headConnectorAnchor,
            new Vector2(headTransform.localScale.x, headTransform.localScale.y));

        Vector2 targetConnector = baseLocalOffset + baseMount + headFineOffset;
        headTransform.localPosition = new Vector3(
            targetConnector.x - headConnector.x,
            targetConnector.y - headConnector.y,
            -0.01f);

        if (allowRandomMirrorX && random != null && random.NextDouble() < 0.5)
        {
            Vector3 scale = rigRoot.localScale;
            scale.x = -Mathf.Abs(scale.x);
            rigRoot.localScale = scale;
        }

        return rigRoot;
    }

    private Sprite PickHead(System.Random random)
    {
        if (lightHeadVariants == null || lightHeadVariants.Length == 0)
            return null;

        int start = random != null ? random.Next(0, lightHeadVariants.Length) : 0;
        for (int i = 0; i < lightHeadVariants.Length; i++)
        {
            Sprite sprite = lightHeadVariants[(start + i) % lightHeadVariants.Length];
            if (sprite != null)
                return sprite;
        }
        return null;
    }

    private static Vector2 GetScaledAnchorPoint(Sprite sprite, Vector2 normalizedAnchor, Vector2 scale)
    {
        if (sprite == null)
            return Vector2.zero;

        Bounds bounds = sprite.bounds;
        Vector2 anchor = new(
            Mathf.Lerp(bounds.min.x, bounds.max.x, Mathf.Clamp01(normalizedAnchor.x)),
            Mathf.Lerp(bounds.min.y, bounds.max.y, Mathf.Clamp01(normalizedAnchor.y)));
        return new Vector2(anchor.x * scale.x, anchor.y * scale.y);
    }

    private static void ApplySorting(SpriteRenderer renderer, SpriteRenderer floorReference, int offset)
    {
        if (renderer == null)
            return;

        if (floorReference != null)
        {
            renderer.sortingLayerID = floorReference.sortingLayerID;
            renderer.sharedMaterial = floorReference.sharedMaterial;
            renderer.sortingOrder = floorReference.sortingOrder + offset;
        }
        else
        {
            renderer.sortingOrder = offset;
        }
    }

    private static float SafeScale(float value)
    {
        return Mathf.Approximately(value, 0f) ? 1f : value;
    }

    private static string SafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Light";
        return value.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        baseMountAnchor = new Vector2(
            Mathf.Clamp01(baseMountAnchor.x),
            Mathf.Clamp01(baseMountAnchor.y));
        headConnectorAnchor = new Vector2(
            Mathf.Clamp01(headConnectorAnchor.x),
            Mathf.Clamp01(headConnectorAnchor.y));

        baseScale = new Vector2(SafeScale(baseScale.x), SafeScale(baseScale.y));
        headScale = new Vector2(SafeScale(headScale.x), SafeScale(headScale.y));
        overallScale = new Vector2(SafeScale(overallScale.x), SafeScale(overallScale.y));
    }
#endif
}
