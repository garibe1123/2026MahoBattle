using UnityEngine;

public enum AnimPhase { None, Start, Idle, End }

[RequireComponent(typeof(SpriteRenderer))]
public class EffectAnimator : MonoBehaviour
{
    private SpriteRenderer sr;
    private Collider2D col;
    private LineRenderer lr;

    private EffectVisualSO visualSO;
    private Sprite[] frames;
    private float fps;
    private bool loop;
    private int index;
    private float timer;
    private System.Action onComplete;

    private Sprite defaultSprite;
    private Material defaultSpriteMaterial;
    private Material defaultLineMaterial;

    public AnimPhase currentPhase { get; private set; }
    public int currentFrameIndex => index;

    private MaterialPropertyBlock mpb;
    private static readonly int DissolveID = Shader.PropertyToID("_DissolveAmount");
    private static readonly int ColorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int MainTexID = Shader.PropertyToID("_MainTex");

    public float normalizedProgress
    {
        get { return (frames == null || frames.Length == 0) ? 0f : (float)index / frames.Length; }
    }

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        lr = GetComponent<LineRenderer>();
        mpb = new MaterialPropertyBlock();

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

    public void PlayOnce(AnimPhase phase, Sprite[] sprites, float fps, System.Action onComplete)
    {
        PlayInternal(phase, sprites, fps, false, onComplete);
    }

    private void PlayInternal(AnimPhase phase, Sprite[] sprites, float fps, bool loop, System.Action onComplete)
    {
        currentPhase = phase;
        frames = sprites;
        this.fps = Mathf.Max(0.01f, fps);
        this.loop = loop;
        this.onComplete = onComplete;
        index = 0;
        timer = 0f;

        if (frames != null && frames.Length > 0 && sr != null)
            sr.sprite = frames[0];
        if (col != null)
            col.enabled = phase == AnimPhase.Idle;
    }

    public void ResetForPool()
    {
        visualSO = null;
        frames = null;
        fps = 0f;
        loop = false;
        index = 0;
        timer = 0f;
        onComplete = null;
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

        if (frames == null || frames.Length == 0)
            return;

        timer += Time.deltaTime;
        float frameTime = 1f / fps;

        if (timer < frameTime)
            return;

        timer -= frameTime;
        index++;

        if (index >= frames.Length)
        {
            if (loop)
            {
                index = 0;
            }
            else
            {
                index = frames.Length - 1;
                System.Action callback = onComplete;
                onComplete = null;
                callback?.Invoke();
                return;
            }
        }

        if (sr != null)
            sr.sprite = frames[index];
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
}
