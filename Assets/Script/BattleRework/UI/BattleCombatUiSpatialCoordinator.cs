using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Combat / TAB UI의 공통 2.5D 공간 Coordinator.
///
/// 책임:
/// - PACK / RULES / MISSION / CHAT 메인 판을 자동 탐색해 BattleUiSpatialSurface를 부착합니다.
/// - 화면 기준 Cursor Tilt를 공통 계산합니다.
/// - 회전된 Screen Bounds끼리 겹치면 Focus / 화면 Y / 기본 우선순위로 앞뒤 Depth를 재배치합니다.
/// - RectTransform Z와 Nested Canvas sortingOrder를 함께 움직여 서로 다른 Canvas 사이의 앞뒤 관계도 통일합니다.
/// - Mini PACK / Compact PACK / Viewer Metric 같은 전투 HUD에는 약한 Cursor Tilt만 적용합니다.
/// - Detail / Tooltip은 항상 가장 높은 Overlay Depth로 보호합니다.
///
/// 기존 UI Owner의 Update를 건드리지 않기 위해:
/// 1) 이 컴포넌트의 Update(매우 이른 ExecutionOrder)에서 지난 Spatial Pass를 복원
/// 2) 기존 UI Owner들이 자신의 Transform 애니메이션을 계산
/// 3) LateUpdate에서 Cursor Tilt / Depth를 추가 적용
/// 순서로 동작합니다.
/// </summary>
[DefaultExecutionOrder(-30000)]
[DisallowMultipleComponent]
public sealed class BattleCombatUiSpatialCoordinator : MonoBehaviour
{
    private const string RuntimeHostName =
        "BattleCombatUiSpatialCoordinator";

    [Header("CURSOR TILT")]
    [SerializeField] private bool enableSpatialUi = true;
    [SerializeField, Range(0f, 24f)] private float maxTiltX = 14.0f;
    [SerializeField, Range(0f, 24f)] private float maxTiltY = 16.0f;
    [SerializeField, Range(1f, 30f)] private float tiltSharpness = 12f;
    [SerializeField, Range(0f, 1f)] private float focusedTiltStrength = 1f;
    [SerializeField, Range(0f, 1f)] private float restTiltStrength = 0.80f;
    [SerializeField, Range(0f, 1f)] private float suppressedTiltStrength = 0.50f;
    [SerializeField, Range(0f, 1f)] private float combatHudTiltStrength = 0.58f;
    [SerializeField, Range(0f, 0.25f)] private float pointerDeadZone = 0.01f;

    [Header("COMBAT HUD PLAYER PIVOT")]
    [Tooltip("TAB이 닫힌 전투 HUD Tilt에서 플레이어 화면 위치가 차지하는 비중입니다.")]
    [SerializeField, Range(0f, 1f)] private float combatPlayerTiltWeight = 0.82f;

    [Tooltip("TAB이 닫힌 전투 HUD Tilt에서 마우스 커서가 차지하는 보조 비중입니다.")]
    [SerializeField, Range(0f, 1f)] private float combatCursorTiltWeight = 0.18f;

    [Tooltip("화면 위/아래에 있는 HUD가 Screen Center 쪽을 바라보게 만드는 기본 X축 기울기입니다.")]
    [SerializeField, Range(0f, 12f)] private float centerGatherTiltX = 3.0f;

    [Tooltip("화면 좌/우에 있는 HUD가 Screen Center 쪽을 바라보게 만드는 기본 Y축 기울기입니다.")]
    [SerializeField, Range(0f, 16f)] private float centerGatherTiltY = 5.5f;

    [Header("DEPTH")]
    [Tooltip("Depth Score 1당 RectTransform Z 이동량입니다. 현재 Battle UI 관례대로 음수 Z가 앞으로 옵니다.")]
    [SerializeField, Min(1f)] private float zStep = 18f;
    [SerializeField, Range(1f, 30f)] private float depthSharpness = 13f;
    [SerializeField] private int spatialSortingBase = 3200;
    [SerializeField, Range(1, 32)] private int sortingStep = 12;
    [SerializeField, Range(0f, 8f)] private float focusedDepthBonus = 3.2f;
    [SerializeField, Range(0f, 4f)] private float suppressedDepthPenalty = 0.8f;
    [SerializeField, Range(0f, 3f)] private float verticalDepthWeight = 0.85f;
    [SerializeField, Range(0f, 4f)] private float overlapDepthBoost = 1.15f;
    [SerializeField, Range(10, 200)] private int overlaySortingBonus = 96;

