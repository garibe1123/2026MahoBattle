using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

public enum PlayerState { Idle, Move, Roll, Dead }

public class PlayerController : MonoBehaviour
{
    [Header("Data & Stats")]
    public PlayerShootingSO shootingSO;
    public PlayerSpriteSO spriteSO;
    public float moveSpeed = 5f;
    public float rollSpeed = 12f;
    public float rollDuration = 0.3f;
    public float maxHp = 100f;
    public float maxStamina = 100f;
    public float staminaRegen = 15f;

    [Header("References")]
    public PlayerShootingSystem shootingSystem;
    private Rigidbody2D rb;
    private PlayerAnimator anim;

    // 내부 상태 변수
    private PlayerState currentState;
    private float currentHp;
    private float currentStamina;
    private Vector2 moveInput;
    private bool isInvincible;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<PlayerAnimator>();

        currentHp = maxHp;
        currentStamina = maxStamina;

        // 시네머신 플레이어 트래킹 자동 연결
        //if (vCam != null) vCam.Follow = this.transform;
    }

    void Update()
    {
        if (currentState == PlayerState.Dead) return;

        HandleInput();
        HandleStamina();

        // 상태 및 애니메이션 업데이트
        UpdateState();
        anim.UpdateAnimation(currentState, moveInput, spriteSO);
    }

    void FixedUpdate()
    {
        if (currentState == PlayerState.Dead) return;

        // 구르기 중에는 Coroutine에서 속도를 직접 제어하므로 평상시에만 이동 적용
        if (currentState != PlayerState.Roll)
        {
            rb.linearVelocity = moveInput * moveSpeed;
        }
    }

    private void HandleInput()
    {
        // 8방향 이소메트릭 이동 입력
        moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;

        // 사격 입력: 마우스 위치를 타겟으로 전달 (Projectile.cs 대응)
        if (Input.GetMouseButton(0))
        {
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0f;
            shootingSystem.TryShoot(mousePos);
        }

        // 소울라이크 구르기 입력
        if (Input.GetKeyDown(KeyCode.Space) && currentState != PlayerState.Roll && currentStamina >= 30f)
        {
            StartCoroutine(RollRoutine());
        }

        if (Input.GetKeyDown(KeyCode.R) && currentState != PlayerState.Roll)
        {
            shootingSystem.ReloadFuncCall();
        }
    }

    private void UpdateState()
    {
        if (currentState == PlayerState.Roll) return;

        if (moveInput.sqrMagnitude > 0) currentState = PlayerState.Move;
        else currentState = PlayerState.Idle;
    }

    private void HandleStamina()
    {
        if (currentState != PlayerState.Roll)
        {
            currentStamina = Mathf.MoveTowards(currentStamina, maxStamina, staminaRegen * Time.deltaTime);
        }
    }

    // 구르기 로직: 무적 판정 및 강제 이동
    IEnumerator RollRoutine()
    {
        currentState = PlayerState.Roll;
        currentStamina -= 30f;
        isInvincible = true;

        // 입력이 없으면 캐릭터가 보는 방향(localScale 기준)으로 구름
        Vector2 rollDir = moveInput == Vector2.zero ? new Vector2(transform.localScale.x, 0) : moveInput;

        float timer = 0f;
        while (timer < rollDuration)
        {
            rb.linearVelocity = rollDir * rollSpeed;
            timer += Time.deltaTime;
            yield return null;
        }

        isInvincible = false;
        currentState = PlayerState.Idle;
    }

    // 체력 및 피격 무적 시스템
    public void TakeDamage(float damage)
    {
        if (isInvincible || currentState == PlayerState.Dead) return;

        currentHp -= damage;
        anim.StartBlink(1.5f); // 애니메이터의 깜빡임 연출 호출
        StartCoroutine(InvincibleRoutine(1.5f));

        if (currentHp <= 0) Die();
    }

    IEnumerator InvincibleRoutine(float duration)
    {
        isInvincible = true;
        yield return new WaitForSeconds(duration);
        isInvincible = false;
    }

    private void Die()
    {
        currentState = PlayerState.Dead;
        rb.linearVelocity = Vector2.zero;
        // 사망 애니메이션이나 게임오버 UI 처리 추가 가능
    }

    // (PlayerController.cs 기존 코드 어딘가에 추가)

    public void Heal(float amount)
    {
        currentHp = Mathf.Min(currentHp + amount, maxHp);
        Debug.Log($"체력 회복! 현재 HP: {currentHp}");
    }

    public void RestoreStamina(float amount)
    {
        currentStamina = Mathf.Min(currentStamina + amount, maxStamina);
    }

    public void AddShield(int count)
    {
        // 쉴드 변수를 하나 추가해서 나중에 TakeDamage()에서 쉴드부터 깎이게 처리하면 됩니다!
    }

    public void StartInvincible(float duration)
    {
        StartCoroutine(InvincibleRoutine(duration)); // 기존에 짜두신 무적 코루틴 재활용!
    }
}