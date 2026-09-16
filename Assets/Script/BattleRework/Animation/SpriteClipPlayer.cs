using System;
using UnityEngine;

/// <summary>
/// Sprite[] 기반 애니메이션의 공통 프레임 재생기입니다.
/// MonoBehaviour가 아니므로 Player/Enemy/Projectile/Effect/Weapon 등 각 Owner가 Tick을 호출해 사용합니다.
/// Sprite 적용 방식은 Action<Sprite>로 주입해 SpriteRenderer뿐 아니라 WeaponDisplay 같은 브리지에도 재사용할 수 있습니다.
/// </summary>
public sealed class SpriteClipPlayer
{
    private readonly Action<Sprite> applySprite;

    private Sprite[] frames;
    private float fps = 12f;
    private bool loop;
    private int frameIndex;
    private float frameTimer;
    private Action onComplete;
    private bool isPlaying;
    private bool completeSingleFrameOnNextTick;

    public Sprite[] CurrentFrames => frames;
    public int CurrentFrameIndex => frameIndex;
    public float CurrentFps => fps;
    public bool IsLooping => loop;
    public bool IsPlaying => isPlaying;

    public float NormalizedProgress =>
        frames == null || frames.Length == 0
            ? 0f
            : (float)frameIndex / frames.Length;

    public SpriteClipPlayer(Action<Sprite> applySprite)
    {
        this.applySprite = applySprite;
    }

    public void Play(
        Sprite[] sprites,
        float framesPerSecond,
        bool shouldLoop,
        Action complete = null,
        bool completeEmptyImmediately = false,
        bool singleFrameCompletesNextTick = false)
    {
        frames = sprites;
        fps = Mathf.Max(0.01f, framesPerSecond);
        loop = shouldLoop;
        frameIndex = 0;
        frameTimer = 0f;
        onComplete = complete;
        completeSingleFrameOnNextTick = false;

        if (frames == null || frames.Length == 0)
        {
            isPlaying = false;

            if (!loop && completeEmptyImmediately)
                InvokeCompletion();

            return;
        }

        isPlaying = true;
        ApplyFrame(0);

        if (!loop && frames.Length == 1 && singleFrameCompletesNextTick)
            completeSingleFrameOnNextTick = true;
    }

    public void Stop(bool clearClip = true)
    {
        isPlaying = false;
        frameTimer = 0f;
        onComplete = null;
        completeSingleFrameOnNextTick = false;

        if (!clearClip)
            return;

        frames = null;
        frameIndex = 0;
        loop = false;
    }

    public void Reset()
    {
        Stop(true);
        fps = 12f;
    }

    public void Tick(float deltaTime)
    {
        if (!isPlaying)
            return;

        if (completeSingleFrameOnNextTick)
        {
            completeSingleFrameOnNextTick = false;
            CompletePlayback();
            return;
        }

        if (frames == null || frames.Length == 0)
        {
            isPlaying = false;
            return;
        }

        frameTimer += Mathf.Max(0f, deltaTime);
        float frameDuration = 1f / Mathf.Max(0.01f, fps);

        while (isPlaying && frameTimer >= frameDuration)
        {
            frameTimer -= frameDuration;
            AdvanceFrame();
        }
    }

    private void AdvanceFrame()
    {
        if (frames == null || frames.Length == 0)
        {
            isPlaying = false;
            return;
        }

        int nextIndex = frameIndex + 1;
        if (nextIndex < frames.Length)
        {
            frameIndex = nextIndex;
            ApplyFrame(frameIndex);
            return;
        }

        if (loop)
        {
            frameIndex = 0;
            ApplyFrame(frameIndex);
            return;
        }

        frameIndex = frames.Length - 1;
        ApplyFrame(frameIndex);
        CompletePlayback();
    }

    private void CompletePlayback()
    {
        isPlaying = false;
        frameTimer = 0f;
        InvokeCompletion();
    }

    private void InvokeCompletion()
    {
        Action callback = onComplete;
        onComplete = null;
        callback?.Invoke();
    }

    private void ApplyFrame(int index)
    {
        if (applySprite == null || frames == null || frames.Length == 0)
            return;

        int safeIndex = Mathf.Clamp(index, 0, frames.Length - 1);
        Sprite sprite = frames[safeIndex];
        if (sprite != null)
            applySprite(sprite);
    }
}