    [Header("OVERLAP")]
    [SerializeField] private bool resolveOverlap = true;
    [SerializeField, Range(0f, 48f)] private float overlapEnterPadding = 8f;
    [SerializeField, Range(0f, 72f)] private float overlapExitPadding = 24f;

    [Header("SCREEN SAFETY")]
    [Tooltip("화면 가장자리로 갈수록 X/Y Tilt를 줄여 회전된 패널이 화면 밖으로 잘리는 양을 줄입니다.")]
    [SerializeField] private bool reduceTiltNearScreenEdge = false;
    [SerializeField, Range(12f, 240f)] private float edgeTiltFadeDistance = 96f;
    [SerializeField, Range(0f, 1f)] private float minimumEdgeTiltStrength = 0.65f;

    [Header("AUTO BIND")]
    [SerializeField, Range(0.05f, 2f)] private float referenceScanInterval = 0.30f;

    private static BattleCombatUiSpatialCoordinator instance;

    private BattleKineticLoadoutUI kineticLoadout;
    private BattleRuleRouletteController rouletteController;
    private BattleBroadcastDashboardController dashboardController;
    private BattleKineticItemBarUI miniPackUi;
    private PlayerController player;
    private Camera playerScreenCamera;

    private BattleUiSpatialSurface packSurface;
    private BattleUiSpatialSurface rulesSurface;
    private BattleUiSpatialSurface missionSurface;
    private BattleUiSpatialSurface chatSurface;

    private BattleUiSpatialSurface compactSurface;
    private BattleUiSpatialSurface miniPackSurface;
    private BattleUiSpatialSurface metricSurface;

    private BattleUiSpatialSurface packDetailSurface;
    private BattleUiSpatialSurface ruleDetailSurface;

    private readonly List<BattleUiSpatialSurface> allSurfaces = new();
    private readonly List<BattleUiSpatialSurface> activeTabSurfaces = new();
    private readonly Dictionary<int, float> depthScores = new();
    private readonly Dictionary<ulong, bool> overlapLatch = new();

    private float nextReferenceScanAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeHost()
    {
        if (instance != null)
            return;

        BattleCombatUiSpatialCoordinator existing =
            Object.FindFirstObjectByType<BattleCombatUiSpatialCoordinator>();

        if (existing != null)
        {
            instance = existing;
            return;
        }

        GameObject host =
            new(RuntimeHostName);

        Object.DontDestroyOnLoad(
            host);

        instance =
            host.AddComponent<BattleCombatUiSpatialCoordinator>();
    }

    private void Awake()
    {
        if (instance != null &&
            instance != this)
        {
            Destroy(
                gameObject);
            return;
        }

        instance =
            this;

        SceneManager.sceneLoaded -=
            HandleSceneLoaded;

        SceneManager.sceneLoaded +=
            HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -=
            HandleSceneLoaded;

        RestoreAllSurfaces();

        if (instance == this)
            instance = null;
    }

    private void HandleSceneLoaded(
        Scene _,
        LoadSceneMode __)
    {
        kineticLoadout = null;
        rouletteController = null;
        dashboardController = null;
        miniPackUi = null;
        player = null;
        playerScreenCamera = null;

        packSurface = null;
        rulesSurface = null;
        missionSurface = null;
        chatSurface = null;
        compactSurface = null;
        miniPackSurface = null;
        metricSurface = null;
        packDetailSurface = null;
        ruleDetailSurface = null;

        allSurfaces.Clear();
        activeTabSurfaces.Clear();
        depthScores.Clear();
        overlapLatch.Clear();

        nextReferenceScanAt =
            0f;
    }

