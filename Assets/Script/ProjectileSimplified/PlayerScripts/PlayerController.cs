using System;
using System.Collections;
using UnityEngine;

public enum PlayerState { Idle, Move, Roll, Dead }

public class PlayerController : MonoBehaviour, IDamageable
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
    [SerializeField] private float baseDefense = 0f;

    [Header("Invincibility - v2.2")]
    [SerializeField] private float hitIFrameDuration = 0.5f;
    [SerializeField] private float rollIFrameDuration = 0.15f;

    [Header("Roll Chain")]
    [SerializeField] private float rollStaminaCost = 30f;
    [SerializeField] private int maxConsecutiveRolls = 3;
    [Tooltip("3연속 구르기 후 강제되는 잠금 시간. 정확한 수치는 플레이테스트 조정 대상.")]
    [SerializeField] private float rollChainCooldown = 0.8f;

    [Header("References")]
    public PlayerShootingSystem shootingSystem;
    [SerializeField] private BattleRunManager battleRunManager;

    private Rigidbody2D rb;
    private PlayerAnimator anim;

    private PlayerState currentState;
    private float currentHp;
    private float currentStamina;
    private Vector2 moveInput;

    private bool hitInvincible;
    private bool rollInvincible;
    private float rollLockTimer;
    private int consecutiveRolls;

    public bool IsAlive => currentState != PlayerState.Dead && currentHp > 0f;
    public float Defense => Mathf.Max(0f, baseDefense);
    public float CurrentHp => currentHp;
    public float CurrentStamina => currentStamina;
    public PlayerState CurrentState => currentState;

    public event Action Died;
    public event Action<float, float> HpChanged;
    public event Action<float, float> StaminaChanged;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<PlayerAnimator>();

        currentHp = maxHp;
        currentStamina = maxStamina;
    }

    private void Update()
    {
        if (currentState == PlayerState.Dead) return;

        if (rollLockTimer > 0f)
            rollLockTimer = Mathf.Max(0f, rollLockTimer - Time.deltaTime);

        HandleInput();
        HandleStamina();
        UpdateState();

        if (anim != null)
            anim.UpdateAnimation(currentState, moveInput, spriteSO);
    }

    private void FixedUpdate()
    {
        if (currentState == PlayerState.Dead) return;

        if (currentState != PlayerState.Roll)
            rb.linearVelocity = moveInput * moveSpeed;
    }

    private void HandleInput()
    {
        moveInput = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")).normalized;

        if (Input.GetMouseButton(0) && shootingSystem != null)
        {
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0f;
            shootingSystem.TryShoot(mousePos);
        }

        bool canRoll =
            currentState != PlayerState.Roll &&
            currentStamina >= rollStaminaCost &&
            rollLockTimer <= 0f;

        if (Input.GetKeyDown(KeyCode.Space) && canRoll)
            StartCoroutine(RollRoutine());

        if (Input.GetKeyDown(KeyCode.R) && currentState != PlayerState.Roll && shootingSystem != null)
            shootingSystem.ReloadFuncCall();
    }

    private void UpdateState()
    {
        if (currentState == PlayerState.Roll) return;
        currentState = moveInput.sqrMagnitude > 0f ? PlayerState.Move : PlayerState.Idle;
    }

    private void HandleStamina()
    {
        if (currentState == PlayerState.Roll) return;

        float before = currentStamina;
        currentStamina = Mathf.MoveTowards(
            currentStamina,
            maxStamina,
            staminaRegen * Time.deltaTime);

        if (!Mathf.Approximately(before, currentStamina))
            StaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    private IEnumerator RollRoutine()
    {
        currentState = PlayerState.Roll;
        currentStamina -= rollStaminaCost;
        consecutiveRolls++;
        StaminaChanged?.Invoke(currentStamina, maxStamina);

        Vector2 rollDir = moveInput == Vector2.zero
            ? new Vector2(Mathf.Sign(transform.localScale.x), 0f)
            : moveInput;

        float timer = 0f;
        rollInvincible = true;

        while (timer < rollDuration)
        {
            rb.linearVelocity = rollDir * rollSpeed;
            timer += Time.deltaTime;

            if (timer >= rollIFrameDuration)
                rollInvincible = false;

            yield return null;
        }

        rollInvincible = false;
        rb.linearVelocity = Vector2.zero;
        currentState = PlayerState.Idle;

        if (consecutiveRolls >= Mathf.Max(1, maxConsecutiveRolls))
        {
            consecutiveRolls = 0;
            rollLockTimer = Mathf.Max(0f, rollChainCooldown);
        }
    }

    public void ReceiveDamage(DamageContext context, float finalDamage)
    {
        TakeDamage(finalDamage);
    }

    public void TakeDamage(float damage)
    {
        if (!IsAlive || hitInvincible || rollInvincible) return;

        float applied = Mathf.Max(0f, damage);
        currentHp = Mathf.Max(0f, currentHp - applied);
        HpChanged?.Invoke(currentHp, maxHp);

        if (anim != null)
            anim.StartBlink(hitIFrameDuration);

        if (currentHp <= 0f)
        {
            Die();
            return;
        }

        StartCoroutine(HitInvincibleRoutine(hitIFrameDuration));
    }

    private IEnumerator HitInvincibleRoutine(float duration)
    {
        hitInvincible = true;
        yield return new WaitForSeconds(Mathf.Max(0f, duration));
        hitInvincible = false;
    }

    private void Die()
    {
        if (currentState == PlayerState.Dead) return;

        currentState = PlayerState.Dead;
        hitInvincible = false;
        rollInvincible = false;
        rb.linearVelocity = Vector2.zero;

        Died?.Invoke();
        battleRunManager?.NotifyPlayerDeath();
    }

    public void Heal(float amount)
    {
        if (!IsAlive) return;

        currentHp = Mathf.Min(currentHp + Mathf.Max(0f, amount), maxHp);
        HpChanged?.Invoke(currentHp, maxHp);
    }

    public void RestoreStamina(float amount)
    {
        currentStamina = Mathf.Min(currentStamina + Mathf.Max(0f, amount), maxStamina);
        StaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    public void AddShield(int count)
    {
        // 구형 호환용 API. 플레이어 실드는 Core/Equipment StatModifier 패스에서 별도 구현 예정.
    }

    public void StartInvincible(float duration)
    {
        if (!IsAlive) return;
        StartCoroutine(HitInvincibleRoutine(duration));
    }
}
