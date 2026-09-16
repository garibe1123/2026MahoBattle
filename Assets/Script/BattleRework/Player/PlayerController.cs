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
    [SerializeField] private BattleInputRouter inputRouter;

    [Header("Input Gate")]
    [SerializeField] private bool useRunStateInputGate = true;
    [SerializeField] private bool allowMovementDuringRoomBuild = true;

    private static readonly Vector2[] WalkableFootprintProbes =
    {
        Vector2.right,
        Vector2.left,
        Vector2.up,
        Vector2.down,
        new(0.7071068f, 0.7071068f),
        new(-0.7071068f, 0.7071068f),
        new(0.7071068f, -0.7071068f),
        new(-0.7071068f, -0.7071068f)
    };

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
        if (inputRouter == null)
            inputRouter = BattleInputRouter.ResolveOrCreate(this);

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
        if (inputRouter == null && Application.isPlaying)
            inputRouter = BattleInputRouter.ResolveOrCreate(this);

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
        if (currentState == PlayerState.Dead)
            return;

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

        for (int i = 0; i < WalkableFootprintProbes.Length; i++)
        {
            if (!BattleWalkableField.HasSupport(center + WalkableFootprintProbes[i] * radius))
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
        if (inputRouter == null)
            inputRouter = BattleInputRouter.ResolveOrCreate(this);

        moveInput = movementInputEnabled && inputRouter != null
            ? inputRouter.Move.normalized
            : Vector2.zero;

        bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        if (combatInputEnabled &&
            !pointerOverUi &&
            inputRouter != null &&
            inputRouter.FireHeld &&
            shootingSystem != null)
        {
            Camera mainCamera = Camera.main;
            if (inputRouter.TryGetAimWorldPoint(transform.position, mainCamera, out Vector2 aimPoint))
            {
                anim?.SetFacing(aimPoint - (Vector2)transform.position);
                shootingSystem.TryShoot(aimPoint);
            }
        }

        bool canRoll =
            rollInputEnabled &&
            currentState != PlayerState.Roll &&
            currentStamina >= rollStaminaCost &&
            rollLockTimer <= 0f;

        if (inputRouter != null && inputRouter.RollPressedThisFrame && canRoll)
            StartCoroutine(RollRoutine());

        if (combatInputEnabled &&
            inputRouter != null &&
            inputRouter.ReloadPressedThisFrame &&
            currentState != PlayerState.Roll &&
            shootingSystem != null)
        {
            shootingSystem.ReloadFuncCall();
        }
    }

    private void UpdateState()
    {
        if (currentState == PlayerState.Roll)
            return;

        currentState = movementInputEnabled && moveInput.sqrMagnitude > 0f
            ? PlayerState.Move
            : PlayerState.Idle;
    }

    private void HandleStamina()
    {
        if (currentState == PlayerState.Roll)
            return;

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
        if (!IsAlive || hitInvincible || rollInvincible)
            return;

        float applied = Mathf.Max(0f, damage);
        if (applied <= 0f)
            return;

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
        if (currentState == PlayerState.Dead)
            return;

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
        if (!IsAlive)
            return;

        currentHp = Mathf.Min(currentHp + Mathf.Max(0f, amount), maxHp);
        HpChanged?.Invoke(currentHp, maxHp);
    }

    public void RestoreStamina(float amount)
    {
        if (!IsAlive)
            return;

        currentStamina = Mathf.Min(currentStamina + Mathf.Max(0f, amount), maxStamina);
        StaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    public void AddShield(int count)
    {
        // 구형 호환용 API. 플레이어 실드는 Core/Equipment StatModifier 패스에서 별도 구현 예정.
    }

    public void StartInvincible(float duration)
    {
        if (!IsAlive)
            return;

        StartCoroutine(HitInvincibleRoutine(duration));
    }
}