    /// <summary>
    /// 기존 UI Owner Update보다 먼저 지난 프레임의 additive 공간값을 제거합니다.
    /// </summary>
    private void Update()
    {
        RestoreAllSurfaces();

        if (Time.unscaledTime >=
            nextReferenceScanAt)
        {
            nextReferenceScanAt =
                Time.unscaledTime +
                Mathf.Max(
                    0.05f,
                    referenceScanInterval);

            ResolveAndBindSurfaces();
        }
    }

    /// <summary>
    /// 모든 기존 UI Update가 끝난 뒤 최종 2.5D 공간 패스를 얹습니다.
    /// </summary>
    private void LateUpdate()
    {
        if (!enableSpatialUi)
            return;

        ResolveAndBindSurfaces();

        if (kineticLoadout == null)
            return;

        float deltaTime =
            Mathf.Max(
                0f,
                Time.unscaledDeltaTime);

        bool tabOpen =
            kineticLoadout.IsSwitchBoardOpen;

        BattleCombatTabFocus focus =
            tabOpen &&
            dashboardController != null
                ? dashboardController.CurrentFocus
                : BattleCombatTabFocus.None;

        Vector2 normalizedPointer =
            ResolveNormalizedPointer();

        Vector2 normalizedPlayer =
            ResolveNormalizedPlayerScreenPosition();

        ApplyPersistentCombatHud(
            normalizedPointer,
            normalizedPlayer,
            tabOpen,
            deltaTime);

        ApplyTabSpatialLayout(
            normalizedPointer,
            normalizedPlayer,
            tabOpen,
            focus,
            deltaTime);

        ApplyOverlayProtection(
            deltaTime);
    }

    private void ResolveAndBindSurfaces()
    {
        if (kineticLoadout == null)
        {
            kineticLoadout =
                Object.FindFirstObjectByType<BattleKineticLoadoutUI>(
                    FindObjectsInactive.Include);
        }

        if (rouletteController == null)
        {
            rouletteController =
                Object.FindFirstObjectByType<BattleRuleRouletteController>(
                    FindObjectsInactive.Include);
        }

        if (dashboardController == null)
        {
            dashboardController =
                Object.FindFirstObjectByType<BattleBroadcastDashboardController>(
                    FindObjectsInactive.Include);
        }

        if (miniPackUi == null)
        {
            miniPackUi =
                Object.FindFirstObjectByType<BattleKineticItemBarUI>(
                    FindObjectsInactive.Include);
        }

        if (player == null)
        {
            player =
                Object.FindFirstObjectByType<PlayerController>(
                    FindObjectsInactive.Include);
        }

        if (playerScreenCamera == null)
        {
            playerScreenCamera =
                Camera.main;

            if (playerScreenCamera == null)
            {
                playerScreenCamera =
                    Object.FindFirstObjectByType<Camera>();
            }
        }

        RectTransform fullRoot =
            kineticLoadout != null
                ? kineticLoadout.FullRoot
                : null;

        BindSurface(
            ref packSurface,
            kineticLoadout != null
                ? kineticLoadout.GridBoard
                : null,
            BattleUiSpatialSurface.SurfaceRole.Pack,
            0.10f,
            1f,
            true,
            true);

        BindSurface(
            ref rulesSurface,
            rouletteController != null
                ? rouletteController.CombatRulePanel
                : null,
            BattleUiSpatialSurface.SurfaceRole.Rules,
            0.20f,
            0.94f,
            true,
            true);

        BindSurface(
            ref missionSurface,
            FindRect(
                fullRoot,
                "BroadcastDashboard/MissionPanel"),
            BattleUiSpatialSurface.SurfaceRole.Mission,
            0f,
            0.90f,
            true,
            false);

        BindSurface(
            ref chatSurface,
            FindRect(
                fullRoot,
                "BroadcastDashboard/ChatPanel"),
            BattleUiSpatialSurface.SurfaceRole.Chat,
            0f,
            0.82f,
            true,
            false);

        BindSurface(
            ref compactSurface,
            kineticLoadout != null
                ? kineticLoadout.CompactRoot
                : null,
            BattleUiSpatialSurface.SurfaceRole.CombatCompact,
            0f,
            0.50f,
            false,
            false);

        BindSurface(
            ref miniPackSurface,
            miniPackUi != null
                ? miniPackUi.Root
                : null,
            BattleUiSpatialSurface.SurfaceRole.MiniPack,
            0f,
            0.42f,
            false,
            false);

        RectTransform metricRoot =
            null;

        if (fullRoot != null &&
            fullRoot.parent != null)
        {
            metricRoot =
                fullRoot.parent.Find(
                    "BroadcastMetricBar") as RectTransform;
        }

        BindSurface(
            ref metricSurface,
            metricRoot,
            BattleUiSpatialSurface.SurfaceRole.Metric,
            0f,
            0.36f,
            false,
            false);

        BindSurface(
            ref packDetailSurface,
            FindRect(
                fullRoot,
                "DetailPanel"),
            BattleUiSpatialSurface.SurfaceRole.Overlay,
            8f,
            0f,
            true,
            false);

        BindSurface(
            ref ruleDetailSurface,
            rouletteController != null
                ? rouletteController.WinningRuleTab
                : null,
            BattleUiSpatialSurface.SurfaceRole.Overlay,
            8f,
            0f,
            true,
            false);
    }

