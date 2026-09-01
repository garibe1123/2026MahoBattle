using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class ProjectileAnimator : MonoBehaviour
{
    private SpriteRenderer sr;
    private Sprite[] frames;
    private float fps;
    private bool loop;
    private int index;
    private float timer;
    private System.Action onComplete;
    private bool completeSingleFrameNextUpdate;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    public void PlayLoop(Sprite[] sprites, float fps)
        => PlayInternal(sprites, fps, true, null);

    public void PlayOnce(Sprite[] sprites, float fps, System.Action onComplete)
        => PlayInternal(sprites, fps, false, onComplete);

    void PlayInternal(Sprite[] sprites, float fps, bool loop, System.Action onComplete)
    {
        frames = sprites;
        this.fps = Mathf.Max(0.01f, fps);
        this.loop = loop;
        this.onComplete = onComplete;
        index = 0;
        timer = 0f;
        completeSingleFrameNextUpdate = false;

        if (frames == null || frames.Length == 0)
        {
            if (!loop)
            {
                var cb = this.onComplete;
                this.onComplete = null;
                cb?.Invoke();
            }
            return;
        }

        sr.sprite = frames[0];

        // 1프레임짜리 Hit/End 애니메이션도 최소 한 Update 동안 화면에 표시한 뒤
        // completion callback을 반드시 실행해서 Projectile이 Pool로 돌아가게 합니다.
        if (!loop && frames.Length == 1)
            completeSingleFrameNextUpdate = true;
    }

    void Update()
    {
        if (completeSingleFrameNextUpdate)
        {
            completeSingleFrameNextUpdate = false;
            var cb = onComplete;
            onComplete = null;
            cb?.Invoke();
            return;
        }

        if (frames == null || frames.Length <= 1)
            return;

        timer += Time.deltaTime;
        float frameTime = 1f / fps;
        if (timer < frameTime) return;
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
                var cb = onComplete;
                onComplete = null;
                cb?.Invoke();
                return;
            }
        }

        sr.sprite = frames[index];
    }
}
