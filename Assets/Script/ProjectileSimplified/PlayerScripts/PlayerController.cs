using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public enum PlayerState { Idle, Move, Roll, Dead }

[RequireComponent(typeof(Rigidbody2D))]
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
    [SerializeField] private BattleRunManager runManager;

    [Header("Input Gate")]
    [Tooltip("BattleRunManager가 있으면 Run State에 따라 이동/공격 입력을 잠급니다. 구형 테스트 씬에서는 RunManager가 없으면 항상 입력을 허용합니다.")]
    [SerializeField] private bool useRunStateInputGate = true;

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

    private bool movementInputEnabled = true;
    private bool combatInputEnabled = true;
    private bool rollInputEnabled = true;

    public bool IsAlive => currentState != PlayerState.Dead && currentHp > 0f;
    public float Defense => Mathf.Max(0f, baseDefense);
    public float CurrentHp => currentHp;
    public float CurrentStamina => currentStamina;
    public PlayerState CurrentState => currentState;
    public bool MovementInputEnabled => movementInputEnabled;
    public bool CombatInputEnabled => combatInputEnabled;

    public event Action Died;
    public event Action<float, float> HpChanged;
    public event Action<float, float> StaminaChanged;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<PlayerAnimator>();

        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();

        ResetForRun();
    }

    private void OnEnable()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();

        if (runManager != null)
            runManager.StateChanged += HandleRunStateChanged;

        RefreshInputGate();
    }

    private void OnDisable()
    {
        if (runManager != null)
            runManager.StateChanged -= HandleRunStateChanged;
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

        if (!movementInputEnabled)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (currentState != PlayerState.Roll)
            rb.linearVelocity = moveInput * moveSpeed;
    }

    private void HandleInput()
    {
        if (movementInputEnabled)
        {
            moveInput = new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical")).normalized;
        }
        else
        {
            moveInput = Vector2.zero;
        }

        bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        if (combatInputEnabled && !pointerOverUi && Input.GetMouseButton(0) && shootingSystem != null)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                Vector3 mousePos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
                mousePos.z = 0f;
                shootingSystem.TryShoot(mousePos);
            }
        }

        bool canRoll =
            rollInputEnabled &&
            currentState != PlayerState.Roll &&
            currentStamina >= rollStaminaCost &&
            rollLockTimer <= 0f;

        if (Input.GetKeyDown(KeyCode.Space) && canRoll)
            StartCoroutine(RollRoutine());

        if (combatInputEnabled && Input.GetKeyDown(KeyCode.R) && currentState != PlayerState.Roll && shootingSystem != null)
            shootingSystem.ReloadFuncCall();
    }

    private void UpdateState()
    {
        if (currentState == PlayerState.Roll) return;
        currentState = movementInputEnabled && moveInput.sqrMagnitude > 0f
            ? PlayerState.Move
            : PlayerState.Idle;
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

        if (rollDir.sqrMagnitude <= 0.001f)
            rollDir = Vector2.right;

        float timer = 0f;
        rollInvincible = true;

        while (timer < rollDuration && currentState == PlayerState.Roll && movementInputEnabled)
        {
            rb.linearVelocity = rollDir * rollSpeed;
            timer += Time.deltaTime;

            if (timer >= rollIFrameDuration)
                rollInvincible = false;

            yield return null;
        }

        rollInvincible = false;
        rb.linearVelocity = Vector2.zero;

        if (currentState != PlayerState.Dead)
            currentState = PlayerState.Idle;

        if (consecutiveRolls >= Mathf.Max(1, maxConsecutiveRolls))
        {
            consecutiveRolls = 0;
            rollLockTimer = Mathf.Max(0f, rollChainCooldown);
        }
    }

    private void HandleRunStateChanged(BattleRunState nextState)
    {
        ApplyInputGate(nextState);
    }

    private void RefreshInputGate()
    {
        if (!useRunStateInputGate || runManager == null)
        {
            SetInputPermissions(true, true, true);
            return;
        }

        ApplyInputGate(runManager.State);
    }

    private void ApplyInputGate(BattleRunState runState)
    {
        if (!useRunStateInputGate || runManager == null)
        {
            SetInputPermissions(true, true, true);
            return;
        }

        switch (runState)
        {
            case BattleRunState.Combat:
                SetInputPermissions(true, true, true);
                break;

            case BattleRunState.ExitingRoom:
                // 보상 선택 후 Highlight Block까지 직접 걸어갈 수는 있지만 공격은 잠급니다.
                SetInputPermissions(true, false, true);
                break;

            default:
                SetInputPermissions(false, false, false);
                break;
        }
    }

    public void SetInputPermissions(bool allowMovement, bool allowCombat, bool allowRoll)
    {
        movementInputEnabled = allowMovement;
        combatInputEnabled = allowCombat;
        rollInputEnabled = allowRoll;

        if (movementInputEnabled)
            return;

        moveInput = Vector2.zero;
        rollInvincible = false;

        if (currentState == PlayerState.Roll)
            currentState = PlayerState.Idle;

        if (rb != null)
            rb.linearVelocity = Vector2.zero;
    }

    public void ReceiveDamage(DamageContext context, float finalDamage)
    {
        TakeDamage(finalDamage);
    }

    public void TakeDamage(float damage)
    {
        if (!IsAlive || hitInvincible || rollInvincible) return;

        float applied = Mathf.Max(0f, damage);
        if (applied <= 0f) return;

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
        moveInput = Vector2.zero;
        rb.linearVelocity = Vector2.zero;

        StopAllCoroutines();
        Died?.Invoke();
    }

    /// <summary>
    /// 새 런/테스트 재시작 시 플레이어 런타임 상태를 완전히 초기화합니다.
    /// 장기 성장에서 계산된 maxHp/maxStamina 값 자체는 유지하고 현재값만 채웁니다.
    /// </summary>
    public void ResetForRun()
    {
        StopAllCoroutines();

        currentState = PlayerState.Idle;
        currentHp = Mathf.Max(1f, maxHp);
        currentStamina = Mathf.Max(0f, maxStamina);
        moveInput = Vector2.zero;
        hitInvincible = false;
        rollInvincible = false;
        rollLockTimer = 0f;
        consecutiveRolls = 0;

        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        RefreshInputGate();
        HpChanged?.Invoke(currentHp, maxHp);
        StaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    public void Heal(float amount)
    {
        if (!IsAlive) return;

        currentHp = Mathf.Min(currentHp + Mathf.Max(0f, amount), maxHp);
        HpChanged?.Invoke(currentHp, maxHp);
    }

    public void RestoreStamina(float amount)
    {
        if (!IsAlive) return;

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