    private void BindSurface(
        ref BattleUiSpatialSurface slot,
        RectTransform root,
        BattleUiSpatialSurface.SurfaceRole role,
        float baseDepth,
        float tiltMultiplier,
        bool spatialSorting,
        bool preserveRaycasts)
    {
        if (root == null)
        {
            slot = null;
            return;
        }

        if (slot != null &&
            slot.Rect == root)
        {
            return;
        }

        BattleUiSpatialSurface surface =
            root.GetComponent<BattleUiSpatialSurface>();

        if (surface == null)
        {
            surface =
                root.gameObject.AddComponent<BattleUiSpatialSurface>();
        }

        surface.Configure(
            role,
            baseDepth,
            tiltMultiplier,
            spatialSorting,
            preserveRaycasts);

        slot =
            surface;

        if (!allSurfaces.Contains(
                surface))
        {
            allSurfaces.Add(
                surface);
        }
    }

    private void RestoreAllSurfaces()
    {
        for (int i = allSurfaces.Count - 1;
             i >= 0;
             i--)
        {
            BattleUiSpatialSurface surface =
                allSurfaces[i];

            if (surface == null)
            {
                allSurfaces.RemoveAt(
                    i);
                continue;
            }

            surface.RestoreOwnerPose();
        }
    }

    private void ApplyPersistentCombatHud(
        Vector2 normalizedPointer,
        Vector2 normalizedPlayer,
        bool tabOpen,
        float deltaTime)
    {
        if (tabOpen)
        {
            float tabStrength =
                suppressedTiltStrength *
                0.35f;

            ApplyTiltOnlySurface(
                compactSurface,
                normalizedPointer,
                tabStrength,
                deltaTime);

            ApplyTiltOnlySurface(
                miniPackSurface,
                normalizedPointer,
                tabStrength * 0.72f,
                deltaTime);

            ApplyTiltOnlySurface(
                metricSurface,
                normalizedPointer,
                tabStrength * 0.68f,
                deltaTime);

            return;
        }

        // 전투 중에는 Cursor보다 Player의 화면상 위치를 주 Pivot으로 사용합니다.
        // Player가 오른쪽으로 이동하면 UI의 Y축 Tilt도 오른쪽 방향으로 더 기울고,
        // Cursor는 미세한 보조 입력으로만 남습니다.
        Vector2 combatDriver =
            ResolveCombatHudDriver(
                normalizedPlayer,
                normalizedPointer);

        ApplyCombatHudSurface(
            compactSurface,
            combatDriver,
            combatHudTiltStrength,
            deltaTime);

        ApplyCombatHudSurface(
            miniPackSurface,
            combatDriver,
            combatHudTiltStrength * 0.82f,
            deltaTime);

        ApplyCombatHudSurface(
            metricSurface,
            combatDriver,
            combatHudTiltStrength * 0.78f,
            deltaTime);
    }

