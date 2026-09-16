using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum BattleInputDevice
{
    KeyboardMouse,
    Gamepad
}

/// <summary>
/// BattleScene 입력의 단일 소유자입니다.
///
/// - Combat / UI / Debug action map의 활성 상태를 BattleRunState와 modal stack으로 결정합니다.
/// - Player, Equipment, Pause, Reward/UI는 Legacy Input API를 직접 읽지 않고 이 Router의 값/이벤트를 사용합니다.
/// - Pointer / Wheel / UI submit-cancel / 개발용 토글도 이 Router에서 한 번만 읽어 노출합니다.
/// - Inspector에 InputActionAsset이 연결되어 있으면 런타임 복제본을 사용합니다.
/// - 씬 배선이 아직 없는 경우 동일 binding의 런타임 fallback asset을 생성합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-9500)]
public sealed class BattleInputRouter : MonoBehaviour
{
    [Header("Authoring")]
    [SerializeField] private InputActionAsset sourceActions;
    [SerializeField] private BattleRunManager runManager;

    [Header("Aim")]
    [SerializeField, Range(0.05f, 0.95f)] private float gamepadAimDeadZone = 0.20f;
    [SerializeField, Min(0.5f)] private float gamepadAimDistance = 8f;

    private readonly Stack<IInputModal> modals = new();
    private readonly InputAction[] slotActions = new InputAction[BattleEquipmentSystem.MaxSlotCount];

    private InputActionAsset runtimeActions;
    private InputActionMap combatMap;
    private InputActionMap uiMap;
    private InputActionMap debugMap;

    private InputAction moveAction;
    private InputAction aimPointAction;
    private InputAction aimStickAction;
    private InputAction fireAction;
    private InputAction rollAction;
    private InputAction reloadAction;
    private InputAction openTabAction;
    private InputAction pauseAction;
    private InputAction navigateAction;
    private InputAction submitAction;
    private InputAction uiCancelAction;
    private InputAction closeTabAction;

    private Vector2 move;
    private Vector2 navigate;
    private Vector2 lastAimDirection = Vector2.right;
    private bool runStateSubscribed;

    public static BattleInputRouter Instance { get; private set; }

    public Vector2 Move => move;
    public Vector2 Navigate => navigate;
    public Vector2 LastAimDirection => lastAimDirection;
    public BattleInputDevice LastDevice { get; private set; } = BattleInputDevice.KeyboardMouse;

    public bool CombatEnabled => combatMap != null && combatMap.enabled;
    public bool UiEnabled => uiMap != null && uiMap.enabled;
    public bool HasModal => modals.Count > 0;

    public bool FireHeld => CombatEnabled && fireAction != null && fireAction.IsPressed();
    public bool RollPressedThisFrame => CombatEnabled && rollAction != null && rollAction.WasPressedThisFrame();
    public bool ReloadPressedThisFrame => CombatEnabled && reloadAction != null && reloadAction.WasPressedThisFrame();
    public bool TabHeld => CombatEnabled && openTabAction != null && openTabAction.IsPressed();
    public bool SubmitPressedThisFrame => UiEnabled && submitAction != null && submitAction.WasPressedThisFrame();
    public bool CancelPressedThisFrame => UiEnabled && uiCancelAction != null && uiCancelAction.WasPressedThisFrame();

    public bool PointerPresent => Pointer.current != null;
    public Vector2 PointerPosition => Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
    public Vector2 ScrollDelta => Mouse.current != null ? Mouse.current.scroll.ReadValue() : Vector2.zero;

    /// <summary>Reward PACK 전용 보조 액션. Gamepad Y / North.</summary>
    public bool DiscardPressedThisFrame => UiEnabled && Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame;
    /// <summary>Reward PACK 완료 보조 액션. Gamepad Start.</summary>
    public bool DonePressedThisFrame => UiEnabled && Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    public bool DebugTogglePressedThisFrame => debugMap != null && debugMap.enabled && Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame;
    public bool DummyTogglePressedThisFrame => debugMap != null && debugMap.enabled && Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame;
    public bool SynergyTogglePressedThisFrame => debugMap != null && debugMap.enabled && Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame;
#else
    public bool DebugTogglePressedThisFrame => false;
    public bool DummyTogglePressedThisFrame => false;
    public bool SynergyTogglePressedThisFrame => false;
#endif

