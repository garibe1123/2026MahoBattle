using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Unity Animator를 사용하지 않는 Player Sprite Animator.
/// SpriteRenderer.flipX만 사용해 좌우를 뒤집으므로 Player Transform/Collider 크기는 유지됩니다.
/// 테스트 단계에서는 마지막 바라보는 방향을 빨간 점으로 표시할 수 있습니다.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerAnimator : MonoBehaviour
{
    [Header("Facing Debug")]
    [SerializeField] private bool showFacingIndicator = true;
    [SerializeField, Min(0.05f)] private float facingIndicatorDistance = 0.62f;
    [SerializeField, Min(0.02f)] private float facingIndicatorSize = 0.12f;
    [SerializeField] private Color facingIndicatorColor = Color.red;

    private SpriteRenderer sr;
    private SpriteRenderer facingIndicatorRenderer;
    private Vector2 facing = Vector2.right;

    private Sprite[] currentFrames;
    private float timer;
    private int frameIndex;
    private float fps = 10f;
    private bool isLooping;
    private Action onComplete;

    public Vector2 Facing => facing;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        EnsureFacingIndicator();
        ApplyFacingVisual();
    }

    /// <summary>
    /// Controller가 매 프레임 호출하는 메인 Sprite 갱신 함수.
    /// 이동 입력이 있으면 마지막 바라보는 방향도 함께 갱신합니다.
    /// </summary>
    public void UpdateAnimation(PlayerState state, Vector2 moveDir, PlayerSpriteSO so)
    {
        if (so == null)
            return;

        fps = Mathf.Max(1f, so.fps);

        switch (state)
        {
            case PlayerState.Idle:
                PlayLoop(so.idleSprites);
                break;
            case PlayerState.Move:
                PlayLoop(so.moveSprites);
                break;
            case PlayerState.Roll:
                PlayOnce(so.rollSprites);
                break;
        }

        if (moveDir.sqrMagnitude > 0.001f)
            SetFacing(moveDir);
    }

    /// <summary>
    /// 이동/조준 방향을 4방향으로 정규화해 디버그 점 위치를 갱신합니다.
    /// 실제 Sprite는 현재 한 방향 원본을 좌우 flip하는 방식이므로 상/하는 점으로 확인합니다.
    /// </summary>
    public void SetFacing(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return;

        Vector2 normalized = direction.normalized;
        facing = Mathf.Abs(normalized.x) >= Mathf.Abs(normalized.y)
            ? new Vector2(Mathf.Sign(normalized.x), 0f)
            : new Vector2(0f, Mathf.Sign(normalized.y));

        ApplyFacingVisual();
    }

    private void ApplyFacingVisual()
    {
        if (sr != null && Mathf.Abs(facing.x) > 0.001f)
            sr.flipX = facing.x < 0f;

        if (facingIndicatorRenderer == null)
            EnsureFacingIndicator();

        if (facingIndicatorRenderer == null)
            return;

        facingIndicatorRenderer.enabled = showFacingIndicator;
        facingIndicatorRenderer.color = facingIndicatorColor;
        facingIndicatorRenderer.transform.localPosition = (Vector3)(facing * facingIndicatorDistance);
        facingIndicatorRenderer.transform.localScale = Vector3.one * facingIndicatorSize;
        facingIndicatorRenderer.sortingOrder = sr != null ? sr.sortingOrder + 100 : 100;
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

    private void PlayLoop(Sprite[] frames) => PlayInternal(frames, true);
    private void PlayOnce(Sprite[] frames, Action callback = null) => PlayInternal(frames, false, callback);

    private void PlayInternal(Sprite[] frames, bool loop, Action callback = null)
    {
        if (ReferenceEquals(currentFrames, frames))
            return;

        currentFrames = frames;
        isLooping = loop;
        onComplete = callback;
        frameIndex = 0;
        timer = 0f;

        if (frames != null && frames.Length > 0 && frames[0] != null && sr != null)
            sr.sprite = frames[0];
    }

    private void Update()
    {
        if (currentFrames == null || currentFrames.Length <= 1 || sr == null)
            return;

        timer += Time.deltaTime;
        float frameDuration = 1f / Mathf.Max(1f, fps);

        while (timer >= frameDuration)
        {
            timer -= frameDuration;
            frameIndex++;

            if (frameIndex >= currentFrames.Length)
            {
                if (isLooping)
                {
                    frameIndex = 0;
                }
                else
                {
                    frameIndex = currentFrames.Length - 1;
                    onComplete?.Invoke();
                    onComplete = null;
                    return;
                }
            }

            Sprite next = currentFrames[frameIndex];
            if (next != null)
                sr.sprite = next;
        }
    }

    public void StartBlink(float duration)
    {
        if (gameObject.activeInHierarchy)
            StartCoroutine(BlinkRoutine(duration));
    }

    private IEnumerator BlinkRoutine(float duration)
    {
        if (sr == null)
            yield break;

        Color original = sr.color;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            Color faded = original;
            faded.a *= 0.4f;
            sr.color = faded;
            yield return new WaitForSeconds(0.1f);
            sr.color = original;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.2f;
        }

        sr.color = original;
    }
}

/// <summary>
/// Player/Enemy가 공용으로 사용하는 런타임 디버그 방향점 Sprite.
/// 별도 Asset 없이 Point Filter 된 작은 빨간 점을 만들기 위한 캐시입니다.
/// </summary>
internal static class FacingDebugSpriteCache
{
    private static Sprite dot;

    public static Sprite Dot => dot != null ? dot : dot = CreateDot();

    private static Sprite CreateDot()
    {
        const int size = 8;
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Vector2 center = new((size - 1) * 0.5f, (size - 1) * 0.5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                texture.SetPixel(x, y, distance <= 2.7f ? Color.white : Color.clear);
            }
        }

        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size,
            0,
            SpriteMeshType.FullRect);
        sprite.name = "RuntimeFacingDebugDot";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
