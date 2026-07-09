using UnityEngine;

public enum EnemyAnimState { Idle, Move, Attack, Die }

[RequireComponent(typeof(SpriteRenderer))]
public class EnemyAnimator : MonoBehaviour
{
    private SpriteRenderer sr;
    private EnemyVisualSO visualSO;

    private Sprite[] currentFrames;
    private float fps;
    private bool loop;
    private int index;
    private float timer;
    private System.Action onComplete;

    public EnemyAnimState currentState { get; private set; }

    // 피격 플래시용 쉐이더 제어
    private MaterialPropertyBlock mpb;
    private float flashTimer;
    private static readonly int FlashColorID = Shader.PropertyToID("_EmissionColor"); // 쉐이더 설정에 따라 다를 수 있음
    private static readonly int FlashAmountID = Shader.PropertyToID("_FlashAmount");

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        mpb = new MaterialPropertyBlock();
    }

    public void SetupVisual(EnemyVisualSO so)
    {
        visualSO = so;
        if (so != null && so.customMaterial != null)
        {
            sr.sharedMaterial = so.customMaterial;
        }
    }

    // 현재 재생 중인 상태와 같으면 무시함 (Update에서 매 프레임 호출 방어)
    public void Play(EnemyAnimState state, Sprite[] sprites, float fps, bool loop, System.Action onComplete = null)
    {
        if (currentState == state && this.loop == loop && currentFrames == sprites) return;

        currentState = state;
        currentFrames = sprites;
        this.fps = Mathf.Max(0.01f, fps);
        this.loop = loop;
        this.onComplete = onComplete;

        index = 0;
        timer = 0f;

        if (currentFrames != null && currentFrames.Length > 0) sr.sprite = currentFrames[0];
    }

    // 피격 시 하얗게 번쩍이게 하는 함수 (경직 없음!)
    public void Flash()
    {
        flashTimer = 0.1f; // 0.1초 동안 짧고 강렬하게 번쩍임
    }

    void Update()
    {
        UpdateMaterialFlash();

        if (currentFrames == null || currentFrames.Length == 0) return;

        timer += Time.deltaTime;
        float frameTime = 1f / fps;

        if (timer >= frameTime)
        {
            timer -= frameTime;
            index++;

            if (index >= currentFrames.Length)
            {
                if (loop) index = 0;
                else
                {
                    index = currentFrames.Length - 1;
                    var cb = onComplete; onComplete = null;
                    cb?.Invoke();
                    return;
                }
            }
            sr.sprite = currentFrames[index];
        }
    }

    void UpdateMaterialFlash()
    {
        if (visualSO == null) return;

        sr.GetPropertyBlock(mpb);

        if (flashTimer > 0)
        {
            flashTimer -= Time.deltaTime;
            mpb.SetColor(FlashColorID, visualSO.hitFlashColor);
            mpb.SetFloat(FlashAmountID, 1f);
        }
        else
        {
            mpb.SetFloat(FlashAmountID, 0f);
        }

        sr.SetPropertyBlock(mpb);
    }
}