    public event Action FirePressed;
    public event Action RollPressed;
    public event Action ReloadPressed;
    public event Action TabOpened;
    public event Action TabClosed;
    public event Action<int> SlotPressed;
    public event Action PauseRequested;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    public static BattleInputRouter ResolveOrCreate(Component context = null)
    {
        if (Instance != null)
            return Instance;

        BattleInputRouter existing = FindFirstObjectByType<BattleInputRouter>();
        if (existing != null)
            return existing;

        GameObject host = null;
        BattleSceneManager sceneManager = FindFirstObjectByType<BattleSceneManager>();
        if (sceneManager != null)
            host = sceneManager.gameObject;

        if (host == null && context != null && context.gameObject.scene.IsValid())
        {
            BattleRunManager run = FindFirstObjectByType<BattleRunManager>();
            if (run != null && run.gameObject.scene == context.gameObject.scene)
                host = run.gameObject;
        }

        if (host == null)
            host = new GameObject("[BattleInputRouter]");

        return host.AddComponent<BattleInputRouter>();
    }

    public void Configure(InputActionAsset actions, BattleRunManager run)
    {
        if (run != null && runManager != run)
        {
            UnsubscribeRunState();
            runManager = run;
            SubscribeRunState();
        }

        if (actions != null && sourceActions != actions)
        {
            sourceActions = actions;
            RebuildRuntimeActions();
        }
        else
        {
            ApplyMaps();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            Destroy(this);
            return;
        }

        Instance = this;
        ResolveRunManager();
        RebuildRuntimeActions();
    }

    private void OnEnable()
    {
        if (Instance == null)
            Instance = this;

        ResolveRunManager();
        SubscribeRunState();
        InputSystem.onActionChange -= TrackDevice;
        InputSystem.onActionChange += TrackDevice;
        ApplyMaps();
    }

    private void OnDisable()
    {
        InputSystem.onActionChange -= TrackDevice;
        UnsubscribeRunState();
        DisableAllMaps();
        move = Vector2.zero;
        navigate = Vector2.zero;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (runtimeActions != null)
            Destroy(runtimeActions);
    }

    private void Update()
    {
        move = CombatEnabled && moveAction != null
            ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f)
            : Vector2.zero;

        navigate = UiEnabled && navigateAction != null
            ? Vector2.ClampMagnitude(navigateAction.ReadValue<Vector2>(), 1f)
            : Vector2.zero;

        if (CombatEnabled)
        {
            if (fireAction != null && fireAction.WasPressedThisFrame())
                FirePressed?.Invoke();
            if (rollAction != null && rollAction.WasPressedThisFrame())
                RollPressed?.Invoke();
            if (reloadAction != null && reloadAction.WasPressedThisFrame())
                ReloadPressed?.Invoke();

            for (int i = 0; i < slotActions.Length; i++)
            {
                if (slotActions[i] != null && slotActions[i].WasPressedThisFrame())
                {
                    SlotPressed?.Invoke(i);
                    break;
                }
            }

            if (openTabAction != null)
            {
                if (openTabAction.WasPressedThisFrame())
                    TabOpened?.Invoke();
                if (openTabAction.WasReleasedThisFrame())
                    TabClosed?.Invoke();
            }

            if (pauseAction != null && pauseAction.WasPressedThisFrame())
                HandleCancel();
        }

        if (UiEnabled && uiCancelAction != null && uiCancelAction.WasPressedThisFrame())
            HandleCancel();

