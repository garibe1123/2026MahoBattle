using UnityEngine;

public enum AnimPhase { None, Start, Idle, End }

[RequireComponent(typeof(SpriteRenderer))]
public class EffectAnimator : MonoBehaviour
{
    private SpriteRenderer sr;
    private Collider2D col;
    private LineRenderer lr; // ★ 1. 라인 렌더러 변수 추가

    private EffectVisualSO visualSO;
    private Sprite[] frames;
    private float fps;
    private bool loop;
    private int index;
    private float timer;
    private System.Action onComplete;

    public AnimPhase currentPhase { get; private set; }
    public int currentFrameIndex => index;

    private MaterialPropertyBlock mpb;
    private static readonly int DissolveID = Shader.PropertyToID("_DissolveAmount");
    private static readonly int ColorID = Shader.PropertyToID("_EmissionColor");

    // ★ 2. 텍스처를 넘겨주기 위한 ID 추가 (유니티 기본 텍스처 레퍼런스 이름)
    private static readonly int MainTexID = Shader.PropertyToID("_MainTex");

    public float normalizedProgress
    {
        get { return (frames == null || frames.Length == 0) ? 0f : (float)index / frames.Length; }
    }

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        lr = GetComponent<LineRenderer>(); // ★ Awake에서 가져오기
        mpb = new MaterialPropertyBlock();
    }

    public void SetupVisual(EffectVisualSO so)
    {
        visualSO = so;
        if (so != null && so.customMaterial != null)
        {
            sr.sharedMaterial = so.customMaterial;

            // ★ LineRenderer가 있다면, 걔한테도 똑같은 머티리얼을 덮어씌움!
            if (lr != null) lr.sharedMaterial = so.customMaterial;
        }
    }

    // (PlayLoop, PlayOnce, PlayInternal 함수는 기존과 100% 동일하므로 생략)
    public void PlayLoop(AnimPhase phase, Sprite[] sprites, float fps) => PlayInternal(phase, sprites, fps, true, null);
    public void PlayOnce(AnimPhase phase, Sprite[] sprites, float fps, System.Action onComplete) => PlayInternal(phase, sprites, fps, false, onComplete);
    void PlayInternal(AnimPhase phase, Sprite[] sprites, float fps, bool loop, System.Action onComplete)
    {
        this.currentPhase = phase;
        frames = sprites;
        this.fps = Mathf.Max(0.01f, fps);
        this.loop = loop;
        this.onComplete = onComplete;
        index = 0;
        timer = 0f;

        if (frames != null && frames.Length > 0) sr.sprite = frames[0];
        if (col != null) col.enabled = (phase == AnimPhase.Idle);
    }

    void Update()
    {
        UpdateMaterialProperties(); // ★ 매 프레임 쉐이더 & 텍스처 업데이트

        if (frames == null || frames.Length == 0) return;

        timer += Time.deltaTime;
        float frameTime = 1f / fps;

        if (timer >= frameTime)
        {
            timer -= frameTime;
            index++;

            if (index >= frames.Length)
            {
                if (loop) index = 0;
                else
                {
                    index = frames.Length - 1;
                    var cb = onComplete; onComplete = null;
                    cb?.Invoke();
                    return;
                }
            }
            sr.sprite = frames[index];
        }
    }

    void UpdateMaterialProperties()
    {
        if (visualSO == null || mpb == null) return;

        // --- 1. SpriteRenderer 갱신 ---
        sr.GetPropertyBlock(mpb);
        mpb.SetColor(ColorID, visualSO.glowColor);

        float shaderValue = 0f;
        if (currentPhase == AnimPhase.Start || currentPhase == AnimPhase.End)
            shaderValue = visualSO.dissolveCurve.Evaluate(normalizedProgress);

        mpb.SetFloat(DissolveID, shaderValue);
        sr.SetPropertyBlock(mpb);

        // --- ★ 2. LineRenderer 동기화 (텍스처 실시간 교체) ---
        if (lr != null && sr.sprite != null)
        {
            lr.GetPropertyBlock(mpb);

            // 핵심: 현재 Sprite의 텍스처를 LineRenderer의 쉐이더로 쏴줌!
            mpb.SetTexture(MainTexID, sr.sprite.texture);

            mpb.SetColor(ColorID, visualSO.glowColor);
            mpb.SetFloat(DissolveID, shaderValue);

            lr.SetPropertyBlock(mpb);
        }
    }
}