    private void ApplyCombatHudSurface(
        BattleUiSpatialSurface surface,
        Vector2 combatDriver,
        float strength,
        float deltaTime)
    {
        if (surface == null ||
            !surface.IsRenderable)
        {
            return;
        }

        float edgeSafety =
            ResolveEdgeTiltSafety(
                surface);

        float surfaceStrength =
            strength *
            surface.TiltMultiplier *
            edgeSafety;

        Vector2 dynamicTilt =
            ResolveTilt(
                combatDriver,
                surfaceStrength);

        Vector2 gatherTilt =
            ResolveCenterGatherTilt(
                surface) *
            surface.TiltMultiplier;

        surface.BeginSpatialPass(
            dynamicTilt +
            gatherTilt,
            tiltSharpness,
            deltaTime);

        surface.ApplyDepth(
            0f,
            spatialSortingBase,
            depthSharpness,
            deltaTime);
    }

    private void ApplyTiltOnlySurface(
        BattleUiSpatialSurface surface,
        Vector2 normalizedPointer,
        float strength,
        float deltaTime)
    {
        if (surface == null ||
            !surface.IsRenderable)
        {
            return;
        }

        float edgeSafety =
            ResolveEdgeTiltSafety(
                surface);

        Vector2 tilt =
            ResolveTilt(
                normalizedPointer,
                strength *
                surface.TiltMultiplier *
                edgeSafety);

        surface.BeginSpatialPass(
            tilt,
            tiltSharpness,
            deltaTime);

        surface.ApplyDepth(
            0f,
            spatialSortingBase,
            depthSharpness,
            deltaTime);
    }

    private void ApplyTabSpatialLayout(
        Vector2 normalizedPointer,
        Vector2 normalizedPlayer,
        bool tabOpen,
        BattleCombatTabFocus focus,
        float deltaTime)
    {
        activeTabSurfaces.Clear();
        depthScores.Clear();

        AddActiveTabSurface(
            packSurface,
            tabOpen);

        AddActiveTabSurface(
            missionSurface,
            tabOpen);

        AddActiveTabSurface(
            chatSurface,
            tabOpen);

        // RULES는 전투 중 TAB이 닫혀도 Persistent HUD로 남으므로
        // 표시 중이면 약한 2.5D 반응을 계속 허용합니다.
        if (rulesSurface != null &&
            rulesSurface.IsRenderable)
        {
            activeTabSurfaces.Add(
                rulesSurface);
        }

        if (activeTabSurfaces.Count <= 0)
            return;

        for (int i = 0;
             i < activeTabSurfaces.Count;
             i++)
        {
            BattleUiSpatialSurface surface =
                activeTabSurfaces[i];

            bool isFocused =
                tabOpen &&
                RoleMatchesFocus(
                    surface.Role,
                    focus);

            bool suppressed =
                tabOpen &&
                focus != BattleCombatTabFocus.None &&
                !isFocused;

            float strength =
                !tabOpen
                    ? combatHudTiltStrength
                    : isFocused
                        ? focusedTiltStrength
                        : suppressed
                            ? suppressedTiltStrength
                            : restTiltStrength;

            float edgeSafety =
                ResolveEdgeTiltSafety(
                    surface);

            Vector2 tilt;

            if (!tabOpen)
            {
                Vector2 combatDriver =
                    ResolveCombatHudDriver(
                        normalizedPlayer,
                        normalizedPointer);

                tilt =
                    ResolveTilt(
                        combatDriver,
                        strength *
                        surface.TiltMultiplier *
                        edgeSafety) +
                    ResolveCenterGatherTilt(
                        surface) *
                    surface.TiltMultiplier;
            }
            else
            {
                tilt =
                    ResolveTilt(
                        normalizedPointer,
                        strength *
                        surface.TiltMultiplier *
                        edgeSafety);
            }

            surface.BeginSpatialPass(
                tilt,
                tiltSharpness,
                deltaTime);
        }

        // Tilt가 적용된 뒤의 실제 Screen Bounds를 기준으로 Depth를 계산합니다.
        for (int i = 0;
             i < activeTabSurfaces.Count;
             i++)
        {
            BattleUiSpatialSurface surface =
                activeTabSurfaces[i];

            Rect rect =
                surface.GetScreenRect();

            float screenHeight =
                Mathf.Max(
                    1f,
                    Screen.height);

            float y01 =
                Mathf.Clamp01(
                    rect.center.y /
                    screenHeight);

            // 화면 아래쪽 패널일수록 조금 더 앞으로 옵니다.
            float verticalBias =
                (0.5f - y01) *
                2f *
                verticalDepthWeight;

            bool isFocused =
                tabOpen &&
                RoleMatchesFocus(
                    surface.Role,
                    focus);

            bool suppressed =
                tabOpen &&
                focus != BattleCombatTabFocus.None &&
                !isFocused;

            float score =
                surface.BaseDepth +
                verticalBias;

            if (isFocused)
            {
                score +=
                    focusedDepthBonus;
            }
            else if (suppressed)
            {
                score -=
                    suppressedDepthPenalty;
            }

            depthScores[
                surface.GetInstanceID()] =
                score;
        }

        if (resolveOverlap &&
            activeTabSurfaces.Count > 1)
        {
            ResolveOverlapDepth(
                focus);
        }

        for (int i = 0;
             i < activeTabSurfaces.Count;
             i++)
        {
            BattleUiSpatialSurface surface =
                activeTabSurfaces[i];

            int id =
                surface.GetInstanceID();

            float score =
                depthScores.TryGetValue(
                    id,
                    out float value)
                    ? value
                    : 0f;

            float targetZ =
                -score *
                Mathf.Max(
                    1f,
                    zStep);

            int sorting =
                spatialSortingBase +
                Mathf.RoundToInt(
                    score *
                    Mathf.Max(
                        1,
                        sortingStep));

            surface.ApplyDepth(
                targetZ,
                sorting,
                depthSharpness,
                deltaTime);
        }
    }