        if (UiEnabled && closeTabAction != null && closeTabAction.WasPressedThisFrame())
            TabClosed?.Invoke();
    }

    public bool TryGetAimWorldPoint(Vector2 playerWorldPosition, Camera camera, out Vector2 worldPoint)
    {
        if (LastDevice == BattleInputDevice.Gamepad)
        {
            Vector2 stick = aimStickAction != null && CombatEnabled
                ? aimStickAction.ReadValue<Vector2>()
                : Vector2.zero;

            float deadZone = Mathf.Clamp(gamepadAimDeadZone, 0.05f, 0.95f);
            if (stick.sqrMagnitude >= deadZone * deadZone)
                lastAimDirection = stick.normalized;

            if (lastAimDirection.sqrMagnitude <= 0.001f)
                lastAimDirection = Vector2.right;

            worldPoint = playerWorldPosition + lastAimDirection * Mathf.Max(0.5f, gamepadAimDistance);
            return true;
        }

        if (camera == null || aimPointAction == null || !CombatEnabled)
        {
            worldPoint = playerWorldPosition + lastAimDirection;
            return false;
        }

        Vector2 screenPoint = aimPointAction.ReadValue<Vector2>();
        Vector3 projected = camera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, 0f));
        worldPoint = new Vector2(projected.x, projected.y);

        Vector2 direction = worldPoint - playerWorldPosition;
        if (direction.sqrMagnitude > 0.0001f)
            lastAimDirection = direction.normalized;

        return true;
    }

    public void PushModal(IInputModal modal)
    {
        if (modal == null)
            return;
        if (modals.Count > 0 && ReferenceEquals(modals.Peek(), modal))
            return;
        if (modals.Contains(modal))
            return;

        modals.Push(modal);
        ApplyMaps();
    }

    public void PopModal(IInputModal modal)
    {
        if (modal == null || modals.Count == 0 || !ReferenceEquals(modals.Peek(), modal))
            return;

        modals.Pop();
        ApplyMaps();
    }

    public void ClearModals()
    {
        modals.Clear();
        ApplyMaps();
    }

    private void HandleCancel()
    {
        if (modals.Count > 0)
        {
            modals.Peek().RequestClose();
            return;
        }

        PauseRequested?.Invoke();
    }

    private void ResolveRunManager()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
    }

    private void SubscribeRunState()
    {
        if (runStateSubscribed || runManager == null)
            return;

        runManager.StateChanged += HandleRunStateChanged;
        runStateSubscribed = true;
    }

    private void UnsubscribeRunState()
    {
        if (!runStateSubscribed)
            return;

        if (runManager != null)
            runManager.StateChanged -= HandleRunStateChanged;
        runStateSubscribed = false;
    }

    private void HandleRunStateChanged(BattleRunState _)
    {
        ApplyMaps();
    }

    private void ApplyMaps()
    {
        if (!isActiveAndEnabled || combatMap == null || uiMap == null)
            return;

        combatMap.Disable();
        uiMap.Disable();

        if (modals.Count > 0)
        {
            uiMap.Enable();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            debugMap?.Enable();
#endif
            return;
        }

        BattleRunState state = runManager != null ? runManager.State : BattleRunState.Combat;
        switch (state)
        {
            case BattleRunState.None:
            case BattleRunState.EnteringNode:
            case BattleRunState.BuildingRoom:
            case BattleRunState.Combat:
            case BattleRunState.ExitingRoom:
                combatMap.Enable();
                break;

            case BattleRunState.Reward:
            case BattleRunState.SelectingNode:
            case BattleRunState.NonCombat:
                uiMap.Enable();
                break;

            case BattleRunState.Ended:
                break;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        debugMap?.Enable();
#else
        debugMap?.Disable();
#endif
    }

    private void DisableAllMaps()
    {
        combatMap?.Disable();
        uiMap?.Disable();
        debugMap?.Disable();
    }

    private void RebuildRuntimeActions()
    {
        DisableAllMaps();

        if (runtimeActions != null)
            Destroy(runtimeActions);

        runtimeActions = sourceActions != null
            ? Instantiate(sourceActions)
            : BuildFallbackAsset();

        combatMap = runtimeActions != null ? runtimeActions.FindActionMap("Combat", false) : null;
        uiMap = runtimeActions != null ? runtimeActions.FindActionMap("UI", false) : null;
        debugMap = runtimeActions != null ? runtimeActions.FindActionMap("Debug", false) : null;

        if (combatMap == null || uiMap == null)
        {
            if (runtimeActions != null)
                Destroy(runtimeActions);
            runtimeActions = BuildFallbackAsset();
            combatMap = runtimeActions.FindActionMap("Combat", true);
            uiMap = runtimeActions.FindActionMap("UI", true);
            debugMap = runtimeActions.FindActionMap("Debug", false);
        }

        moveAction = combatMap.FindAction("Move", true);
        aimPointAction = combatMap.FindAction("AimPoint", true);
        aimStickAction = combatMap.FindAction("AimStick", true);
        fireAction = combatMap.FindAction("Fire", true);
        rollAction = combatMap.FindAction("Roll", true);
        reloadAction = combatMap.FindAction("Reload", true);
        openTabAction = combatMap.FindAction("OpenTab", true);
        pauseAction = combatMap.FindAction("Pause", true);

        for (int i = 0; i < slotActions.Length; i++)
            slotActions[i] = combatMap.FindAction($"Slot{i + 1}", true);

        navigateAction = uiMap.FindAction("Navigate", true);
        submitAction = uiMap.FindAction("Submit", true);
        uiCancelAction = uiMap.FindAction("Cancel", true);
        closeTabAction = uiMap.FindAction("CloseTab", true);

        ApplyMaps();
    }

    private static InputActionAsset BuildFallbackAsset()
    {
        InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
        asset.name = "BattleInputRouter_RuntimeActions";

        InputActionMap combat = asset.AddActionMap("Combat");

        InputAction moveAction = combat.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
        moveAction.AddBinding("<Gamepad>/leftStick");

        combat.AddAction("AimPoint", InputActionType.Value, "<Pointer>/position", expectedControlLayout: "Vector2");
        combat.AddAction("AimStick", InputActionType.Value, "<Gamepad>/rightStick", expectedControlLayout: "Vector2");

        InputAction fire = combat.AddAction("Fire", InputActionType.Button);
        fire.AddBinding("<Mouse>/leftButton");
        fire.AddBinding("<Gamepad>/rightTrigger");

        InputAction roll = combat.AddAction("Roll", InputActionType.Button);
        roll.AddBinding("<Keyboard>/space");
        roll.AddBinding("<Gamepad>/buttonSouth");
        roll.AddBinding("<Gamepad>/leftTrigger");

        InputAction reload = combat.AddAction("Reload", InputActionType.Button);
        reload.AddBinding("<Keyboard>/r");
        reload.AddBinding("<Gamepad>/buttonWest");

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            InputAction slot = combat.AddAction($"Slot{i + 1}", InputActionType.Button);
            slot.AddBinding($"<Keyboard>/{i + 1}");
        }

        combat.AddAction("SlotPrev", InputActionType.Button);
        combat.AddAction("SlotNext", InputActionType.Button);

        InputAction openTab = combat.AddAction("OpenTab", InputActionType.Button);
        openTab.AddBinding("<Keyboard>/tab");
        openTab.AddBinding("<Gamepad>/leftShoulder");

        InputAction pause = combat.AddAction("Pause", InputActionType.Button);
        pause.AddBinding("<Keyboard>/escape");
        pause.AddBinding("<Gamepad>/start");

        InputActionMap ui = asset.AddActionMap("UI");

        InputAction navigate = ui.AddAction("Navigate", InputActionType.Value, expectedControlLayout: "Vector2");
        navigate.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
        navigate.AddBinding("<Gamepad>/leftStick");
        navigate.AddBinding("<Gamepad>/dpad");

        InputAction submit = ui.AddAction("Submit", InputActionType.Button);
        submit.AddBinding("<Keyboard>/enter");
        submit.AddBinding("<Keyboard>/space");
        submit.AddBinding("<Gamepad>/buttonSouth");

        InputAction cancel = ui.AddAction("Cancel", InputActionType.Button);
        cancel.AddBinding("<Keyboard>/escape");
        cancel.AddBinding("<Gamepad>/buttonEast");

        ui.AddAction("Point", InputActionType.PassThrough, "<Pointer>/position", expectedControlLayout: "Vector2");
        ui.AddAction("Click", InputActionType.PassThrough, "<Pointer>/press", expectedControlLayout: "Button");
        ui.AddAction("ScrollWheel", InputActionType.PassThrough, "<Mouse>/scroll", expectedControlLayout: "Vector2");

        InputAction closeTab = ui.AddAction("CloseTab", InputActionType.Button);
        closeTab.AddBinding("<Keyboard>/tab");
        closeTab.AddBinding("<Gamepad>/leftShoulder");

        InputActionMap debug = asset.AddActionMap("Debug");
        debug.AddAction("ToggleDebug", InputActionType.Button, "<Keyboard>/f1");
        debug.AddAction("ToggleDummy", InputActionType.Button, "<Keyboard>/f2");
        debug.AddAction("ToggleSynergy", InputActionType.Button, "<Keyboard>/f3");

        return asset;
    }

    private void TrackDevice(object changedObject, InputActionChange change)
    {
        if (change != InputActionChange.ActionPerformed || changedObject is not InputAction action || action.activeControl == null)
            return;

        LastDevice = action.activeControl.device is Gamepad
            ? BattleInputDevice.Gamepad
            : BattleInputDevice.KeyboardMouse;
    }
}

public interface IInputModal
{
    void RequestClose();
}
