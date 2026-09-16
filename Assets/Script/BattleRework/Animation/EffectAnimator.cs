using System;
using UnityEngine;

public enum AnimPhase { None, Start, Idle, End }

[RequireComponent(typeof(SpriteRenderer))]
public class EffectAnimator : MonoBehaviour
{
    private SpriteRenderer sr;
    private Collider2D col;
    private LineRenderer lr;

    private EffectVisualSO visualSO;
    private SpriteClipPlayer clipPlayer;

    private Sprite defaultSprite;
    private Material defaultSpriteMaterial;
    private Material defaultLineMaterial;

    public AnimPhase currentPhase { get; private set; }
    public int currentFrameIndex => clipPlayer != null ? clipPlayer.CurrentFrameIndex : 0;

    private MaterialPropertyBlock mpb;
    private static readonly int DissolveID = Shader.PropertyToID("_DissolveAmount");
    private static readonly int ColorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int MainTexID = Shader.PropertyToID("_MainTex");

    public float normalizedProgress =>
        clipPlayer != null ? clipPlayer.NormalizedProgress : 0f;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        lr = GetComponent<LineRenderer>();
        mpb = new MaterialPropertyBlock();
        clipPlayer = new SpriteClipPlayer(ApplySprite);

        defaultSprite = sr != null ? sr.sprite : null;
        defaultSpriteMaterial = sr != null ? sr.sharedMaterial : null;
        defaultLineMaterial = lr != null ? lr.sharedMaterial : null;
    }

    public void SetupVisual(EffectVisualSO so)
    {
        visualSO = so;

        if (sr != null)
            sr.sharedMaterial = so != null && so.customMaterial != null
                ? so.customMaterial
                : defaultSpriteMaterial;

        if (lr != null)
            lr.sharedMaterial = so != null && so.customMaterial != null
                ? so.customMaterial
                : defaultLineMaterial;
    }

    public void PlayLoop(AnimPhase phase, Sprite[] sprites, float fps)
    {
        PlayInternal(phase, sprites, fps, true, null);
    }

    public void PlayOnce(AnimPhase phase, Sprite[] sprites, float fps, Action onComplete)
    {
        PlayInternal(phase, sprites, fps, false, onComplete);
    }

    private void PlayInternal(
        AnimPhase phase,
        Sprite[] sprites,
        float fps,
        bool loop,
        Action onComplete)
    {
        currentPhase = phase;
        EnsureClipPlayer();

        clipPlayer.Play(
            sprites,
            fps,
            loop,
            onComplete,
            completeEmptyImmediately: false);

        if (col != null)
            col.enabled = phase == AnimPhase.Idle;
    }

    public void ResetForPool()
    {
        visualSO = null;
        EnsureClipPlayer();
        clipPlayer.Reset();
        currentPhase = AnimPhase.None;

        if (sr != null)
        {
            sr.sprite = defaultSprite;
            sr.sharedMaterial = defaultSpriteMaterial;
            sr.SetPropertyBlock(null);
        }

        if (col != null)
            col.enabled = false;

        if (lr != null)
        {
            lr.enabled = false;
            lr.sharedMaterial = defaultLineMaterial;
            lr.SetPropertyBlock(null);
        }
    }

    private void Update()
    {
        UpdateMaterialProperties();
        clipPlayer?.Tick(Time.deltaTime);
    }

    private void UpdateMaterialProperties()
    {
        if (visualSO == null || mpb == null || sr == null)
            return;

        sr.GetPropertyBlock(mpb);
        mpb.SetColor(ColorID, visualSO.glowColor);

        float shaderValue = 0f;
        if (currentPhase == AnimPhase.Start || currentPhase == AnimPhase.End)
            shaderValue = visualSO.dissolveCurve.Evaluate(normalizedProgress);

        mpb.SetFloat(DissolveID, shaderValue);
        sr.SetPropertyBlock(mpb);

        if (lr != null && sr.sprite != null)
        {
            lr.GetPropertyBlock(mpb);
            mpb.SetTexture(MainTexID, sr.sprite.texture);
            mpb.SetColor(ColorID, visualSO.glowColor);
            mpb.SetFloat(DissolveID, shaderValue);
            lr.SetPropertyBlock(mpb);
        }
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