    private void AddActiveTabSurface(
        BattleUiSpatialSurface surface,
        bool tabOpen)
    {
        if (!tabOpen ||
            surface == null ||
            !surface.IsRenderable)
        {
            return;
        }

        activeTabSurfaces.Add(
            surface);
    }

    private void ResolveOverlapDepth(
        BattleCombatTabFocus focus)
    {
        for (int a = 0;
             a < activeTabSurfaces.Count - 1;
             a++)
        {
            BattleUiSpatialSurface first =
                activeTabSurfaces[a];

            for (int b = a + 1;
                 b < activeTabSurfaces.Count;
                 b++)
            {
                BattleUiSpatialSurface second =
                    activeTabSurfaces[b];

                if (!ResolveOverlapLatch(
                        first,
                        second))
                {
                    continue;
                }

                BattleUiSpatialSurface front =
                    ResolveOverlapFront(
                        first,
                        second,
                        focus);

                BattleUiSpatialSurface back =
                    front == first
                        ? second
                        : first;

                int frontId =
                    front.GetInstanceID();

                int backId =
                    back.GetInstanceID();

                depthScores[frontId] =
                    depthScores[frontId] +
                    overlapDepthBoost;

                depthScores[backId] =
                    depthScores[backId] -
                    overlapDepthBoost *
                    0.55f;
            }
        }
    }

    private bool ResolveOverlapLatch(
        BattleUiSpatialSurface first,
        BattleUiSpatialSurface second)
    {
        ulong key =
            BuildPairKey(
                first.GetInstanceID(),
                second.GetInstanceID());

        bool wasOverlapping =
            overlapLatch.TryGetValue(
                key,
                out bool latched) &&
            latched;

        float padding =
            wasOverlapping
                ? overlapExitPadding
                : overlapEnterPadding;

        Rect a =
            ExpandRect(
                first.GetScreenRect(),
                padding);

        Rect b =
            ExpandRect(
                second.GetScreenRect(),
                padding);

        bool overlapping =
            a.Overlaps(
                b,
                true);

        overlapLatch[key] =
            overlapping;

        return overlapping;
    }

