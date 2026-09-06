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

    [Header("Default Body Size")]
    [SerializeField] private bool applyDefaultBodySizing = true;
    [SerializeField, Min(0.1f)] private float defaultCharacterScale = 1.6f;
    [SerializeField, Min(0.05f)] private float defaultHitColliderRadius = 0.46f;

    [Header("Field Movement Guard")]
    [Tooltip("켜면 실제 BattleWalkableField가 존재하는 곳에서만 이동/구르기가 가능합니다. 허공이나 아직 생성되지 않은 통로로 이동할 수 없습니다.")]
    [SerializeField] private bool requireWalkableField = true;
    [Tooltip("Player Collider 반경 중 바닥 위에 남아 있어야 하는 비율입니다. 너무 높이면 좁은 통로에서 과도하게 막힐 수 있습니다.")]
    [SerializeField, Range(0.25f, 0.9f)] private float fieldFootprintRadiusMultiplier = 0.58f;
    [Tooltip("Start Base가 MapBlock이 아니므로 Runtime Marker가 아직 없을 때 자동 등록하는 검사 주기입니다.")]
    [SerializeField, Min(0.05f)] private float startBaseFieldFallbackInterval = 0.20f;

    [Header("Invincibility - v2.2")]
    [SerializeField] private float hitIFrameDuration = 0.5f;
    [SerializeField] private float rollIFrameDuration = 0.15f;

    [Header("Roll Chain")]
    [SerializeField] private float rollStaminaCost = 30f;
    [SerializeField] private int maxConsecutiveRolls = 3;
    [SerializeField] private float rollChainCooldown = 0.8f;

    [Header("References")]
    public PlayerShootingSystem shootingSystem;
    [SerializeField] private BattleRunManager runManager;

    [Header("Input Gate")]
    [SerializeField] private bool useRunStateInputGate = true;
    [SerializeField] private bool allowMovementDuringRoomBuild = true;

    private Rigidbody2D rb;
    private PlayerAnimator anim;
    private CircleCollider2D bodyCircle;

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
    private float nextStartBaseFieldFallbackTime;

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
        ApplyDefaultBodySizing();
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<PlayerAnimator>();
        bodyCircle = GetComponent<CircleCollider2D>();

        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();

        ResetForRun();
        EnsureStartBaseWalkableFieldFallback(true);
    }

    private void ApplyDefaultBodySizing()
    {
        if (!applyDefaultBodySizing)
            return;

        float scale = Mathf.Max(0.1f, defaultCharacterScale);
        transform.localScale = new Vector3(scale, scale, 1f);

        CircleCollider2D circle = GetComponent<CircleCollider2D>();
        if (circle != null)
            circle.radius = Mathf.Max(0.05f, defaultHitColliderRadius);
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
        if (currentState == PlayerState.Dead)
            return;

        if (!movementInputEnabled)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (currentState == PlayerState.Roll)
            return;

        Vector2 desiredVelocity = moveInput * moveSpeed;
        rb.linearVelocity = ResolveFieldSupportedVelocity(desiredVelocity);
    }

    /// <summary>
    /// 대각선 이동이 허공에 걸렸을 때 X/Y 축을 각각 검사해 벽/필드 끝을 따라 자연스럽게 미끄러질 수 있게 합니다.
    /// 어떤 방향에도 실제 Field가 없으면 속도를 0으로 만듭니다.
    /// </summary>
    private Vector2 ResolveFieldSupportedVelocity(Vector2 desiredVelocity)
    {
        if (!requireWalkableField || desiredVelocity.sqrMagnitude <= 0.0001f)
            return desiredVelocity;

        EnsureStartBaseWalkableFieldFallback(false);

        Vector2 current = rb.position;
        Vector2 fullTarget = current + desiredVelocity * Time.fixedDeltaTime;
        if (CanOccupyWalkableField(fullTarget))
            return desiredVelocity;

        Vector2 resolved = Vector2.zero;

        if (Mathf.Abs(desiredVelocity.x) > 0.001f)
        {
            Vector2 xTarget = current + Vector2.right * desiredVelocity.x * Time.fixedDeltaTime;
            if (CanOccupyWalkableField(xTarget))
                resolved.x = desiredVelocity.x;
        }

        if (Mathf.Abs(desiredVelocity.y) > 0.001f)
        {
            Vector2 yTarget = current + Vector2.up * desiredVelocity.y * Time.fixedDeltaTime;
            if (CanOccupyWalkableField(yTarget))
                resolved.y = desiredVelocity.y;
        }

        return resolved;
    }

    private bool CanOccupyWalkableField(Vector2 center)
    {
        if (!requireWalkableField)
            return true;

        if (!BattleWalkableField.HasSupport(center))
            return false;

        float radius = GetWorldBodyRadius() * Mathf.Clamp(fieldFootprintRadiusMultiplier, 0.25f, 0.9f);
        if (radius <= 0.02f)
            return true;

        // 몸 전체가 필드 끝을 크게 넘어가지 않도록 8방향 발자국을 검사합니다.
        Vector2[] probes =
        {
            Vector2.right,
            Vector2.left,
            Vector2.up,
            Vector2.down,
            new Vector2(0.7071068f, 0.7071068f),
            new Vector2(-0.7071068f, 0.7071068f),
            new Vector2(0.7071068f, -0.7071068f),
            new Vector2(-0.7071068f, -0.7071068f)
        };

        for (int i = 0; i < probes.Length; i++)
        {
            if (!BattleWalkableField.HasSupport(center + probes[i] * radius))
                return false;
        }

        return true;
    }

    private float GetWorldBodyRadius()
    {
        if (bodyCircle == null)
            bodyCircle = GetComponent<CircleCollider2D>();

        if (bodyCircle == null)
            return 0.22f;

        Vector3 scale = transform.lossyScale;
        float scaleMax = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        return Mathf.Max(0.05f, bodyCircle.radius * scaleMax);
    }

    /// <summary>
    /// Start Base는 MapBlock이 아니므로 가장 큰 바닥 SpriteRenderer를 WalkableField로 한 번 등록합니다.
    /// 이 fallback은 바닥을 새로 만드는 것이 아니라 이미 존재하는 Start Base Renderer에 판정용 Trigger만 붙입니다.
    /// </summary>
    private void EnsureStartBaseWalkableFieldFallback(bool force)
    {
        if (!requireWalkableField)
            return;

        if (!force && Time.unscaledTime < nextStartBaseFieldFallbackTime)
            return;

        nextStartBaseFieldFallbackTime = Time.unscaledTime + Mathf.Max(0.05f, startBaseFieldFallbackInterval);

        if (BattleWalkableField.HasSupport(transform.position))
            return;

        RoomBaseTemplate baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (baseTemplate == null || baseTemplate.ActiveBase == null)
            return;

        SpriteRenderer[] renderers = baseTemplate.ActiveBase.GetComponentsInChildren<SpriteRenderer>(true);
        SpriteRenderer largest = null;
        float largestArea = 0f;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sprite == null)
                continue;

            Bounds bounds = renderer.bounds;
            float area = Mathf.Abs(bounds.size.x * bounds.size.y);
            if (area > largestArea)
            {
                largestArea = area;
                largest = renderer;
            }
        }

        if (largest != null)
            BattleWalkableField.Ensure(largest);
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
                anim?.SetFacing((Vector2)(mousePos - transform.position));
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
        currentStamina = Mathf.MoveTowards(currentStamina, maxStamina, staminaRegen * Time.deltaTime);

        if (!Mathf.Approximately(before, currentStamina))
            StaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    private IEnumerator RollRoutine()
    {
        currentState = PlayerState.Roll;
        currentStamina -= rollStaminaCost;
        consecutiveRolls++;
        StaminaChanged?.Invoke(currentStamina, maxStamina);

        Vector2 fallbackFacing = anim != null && anim.Facing.sqrMagnitude > 0.001f
            ? anim.Facing
            : Vector2.right;
        Vector2 rollDir = moveInput == Vector2.zero ? fallbackFacing : moveInput;

        if (rollDir.sqrMagnitude <= 0.001f)
            rollDir = Vector2.right;

        anim?.SetFacing(rollDir);

        float timer = 0f;
        rollInvincible = true;

        while (timer < rollDuration && currentState == PlayerState.Roll && movementInputEnabled)
        {
            Vector2 next = rb.position + rollDir * rollSpeed * Time.fixedDeltaTime;
            if (requireWalkableField && !CanOccupyWalkableField(next))
            {
                rb.linearVelocity = Vector2.zero;
                break;
            }

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

    private void HandleRunStateChanged(BattleRunState nextState) => ApplyInputGate(nextState);

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
            case BattleRunState.None:
                SetInputPermissions(true, false, true);
                break;

            case BattleRunState.EnteringNode:
            case BattleRunState.BuildingRoom:
                if (allowMovementDuringRoomBuild)
                    SetInputPermissions(true, false, true);
                else
                    SetInputPermissions(false, false, false);
                break;

            case BattleRunState.Combat:
                SetInputPermissions(true, true, true);
                break;

            case BattleRunState.ExitingRoom:
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

    public void ReceiveDamage(DamageContext context, float finalDamage) => TakeDamage(finalDamage);

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
        if (bodyCircle == null)
            bodyCircle = GetComponent<CircleCollider2D>();

        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        EnsureStartBaseWalkableFieldFallback(true);
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
