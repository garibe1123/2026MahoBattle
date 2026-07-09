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

    void Awake(){ sr = GetComponent<SpriteRenderer>(); }

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

        if (frames != null && frames.Length > 0)
            sr.sprite = frames[0];
    }

    void Update()
    {
        if (frames == null || frames.Length <= 1) return;

        timer += Time.deltaTime;
        float frameTime = 1f / fps;
        if (timer < frameTime) return;
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
