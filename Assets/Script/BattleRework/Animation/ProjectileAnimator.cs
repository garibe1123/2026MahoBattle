using System;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class ProjectileAnimator : MonoBehaviour
{
    private SpriteRenderer sr;
    private SpriteClipPlayer clipPlayer;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        clipPlayer = new SpriteClipPlayer(ApplySprite);
    }

    public void PlayLoop(Sprite[] sprites, float fps)
    {
        EnsureClipPlayer();
        clipPlayer.Play(
            sprites,
            fps,
            shouldLoop: true);
    }

    public void PlayOnce(Sprite[] sprites, float fps, Action onComplete)
    {
        EnsureClipPlayer();
        clipPlayer.Play(
            sprites,
            fps,
            shouldLoop: false,
            complete: onComplete,
            completeEmptyImmediately: true,
            singleFrameCompletesNextTick: true);
    }

    public void Stop(bool clearClip = true)
    {
        EnsureClipPlayer();
        clipPlayer.Stop(clearClip);
    }

    private void Update()
    {
        clipPlayer?.Tick(Time.deltaTime);
    }

    private void EnsureClipPlayer()
    {
        if (clipPlayer == null)
            clipPlayer = new SpriteClipPlayer(ApplySprite);
    }

    private void ApplySprite(Sprite sprite)
    {
        if (sr != null && sprite != null)
            sr.sprite = sprite;
    }
}
