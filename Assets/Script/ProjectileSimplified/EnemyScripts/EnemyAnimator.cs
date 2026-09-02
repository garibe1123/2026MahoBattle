using System;
using UnityEngine;

/// <summary>
/// Unity Animator / AnimatorController / AnimationClip을 사용하지 않는 Sprite 전용 Animator입니다.
/// MonsterDefinitionSO.visual에 연결된 Sprite 배열을 프레임 단위로 직접 재생합니다.
///
/// 기존 EnemyAnimator 컴포넌트 이름은 Prefab 직렬화 호환 때문에 유지하지만,
/// 내부 구현은 완전히 자체 Sprite Animator입니다.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class EnemyAnimator : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private MonsterVisualConfig visual;

    private Sprite[] currentFrames;
    private float currentFps = 10f;
    private bool currentLoop;
    private int frameIndex;
    private float frameTimer;
    private Action onComplete;

    private float flashTimer;
    private Color normalColor = Color.white;

    public EnemyAnimState currentState { get; private set; } = EnemyAnimState.Idle;
    public int CurrentFrameIndex => frameIndex;
    public bool IsPlaying => currentFrames != null && currentFrames.Length > 0;
    public SpriteRenderer SpriteRenderer => spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>
    /// EnemyDefinitionSO 내부 Visual 설정을 직접 연결합니다.
    /// 별도의 EnemyVisualSO Asset은 새 BattleRework 경로에서는 필요하지 않습니다.
    /// </summary>
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
    }

    /// <summary>
    /// 구형 Enemy.cs / EnemySO가 아직 같은 Prefab Animator를 사용하므로 남겨 둔 호환 오버로드입니다.
    /// 새 전투 시스템에서는 사용하지 않고 MonsterDefinitionSO.visual을 직접 사용합니다.
    /// </summary>
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

    /// <summary>
    /// 현재 EnemyDefinition에 등록된 상태 Sprite를 자동으로 찾아 재생합니다.
    /// </summary>
    public void Play(EnemyAnimState state, bool loop, Action complete = null, bool restart = false)
    {
        Sprite[] frames = visual != null ? visual.GetFrames(state) : null;
        float fps = visual != null ? visual.fps : 10f;
        Play(state, frames, fps, loop, complete, restart);
    }

    /// <summary>
    /// 기존 MonsterController/Legacy Enemy 호출과 호환되는 직접 Sprite 배열 재생 API입니다.
    /// 비어 있는 배열이 들어오면 해당 State의 Visual 배열 → Idle → Preview Sprite 순으로 fallback 합니다.
    /// </summary>
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

    /// <summary>
    /// Sprite 원본이 오른쪽을 보고 있다는 기준으로 좌우 반전합니다.
    /// Transform Scale을 뒤집지 않기 때문에 Collider/NavMeshAgent 크기에는 영향을 주지 않습니다.
    /// </summary>
    public void SetFacing(float horizontalDirection)
    {
        if (spriteRenderer == null || visual == null || Mathf.Abs(horizontalDirection) < 0.001f)
            return;

        bool faceLeft = horizontalDirection < 0f;
        spriteRenderer.flipX = visual.sourceFacesRight
            ? faceLeft
            : !faceLeft;
    }

    /// <summary>
    /// 전용 Flash Shader 없이도 동작하도록 SpriteRenderer Color를 짧게 변경합니다.
    /// </summary>
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
        UpdateFlash();
        UpdateFrames();
    }

    private void UpdateFlash()
    {
        if (spriteRenderer == null)
            return;

        if (flashTimer <= 0f)
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

        // 행동 Sprite가 아직 제작되지 않은 테스트 단계에서는 Idle로 fallback 합니다.
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