    private BattleUiSpatialSurface ResolveOverlapFront(
        BattleUiSpatialSurface first,
        BattleUiSpatialSurface second,
        BattleCombatTabFocus focus)
    {
        bool firstFocused =
            RoleMatchesFocus(
                first.Role,
                focus);

        bool secondFocused =
            RoleMatchesFocus(
                second.Role,
                focus);

        if (firstFocused !=
            secondFocused)
        {
            return firstFocused
                ? first
                : second;
        }

        Rect firstRect =
            first.GetScreenRect();

        Rect secondRect =
            second.GetScreenRect();

        // 같은 상태면 화면에서 더 아래에 있는 카드가 앞으로 옵니다.
        if (Mathf.Abs(
                firstRect.center.y -
                secondRect.center.y) >
            1f)
        {
            return firstRect.center.y <
                   secondRect.center.y
                ? first
                : second;
        }

        return first.BaseDepth >=
               second.BaseDepth
            ? first
            : second;
    }

    private void ApplyOverlayProtection(
        float deltaTime)
    {
        int overlaySorting =
            spatialSortingBase +
            Mathf.Max(
                10,
                overlaySortingBonus);

        ApplyOverlaySurface(
            packDetailSurface,
            overlaySorting,
            deltaTime);

        ApplyOverlaySurface(
            ruleDetailSurface,
            overlaySorting + 1,
            deltaTime);
    }

    private void ApplyOverlaySurface(
        BattleUiSpatialSurface surface,
        int sortingOrder,
        float deltaTime)
    {
        if (surface == null ||
            !surface.IsRenderable)
        {
            return;
        }

        surface.BeginSpatialPass(
            Vector2.zero,
            tiltSharpness,
            deltaTime);

        surface.ApplyDepth(
            -Mathf.Abs(
                zStep) *
            5f,
            sortingOrder,
            depthSharpness,
            deltaTime);
    }

    private Vector2 ResolveNormalizedPlayerScreenPosition()
    {
        if (player == null ||
            playerScreenCamera == null ||
            Screen.width <= 0 ||
            Screen.height <= 0)
        {
            return Vector2.zero;
        }

        Vector3 screen =
            playerScreenCamera.WorldToScreenPoint(
                player.transform.position);

        if (screen.z < 0f)
            return Vector2.zero;

        return new Vector2(
            Mathf.Clamp(
                screen.x /
                Mathf.Max(
                    1f,
                    Screen.width) *
                2f -
                1f,
                -1f,
                1f),
            Mathf.Clamp(
                screen.y /
                Mathf.Max(
                    1f,
                    Screen.height) *
                2f -
                1f,
                -1f,
                1f));
    }

    private Vector2 ResolveCombatHudDriver(
        Vector2 normalizedPlayer,
        Vector2 normalizedPointer)
    {
        float playerWeight =
            Mathf.Max(
                0f,
                combatPlayerTiltWeight);

        float cursorWeight =
            Mathf.Max(
                0f,
                combatCursorTiltWeight);

        float total =
            playerWeight +
            cursorWeight;

        if (total <= 0.0001f)
            return Vector2.zero;

        Vector2 driver =
            (normalizedPlayer *
             playerWeight +
             normalizedPointer *
             cursorWeight) /
            total;

        return new Vector2(
            Mathf.Clamp(
                driver.x,
                -1f,
                1f),
            Mathf.Clamp(
                driver.y,
                -1f,
                1f));
    }

    private Vector2 ResolveCenterGatherTilt(
        BattleUiSpatialSurface surface)
    {
        if (surface == null ||
            Screen.width <= 0 ||
            Screen.height <= 0)
        {
            return Vector2.zero;
        }

        Rect rect =
            surface.GetScreenRect();

        Vector2 centerNormalized =
            new(
                rect.center.x /
                Mathf.Max(
                    1f,
                    Screen.width) *
                2f -
                1f,
                rect.center.y /
                Mathf.Max(
                    1f,
                    Screen.height) *
                2f -
                1f);

        // 오른쪽 HUD는 왼쪽(Screen Center)을 바라보고,
        // 왼쪽 HUD는 오른쪽을 바라보도록 Y축 기본 자세를 줍니다.
        // 위/아래도 같은 원리로 X축을 사용해 중앙으로 살짝 모읍니다.
        return new Vector2(
            centerNormalized.y *
            centerGatherTiltX,
            -centerNormalized.x *
            centerGatherTiltY);
    }

