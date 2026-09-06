using UnityEngine;

/// <summary>
/// Legacy weapon visual bridge.
/// WeaponPivot는 항상 Player 중심을 기준으로 회전하고,
/// WeaponVisual은 Player root scale과 무관하게 일정한 world-space margin만 유지합니다.
/// </summary>
public class WeaponDisplay : MonoBehaviour
{
    public SpriteRenderer weaponSpriteRenderer;
    public Transform pivot;

    [Header("Visual Placement")]
    [Tooltip("Player 중심에서 총 Sprite 중심까지의 WORLD 거리입니다.")]
    [SerializeField, Min(0f)] private float weaponWorldOffset = 0.46f;
    [Tooltip("Placeholder/기본 총 Sprite가 지나치게 커지지 않도록 world 최대 변 길이를 정규화합니다.")]
    [SerializeField] private bool normalizeWeaponWorldSize = true;
    [SerializeField, Min(0.1f)] private float weaponMaxWorldSize = 0.72f;

    public void UpdateWeaponSprite(Sprite newSprite)
    {
        if (weaponSpriteRenderer == null)
            return;

        weaponSpriteRenderer.sprite = newSprite;
        ApplyVisualPlacement();
    }

    private void Awake()
    {
        ResolveReferences();
        ApplyVisualPlacement();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ApplyVisualPlacement();
    }

    private void Update()
    {
        ResolveReferences();
        if (pivot == null || weaponSpriteRenderer == null)
            return;

        // Player Root가 48px 기준으로 scale 보정되어도 총이 멀리 밀려나지 않게 매 frame world offset을 역보정합니다.
        ApplyVisualPlacement();

        Camera main = Camera.main;
        if (main == null)
            return;

        Vector3 mousePos = main.ScreenToWorldPoint(Input.mousePosition);
        mousePos.z = pivot.position.z;

        Vector3 aimDirection = mousePos - pivot.position;
        if (aimDirection.sqrMagnitude <= 0.0001f)
            return;

        float angle = Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg;
        pivot.eulerAngles = new Vector3(0f, 0f, angle);
        weaponSpriteRenderer.flipY = angle > 90f || angle < -90f;
    }

    private void ResolveReferences()
    {
        if (weaponSpriteRenderer == null)
            weaponSpriteRenderer = GetComponent<SpriteRenderer>();

        if (pivot == null)
            pivot = transform.parent != null ? transform.parent : transform;
    }

    private void ApplyVisualPlacement()
    {
        if (pivot == null || weaponSpriteRenderer == null)
            return;

        // Fallback hierarchy는 Player/WeaponPivot/WeaponVisual입니다.
        // Pivot 자체를 Player 중심에 고정해서 과거 local offset이 남아 있어도 멀리 날아가지 않게 합니다.
        if (pivot.parent != null)
            pivot.localPosition = Vector3.zero;

        Transform visual = weaponSpriteRenderer.transform;
        float pivotScaleX = Mathf.Max(0.001f, Mathf.Abs(pivot.lossyScale.x));
        visual.localPosition = new Vector3(weaponWorldOffset / pivotScaleX, 0f, 0f);

        if (!normalizeWeaponWorldSize || weaponSpriteRenderer.sprite == null)
            return;

        Vector2 spriteSize = weaponSpriteRenderer.sprite.bounds.size;
        float spriteMax = Mathf.Max(0.001f, Mathf.Max(spriteSize.x, spriteSize.y));

        Transform visualParent = visual.parent;
        Vector3 parentLossy = visualParent != null ? visualParent.lossyScale : Vector3.one;
        float parentScale = Mathf.Max(
            0.001f,
            Mathf.Max(Mathf.Abs(parentLossy.x), Mathf.Abs(parentLossy.y)));

        float targetLocalScale = Mathf.Max(0.01f, weaponMaxWorldSize / (spriteMax * parentScale));
        visual.localScale = new Vector3(targetLocalScale, targetLocalScale, 1f);
    }
}
