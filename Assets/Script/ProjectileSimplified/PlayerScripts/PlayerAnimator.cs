using UnityEngine;
using System.Collections;

[RequireComponent(typeof(SpriteRenderer))]
public class PlayerAnimator : MonoBehaviour
{
    private SpriteRenderer sr;
    private PlayerSpriteSO currentSO;

    // 애니메이션 제어 변수
    private Sprite[] currentFrames;
    private float timer;
    private int frameIndex;
    private float fps;
    private bool isLooping;
    private System.Action onComplete;

    void Awake() => sr = GetComponent<SpriteRenderer>();

    // 외부(Controller)에서 호출할 메인 함수
    public void UpdateAnimation(PlayerState state, Vector2 moveDir, PlayerSpriteSO so)
    {
        currentSO = so;
        this.fps = so.fps;

        // 1. 상태에 따른 애니메이션 결정
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

        // 2. 방향에 따른 좌우 반전 (이소메트릭)
        if (moveDir.x < 0) transform.localScale = new Vector3(-1, 1, 1);
        else if (moveDir.x > 0) transform.localScale = new Vector3(1, 1, 1);
    }

    private void PlayLoop(Sprite[] frames) => PlayInternal(frames, true);
    private void PlayOnce(Sprite[] frames, System.Action callback = null) => PlayInternal(frames, false, callback);

    private void PlayInternal(Sprite[] frames, bool loop, System.Action callback = null)
    {
        if (currentFrames == frames) return; // 이미 재생 중이면 무시

        currentFrames = frames;
        isLooping = loop;
        onComplete = callback;
        frameIndex = 0;
        timer = 0f;

        if (frames != null && frames.Length > 0) sr.sprite = frames[0];
    }

    void Update()
    {
        if (currentFrames == null || currentFrames.Length <= 1) return;

        timer += Time.deltaTime;
        if (timer >= 1f / fps)
        {
            timer = 0f;
            frameIndex++;

            if (frameIndex >= currentFrames.Length)
            {
                if (isLooping) frameIndex = 0;
                else
                {
                    frameIndex = currentFrames.Length - 1;
                    onComplete?.Invoke();
                    onComplete = null;
                    return;
                }
            }
            sr.sprite = currentFrames[frameIndex];
        }
    }

    // 피격 시 깜빡임 연출
    public void StartBlink(float duration) => StartCoroutine(BlinkRoutine(duration));

    private IEnumerator BlinkRoutine(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            sr.color = new Color(1, 1, 1, 0.4f);
            yield return new WaitForSeconds(0.1f);
            sr.color = Color.white;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.2f;
        }
        sr.color = Color.white;
    }
}