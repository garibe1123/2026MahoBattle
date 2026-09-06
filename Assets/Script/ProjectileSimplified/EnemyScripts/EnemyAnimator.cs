using System;
using UnityEngine;

/// <summary>
/// Unity Animator / AnimatorController / AnimationClip을 사용하지 않는 Sprite 전용 Animator입니다.
/// MonsterDefinitionSO.visual에 연결된 Sprite 배열을 프레임 단위로 직접 재생합니다.
/// 기존 EnemyAnimator 이름은 Prefab 직렬화 호환 때문에 유지합니다.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class EnemyAnimator : MonoBehaviour
{
    [Header("Facing Debug")]
    [Tooltip("몬스터 중심에서 방향점 중심까지의 WORLD 거리입니다. Root scale에 의해 멀어지지 않습니다.")]
    [SerializeField] private bool showFacingIndicator = true;
    [SerializeField, Min(0.05f)] private float facingIndicatorDistance = 0.58f;
    [SerializeField, Min(0.02f)] private float facingIndicatorSize = 0.085f;
    [SerializeField] private Color facingIndicatorColor = Color.red;

    private SpriteRenderer spriteRenderer;
    private SpriteRenderer facingIndicatorRenderer;
    private MonsterVisualConfig visual;

    private Sprite[] currentFrames;
    private float currentFps = 10f;
    private bool currentLoop;
    private int frameIndex;
    private float frameTimer;
    private Action onComplete;

    private float flashTimer;
    private Color normalColor = Color.white;
    private Vector2 facing = Vector2.right;

    public EnemyAnimState currentState { get; private set; } = EnemyAnimState.Idle;
    public int CurrentFrameIndex => frameIndex;
    public bool IsPlaying => currentFrames != null && currentFrames.Length > 0;
    public SpriteRenderer SpriteRenderer => spriteRenderer;
    public Vector2 Facing => facing;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        EnsureFacingIndicator();
        ApplyFacingVisual();
    }

    public void SetupVisual(MonsterVisualConfig config)
    {
        visual = config;

        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer == null)
            return;

        if (visual == null)
        {
            spriteRenderer.sprite = null;
            return;
        }

        spriteRenderer.sharedMaterial = visual.customMaterial;
        normalColor = visual.spriteColor;
        spriteRenderer.color = normalColor;

        Sprite fallback = visual.GetFallbackSprite();
        if (fallback != null)
            spriteRenderer.sprite = fallback;

        ApplyFacingVisual();
    }

    public void SetupVisual(EnemyVisualSO legacyVisual)
    {
        if (legacyVisual == null)
        {
            SetupVisual((MonsterVisualConfig)null);
            return;
        }

        MonsterVisualConfig adapter = new()
        {
            idleSprites = legacyVisual.idleSprites,
            moveSprites = legacyVisual.moveSprites,
            attackSprites = legacyVisual.attackSprites,
            dieSprites = legacyVisual.dieSprites,
            fps = Mathf.Max(1f, legacyVisual.fps),
            customMaterial = legacyVisual.customMaterial,
            spriteColor = Color.white,
            hitFlashColor = legacyVisual.hitFlashColor,
            hitFlashDuration = 0.08f,
            sourceFacesRight = true
        };

        SetupVisual(adapter);
    }

    public void Play(EnemyAnimState state, bool loop, Action complete = null, bool restart = false)
    {
        Sprite[] frames = visual != null ? visual.GetFrames(state) : null;
        float fps = visual != null ? visual.fps : 10f;
        Play(state, frames, fps, loop, complete, restart);
    }

    public void Play(
        EnemyAnimState state,
        Sprite[] sprites,
        float fps,
        bool loop,
        Action complete = null,
        bool restart = false)
    {
        Sprite[] resolvedFrames = ResolveFrames(state, sprites);

        if (!restart &&
            currentState == state &&
            currentLoop == loop &&
            ReferenceEquals(currentFrames, resolvedFrames))
        {
            return;
        }

        currentState = state;
        currentFrames = resolvedFrames;
        currentFps = Mathf.Max(1f, fps);
        currentLoop = loop;
        onComplete = complete;
        frameIndex = 0;
        frameTimer = 0f;

        if (currentFrames != null && currentFrames.Length > 0)
        {
            ApplyFrame(0);
            return;
        }

        ApplyFallbackSprite();

        if (!loop)
        {
            Action callback = onComplete;
            onComplete = null;
            callback?.Invoke();
        }
    }

    public void PlayIdle()
    {
        Play(EnemyAnimState.Idle, true);
    }

    public void Stop(bool keepCurrentSprite = true)
    {
        currentFrames = null;
        frameIndex = 0;
        frameTimer = 0f;
        onComplete = null;

        if (!keepCurrentSprite)
            ApplyFallbackSprite();
    }

    public void SetFacing(float horizontalDirection)
    {
        if (Mathf.Abs(horizontalDirection) < 0.001f)
            return;

        SetFacing(new Vector2(horizontalDirection, 0f));
    }

    /// <summary>
    /// 4방향 바라보기를 디버그 점으로 표시하고 실제 Sprite는 좌/우 flip만 수행합니다.
    /// Transform Scale은 건드리지 않으므로 Collider/NavMeshAgent 크기에 영향이 없습니다.
    /// </summary>
    public void SetFacing(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return;

        Vector2 normalized = direction.normalized;
        facing = Mathf.Abs(normalized.x) >= Mathf.Abs(normalized.y)
            ? new Vector2(Mathf.Sign(normalized.x), 0f)
            : new Vector2(0f, Mathf.Sign(normalized.y));

        ApplyFacingVisual(normalized);
    }

    private void ApplyFacingVisual()
    {
        ApplyFacingVisual(facing);
    }

    private void ApplyFacingVisual(Vector2 rawDirection)
    {
        if (spriteRenderer != null && visual != null && Mathf.Abs(rawDirection.x) > 0.001f)
        {
            bool faceLeft = rawDirection.x < 0f;
            spriteRenderer.flipX = visual.sourceFacesRight
                ? faceLeft
                : !faceLeft;
        }

        if (facingIndicatorRenderer == null)
            EnsureFacingIndicator();

        if (facingIndicatorRenderer == null)
            return;

        Transform indicator = facingIndicatorRenderer.transform;
        Vector3 lossy = transform.lossyScale;
        float scaleX = Mathf.Max(0.001f, Mathf.Abs(lossy.x));
        float scaleY = Mathf.Max(0.001f, Mathf.Abs(lossy.y));

        indicator.localPosition = new Vector3(
            facing.x * facingIndicatorDistance / scaleX,
            facing.y * facingIndicatorDistance / scaleY,
            0f);
        indicator.localScale = new Vector3(
            facingIndicatorSize / scaleX,
            facingIndicatorSize / scaleY,
            1f);

        facingIndicatorRenderer.enabled = showFacingIndicator;
        facingIndicatorRenderer.color = facingIndicatorColor;
        facingIndicatorRenderer.sortingOrder = spriteRenderer != null ? spriteRenderer.sortingOrder + 100 : 100;
    }

    private void EnsureFacingIndicator()
    {
        Transform existing = transform.Find("FacingIndicator");
        if (existing != null)
        {
            facingIndicatorRenderer = existing.GetComponent<SpriteRenderer>();
        }
        else
        {
            GameObject indicator = new("FacingIndicator");
            indicator.transform.SetParent(transform, false);
            facingIndicatorRenderer = indicator.AddComponent<SpriteRenderer>();
        }

        if (facingIndicatorRenderer == null)
            return;

        facingIndicatorRenderer.sprite = FacingDebugSpriteCache.Dot;
        facingIndicatorRenderer.color = facingIndicatorColor;
        facingIndicatorRenderer.enabled = showFacingIndicator;
    }

    public void Flash()
    {
        if (spriteRenderer == null || visual == null)
            return;

        flashTimer = Mathf.Max(0f, visual.hitFlashDuration);
        spriteRenderer.color = visual.hitFlashColor;
    }

    public float GetStateDuration(EnemyAnimState state)
    {
        if (visual == null)
            return 0f;

        Sprite[] frames = visual.GetFrames(state);
        if (frames == null || frames.Length == 0)
            return 0f;

        return frames.Length / Mathf.Max(1f, visual.fps);
    }

    private void Update()
    {
        // Runtime scale 보정이 바뀌어도 방향점 world offset은 일정하게 유지합니다.
        ApplyFacingVisual();
        UpdateFlash();
        UpdateFrames();
    }

    private void UpdateFlash()
    {
        if (spriteRenderer == null || flashTimer <= 0f)
            return;

        flashTimer -= Time.deltaTime;
        if (flashTimer <= 0f)
            spriteRenderer.color = normalColor;
    }

    private void UpdateFrames()
    {
        if (currentFrames == null || currentFrames.Length == 0)
            return;

        frameTimer += Time.deltaTime;
        float frameDuration = 1f / Mathf.Max(1f, currentFps);

        while (frameTimer >= frameDuration)
        {
            frameTimer -= frameDuration;
            frameIndex++;

            if (frameIndex >= currentFrames.Length)
            {
                if (currentLoop)
                {
                    frameIndex = 0;
                }
                else
                {
                    frameIndex = currentFrames.Length - 1;
                    ApplyFrame(frameIndex);

                    Action callback = onComplete;
                    onComplete = null;
                    currentFrames = null;
                    callback?.Invoke();
                    return;
                }
            }

            ApplyFrame(frameIndex);
        }
    }

    private Sprite[] ResolveFrames(EnemyAnimState state, Sprite[] requested)
    {
        if (HasFrames(requested))
            return requested;

        if (visual == null)
            return requested;

        Sprite[] stateFrames = visual.GetFrames(state);
        if (HasFrames(stateFrames))
            return stateFrames;

        if (state != EnemyAnimState.Idle && HasFrames(visual.idleSprites))
            return visual.idleSprites;

        return requested;
    }

    private void ApplyFrame(int index)
    {
        if (spriteRenderer == null || currentFrames == null || currentFrames.Length == 0)
            return;

        int safeIndex = Mathf.Clamp(index, 0, currentFrames.Length - 1);
        Sprite sprite = currentFrames[safeIndex];
        if (sprite != null)
            spriteRenderer.sprite = sprite;
    }

    private void ApplyFallbackSprite()
    {
        if (spriteRenderer == null || visual == null)
            return;

        Sprite fallback = visual.GetFallbackSprite();
        if (fallback != null)
            spriteRenderer.sprite = fallback;
    }

    private static bool HasFrames(Sprite[] frames)
    {
        if (frames == null || frames.Length == 0)
            return false;

        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null)
                return true;
        }

        return false;
    }
}