    private Vector2 ResolveNormalizedPointer()
    {
        if (!Input.mousePresent ||
            Screen.width <= 0 ||
            Screen.height <= 0)
        {
            return Vector2.zero;
        }

        Vector2 pointer =
            Input.mousePosition;

        Vector2 normalized =
            new(
                pointer.x /
                Mathf.Max(
                    1f,
                    Screen.width) *
                2f -
                1f,
                pointer.y /
                Mathf.Max(
                    1f,
                    Screen.height) *
                2f -
                1f);

        normalized.x =
            ApplyDeadZone(
                normalized.x);

        normalized.y =
            ApplyDeadZone(
                normalized.y);

        return new Vector2(
            Mathf.Clamp(
                normalized.x,
                -1f,
                1f),
            Mathf.Clamp(
                normalized.y,
                -1f,
                1f));
    }

    private float ApplyDeadZone(
        float value)
    {
        float deadZone =
            Mathf.Clamp(
                pointerDeadZone,
                0f,
                0.25f);

        float absolute =
            Mathf.Abs(
                value);

        if (absolute <=
            deadZone)
        {
            return 0f;
        }

        return Mathf.Sign(
                   value) *
               Mathf.InverseLerp(
                   deadZone,
                   1f,
                   absolute);
    }

    private Vector2 ResolveTilt(
        Vector2 normalizedPointer,
        float strength)
    {
        float safeStrength =
            Mathf.Max(
                0f,
                strength);

        return new Vector2(
            -normalizedPointer.y *
            maxTiltX *
            safeStrength,
            normalizedPointer.x *
            maxTiltY *
            safeStrength);
    }

    private float ResolveEdgeTiltSafety(
        BattleUiSpatialSurface surface)
    {
        if (!reduceTiltNearScreenEdge ||
            surface == null)
        {
            return 1f;
        }

        Rect rect =
            surface.GetScreenRect();

        float nearest =
            Mathf.Min(
                rect.xMin,
                Screen.width -
                rect.xMax,
                rect.yMin,
                Screen.height -
                rect.yMax);

        float safety01 =
            Mathf.Clamp01(
                nearest /
                Mathf.Max(
                    1f,
                    edgeTiltFadeDistance));

        return Mathf.Lerp(
            minimumEdgeTiltStrength,
            1f,
            safety01);
    }

    private static bool RoleMatchesFocus(
        BattleUiSpatialSurface.SurfaceRole role,
        BattleCombatTabFocus focus)
    {
        return role switch
        {
            BattleUiSpatialSurface.SurfaceRole.Pack =>
                focus == BattleCombatTabFocus.Pack,

            BattleUiSpatialSurface.SurfaceRole.Rules =>
                focus == BattleCombatTabFocus.Rules,

            BattleUiSpatialSurface.SurfaceRole.Mission =>
                focus == BattleCombatTabFocus.Mission,

            BattleUiSpatialSurface.SurfaceRole.Chat =>
                focus == BattleCombatTabFocus.Chat,

            _ => false
        };
    }

    private static RectTransform FindRect(
        RectTransform root,
        string path)
    {
        if (root == null ||
            string.IsNullOrWhiteSpace(
                path))
        {
            return null;
        }

        return root.Find(
            path) as RectTransform;
    }

    private static Rect ExpandRect(
        Rect rect,
        float padding)
    {
        float value =
            Mathf.Max(
                0f,
                padding);

        rect.xMin -=
            value;

        rect.xMax +=
            value;

        rect.yMin -=
            value;

        rect.yMax +=
            value;

        return rect;
    }

    private static ulong BuildPairKey(
        int first,
        int second)
    {
        uint a =
            unchecked(
                (uint)Mathf.Min(
                    first,
                    second));

        uint b =
            unchecked(
                (uint)Mathf.Max(
                    first,
                    second));

        return ((ulong)a << 32) |
               b;
    }
}
