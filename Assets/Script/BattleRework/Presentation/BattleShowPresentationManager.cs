using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전투 쇼 연출의 중앙 통제 매니저입니다.
/// 씬에 Empty GameObject 하나를 만들고 이 컴포넌트를 붙인 뒤,
/// Default Floor Template에 BattleShowFloorTemplateSO를 할당해서 사용합니다.
///
/// 담당 범위:
/// - SO 기반 32px 슬라이드 바닥 조립
/// - 기본 4x4 Base, 일반 전투 MapBlock, Reward Show Slab에 동일 SO 적용
/// - 선택형 위 판 / 3분할 하판(좌측 끝·중앙 반복·우측 끝) 배치
/// - 바닥에 바로 붙는 4방향 핸들 배치
/// - MapBlock 도킹 순간 핸들 '찰칵' 반동
/// - 사회자 Sprite Sheet 재생
/// - 버드아이뷰 조명 Sprite Sheet 재생
/// - LET'S ROLL! / CUT! 컷인 재생
///
/// 실제 바닥의 이동과 위치 계산은 BattleSpatialMapController / BattleStageTransitionController / MapBlock이 담당하고,
/// 이 스크립트는 아트와 연출 타이밍만 통제합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleShowPresentationManager : MonoBehaviour
{
    public static BattleShowPresentationManager Instance { get; private set; }

    [Header("기본 쇼 바닥 템플릿")]
    [Tooltip("기본 4x4 Base, 굴러오는 모든 전투 필드 조각, Reward Show 바닥에 공통으로 사용할 SO입니다. Project 창에서 Create > Battle > Show > Floor Template으로 만든 뒤, 바닥/위 판/3분할 하판/핸들 Sprite를 넣어 할당합니다.")]
    [SerializeField] private BattleShowFloorTemplateSO defaultFloorTemplate;

    [Header("필드 SO 자동 적용")]
    [Tooltip("켜면 현재 씬의 기본 4x4 Base와 생성되는 WheelSlide MapBlock을 주기적으로 찾아 Default Floor Template을 자동 적용합니다. 일반 전투 필드와 Reward Show Slab 모두 대상입니다.")]
    [SerializeField] private bool autoApplyTemplateToSlidingField = true;

    [Tooltip("새로 생성된 굴러오는 필드 조각을 찾는 간격입니다. 값이 작을수록 생성 직후 빨리 SO가 적용됩니다.")]
    [SerializeField, Range(0.02f, 0.5f)] private float fieldTemplateScanInterval = 0.06f;

    [Header("사회자 Sprite Sheet")]
    [Tooltip("사회자 애니메이션의 잘라진 Sprite 프레임을 재생 순서대로 넣습니다. 비어 있으면 BattleHUD에 설정된 기본 사회자 Sprite를 그대로 사용합니다.")]
    [SerializeField] private Sprite[] presenterFrames;

    [Tooltip("사회자 애니메이션의 초당 프레임 수입니다. 예: 8이면 1초에 8프레임을 재생합니다.")]
    [SerializeField, Min(1f)] private float presenterFps = 8f;

    [Tooltip("켜면 사회자 Sprite Sheet를 마지막 프레임 뒤에 처음부터 반복 재생합니다.")]
    [SerializeField] private bool presenterLoop = true;

    [Tooltip("켜면 아이템 선택 또는 맵 선택 토크쇼에 진입했을 때 사회자 애니메이션을 자동으로 시작합니다.")]
    [SerializeField] private bool autoPlayPresenterDuringReward = true;

    [Header("버드아이뷰 조명 Sprite Sheet")]
    [Tooltip("플레이어와 사회자 발밑 조명에 사용할 Sprite Sheet 프레임입니다. 비어 있으면 BattleHUD가 만드는 기본 타원형 조명을 사용합니다.")]
    [SerializeField] private Sprite[] spotlightFrames;

    [Tooltip("조명 Sprite Sheet의 초당 프레임 수입니다.")]
    [SerializeField, Min(1f)] private float spotlightFps = 10f;

    [Tooltip("켜면 조명 Sprite Sheet를 반복 재생합니다.")]
    [SerializeField] private bool spotlightLoop = true;

    [Tooltip("켜면 아이템 선택 또는 맵 선택 토크쇼에 진입했을 때 플레이어/사회자 조명 애니메이션을 자동으로 시작합니다.")]
    [SerializeField] private bool autoPlaySpotlightDuringReward = true;

    [Header("쇼 컷인 Sprite Sheet")]
    [Tooltip("전투/엘리트 노드가 시작될 때 재생할 LET'S ROLL! 컷인 프레임입니다. 비어 있으면 아무것도 재생하지 않습니다.")]
    [SerializeField] private Sprite[] letsRollFrames;

    [Tooltip("전투가 끝나고 보상 선택으로 넘어갈 때 재생할 CUT! 컷인 프레임입니다. 비어 있으면 아무것도 재생하지 않습니다.")]
    [SerializeField] private Sprite[] cutFrames;

    [Tooltip("LET'S ROLL! / CUT! 컷인의 초당 프레임 수입니다.")]
    [SerializeField, Min(1f)] private float cueFps = 12f;

    [Tooltip("컷인 이미지가 화면에서 차지하는 UI 크기입니다. 기준 해상도는 1920x1080입니다.")]
    [SerializeField] private Vector2 cueSize = new(900f, 360f);

    [Tooltip("컷인 이미지의 화면 기준 위치입니다. (0,0)=좌하단, (0.5,0.5)=중앙, (1,1)=우상단입니다.")]
    [SerializeField] private Vector2 cueAnchor = new(0.5f, 0.58f);

    [Tooltip("Cue Anchor를 기준으로 컷인 위치를 추가로 이동시키는 픽셀 오프셋입니다.")]
    [SerializeField] private Vector2 cueOffset = Vector2.zero;

    [Tooltip("컷인 전용 Canvas의 Sorting Order입니다. BattleHUD보다 앞에 보여야 하므로 기본값은 높게 잡혀 있습니다.")]
    [SerializeField] private int cueCanvasSortingOrder = 900;

    private BattleShowFloorTemplateSO runtimeFloorTemplate;
    private BattleRunManager runManager;
    private BattleHUD hud;
    private RoomBaseTemplate baseTemplate;
    private Image playerSpotlightImage;
    private Image presenterSpotlightImage;
    private Canvas cueCanvas;
    private Image cueImage;

    private Coroutine presenterRoutine;
    private Coroutine spotlightRoutine;
    private Coroutine cueRoutine;
    private Coroutine floorDecorateRoutine;

    private bool selectionShowPresentationActive;
    private bool subscribed;
    private bool warnedMissingTemplate;
    private float nextFieldTemplateScan;

    private readonly HashSet<MapBlock> decoratedBlocks = new();
    /// <summary>런타임 임시 교체가 없으면 Inspector의 Default Floor Template을 사용합니다.</summary>
    public BattleShowFloorTemplateSO ActiveFloorTemplate =>
        runtimeFloorTemplate != null ? runtimeFloorTemplate : defaultFloorTemplate;

    public BattleShowFloorTemplateSO DefaultFloorTemplate => defaultFloorTemplate;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[BattleShowPresentationManager] 여러 Manager가 존재합니다. 먼저 생성된 Manager만 사용합니다.", this);
            enabled = false;
            return;
        }

        Instance = this;
        ResolveReferences();
        EnsureCueCanvas();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeRunEvents();
        nextFieldTemplateScan = 0f;
    }

    private void OnDisable()
    {
        UnsubscribeRunEvents();
        StopPresenterAnimation();
        StopSpotlightAnimation();
        StopCue();

        if (floorDecorateRoutine != null)
            StopCoroutine(floorDecorateRoutine);
        floorDecorateRoutine = null;

        UnsubscribeDecoratedBlocks();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (hud == null || runManager == null || baseTemplate == null)
        {
            ResolveReferences();
            SubscribeRunEvents();
        }

        if (selectionShowPresentationActive)
            ResolveHudSpotlightImages();

        if (autoApplyTemplateToSlidingField &&
            ActiveFloorTemplate != null &&
            Time.unscaledTime >= nextFieldTemplateScan)
        {
            nextFieldTemplateScan = Time.unscaledTime + Mathf.Max(0.02f, fieldTemplateScanInterval);
            DecorateAllLiveFloor(false);
        }

        CleanupDecoratedBlocks();
    }

    /// <summary>
    /// 런타임 중 특정 스테이지/테마의 다른 바닥 SO로 잠시 교체할 때 사용합니다.
    /// null을 넘기면 다시 Default Floor Template을 사용합니다.
    /// </summary>
    public void SetFloorTemplate(BattleShowFloorTemplateSO template, bool refreshExisting = true)
    {
        runtimeFloorTemplate = template;
        ResetTemplateWarnings();

        if (refreshExisting)
            RefreshSlidingFloorArt();
    }

    /// <summary>런타임 바닥 SO 교체를 해제하고 Inspector의 기본 SO로 되돌립니다.</summary>
    public void ResetFloorTemplate(bool refreshExisting = true)
    {
        runtimeFloorTemplate = null;
        ResetTemplateWarnings();

        if (refreshExisting)
            RefreshSlidingFloorArt();
    }

    private void ResetTemplateWarnings()
    {
        warnedMissingTemplate = false;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (hud == null)
            hud = FindFirstObjectByType<BattleHUD>();
        if (baseTemplate == null)
            baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
    }

    private void SubscribeRunEvents()
    {
        if (runManager == null || subscribed)
            return;

        runManager.StateChanged += HandleRunStateChanged;
        runManager.NodeEntered += HandleNodeEntered;
        runManager.RewardSelectionRequested += HandleRewardSelectionRequested;
        subscribed = true;
        HandleRunStateChanged(runManager.State);
    }

    private void UnsubscribeRunEvents()
    {
        if (!subscribed || runManager == null)
            return;

        runManager.StateChanged -= HandleRunStateChanged;
        runManager.NodeEntered -= HandleNodeEntered;
        runManager.RewardSelectionRequested -= HandleRewardSelectionRequested;
        subscribed = false;
    }

    private void HandleRunStateChanged(BattleRunState state)
    {
        bool selectionShow = state == BattleRunState.Reward || state == BattleRunState.SelectingNode;
        if (selectionShow == selectionShowPresentationActive)
            return;

        selectionShowPresentationActive = selectionShow;
        if (selectionShow)
        {
            if (autoPlayPresenterDuringReward)
                PlayPresenterAnimation(true);
            if (autoPlaySpotlightDuringReward)
                PlaySpotlightAnimation(true);
        }
        else
        {
            StopPresenterAnimation();
            StopSpotlightAnimation();
        }
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        if (node == null)
            return;

        if (node.type == BattleNodeType.Combat || node.type == BattleNodeType.Elite)
        {
            PlayLetsRoll();
            nextFieldTemplateScan = 0f;
        }
    }

    private void HandleRewardSelectionRequested(IReadOnlyList<BattleEquipmentSO> _)
    {
        PlayCut();

        if (floorDecorateRoutine != null)
            StopCoroutine(floorDecorateRoutine);

        floorDecorateRoutine = StartCoroutine(DecorateIncomingShowFloorNextFrame());
    }

    // ------------------------------------------------------------------
    // SO 기반 슬라이드 바닥 / 기계식 템플릿
    // ------------------------------------------------------------------

    public Sprite GetRandomFloorSprite(Sprite fallback = null)
    {
        Sprite[] variants = ActiveFloorTemplate != null ? ActiveFloorTemplate.FloorVariants : null;
        if (variants == null || variants.Length == 0)
            return fallback;

        int start = UnityEngine.Random.Range(0, variants.Length);
        for (int i = 0; i < variants.Length; i++)
        {
            Sprite sprite = variants[(start + i) % variants.Length];
            if (sprite != null)
                return sprite;
        }

        return fallback;
    }

    /// <summary>기본 4x4 Base와 현재 존재하는 전투/Reward Show 필드에 활성 SO의 아트를 다시 적용합니다.</summary>
    public void RefreshSlidingFloorArt()
    {
        if (floorDecorateRoutine != null)
            StopCoroutine(floorDecorateRoutine);

        floorDecorateRoutine = StartCoroutine(DecorateAllSlidingFloorNextFrame(true));
    }

    /// <summary>
    /// 다음 스테이지용 4x4 Base가 플레이어 발밑으로 승격됐을 때 Base 아트만 새로 조립합니다.
    /// 굴러온 판은 건드리지 않으므로 기존 랜덤 바닥과 손잡이 배치가 바뀌지 않습니다.
    /// </summary>
    public void RefreshPersistentBaseArt()
    {
        DecoratePersistentBase(true);
    }

    /// <summary>
    /// MapBlock에 현재 SO 템플릿을 즉시 적용합니다.
    /// contactSide 인자는 기존 호출부 호환용으로 남겨 두며,
    /// 실제 하드웨어 배치는 전체 필드의 점유 타일을 기준으로 계산합니다.
    /// </summary>
    public void ApplySlidingTemplate(MapBlock block, Vector2 contactSide, bool rebuild = false)
    {
        if (block == null)
            return;

        _ = contactSide;

        MapBlock[] blocks = FindObjectsByType<MapBlock>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        HashSet<Vector2Int> occupied = BuildOccupiedFloorCells(blocks);
        DecorateSlidingBlock(block.transform, rebuild, occupied);
    }

    private IEnumerator DecorateIncomingShowFloorNextFrame(bool rebuild = false)
    {
        yield return null;
        DecorateAllLiveFloor(rebuild);
        floorDecorateRoutine = null;
    }

    private IEnumerator DecorateAllSlidingFloorNextFrame(bool rebuild)
    {
        yield return null;
        DecorateAllLiveFloor(rebuild);
        floorDecorateRoutine = null;
    }

    private void DecorateAllLiveFloor(bool rebuild)
    {
        DecoratePersistentBase(rebuild);
        DecorateAllLiveSlidingBlocks(rebuild);
    }

    /// <summary>
    /// 현재 씬에 실제 생성된 굴러오는 MapBlock을 찾아 SO를 입힙니다.
    /// 기존 구현이 RewardShowSlab_ 이름만 찾던 문제를 없애 일반 전투 필드도 함께 처리합니다.
    /// </summary>
    private void DecorateAllLiveSlidingBlocks(bool rebuild)
    {
        MapBlock[] blocks = FindObjectsByType<MapBlock>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        List<MapBlock> liveBlocks = new();

        for (int i = 0; i < blocks.Length; i++)
        {
            MapBlock block = blocks[i];
            if (block == null || !block.WillImpact)
                continue;

            string blockName = block.name;
            // Runtime 전투 조각은 Prototype을 Instantiate한 뒤에도
            // "__RuntimeRoomPiecePrototype_...(Clone)" 이름을 유지합니다.
            // 복제본은 포함하되 10000,10000에 있는 원본 Prototype 자체는 제외합니다.
            bool rawPrototype =
                blockName.StartsWith("__RuntimeRoomPiecePrototype_", StringComparison.Ordinal) &&
                !blockName.EndsWith("(Clone)", StringComparison.Ordinal);
            if (rawPrototype || blockName.StartsWith("Outgoing_", StringComparison.Ordinal))
                continue;

            if (!HasSupportedFloorTiles(block.transform))
                continue;

            liveBlocks.Add(block);
        }

        HashSet<Vector2Int> occupiedFloorCells = BuildOccupiedFloorCells(blocks);

        // 자동 스캔은 아직 장식되지 않은 새 판만 처리합니다.
        // 도킹/필드 확장 때 기존 판을 rebuild하면 랜덤 바닥과 손잡이가 다시 배치되어
        // 판이 연결 순간 재정렬되는 것처럼 보이므로, 명시적인 Refresh에서만 rebuild합니다.
        for (int i = 0; i < liveBlocks.Count; i++)
            DecorateSlidingBlock(
                liveBlocks[i].transform,
                rebuild,
                occupiedFloorCells);
    }

    private static bool HasSupportedFloorTiles(Transform blockRoot)
    {
        if (blockRoot == null)
            return false;

        Transform visual = blockRoot.Find("Visual");
        Transform tileRoot = visual != null ? visual : blockRoot;

        for (int i = 0; i < tileRoot.childCount; i++)
        {
            string childName = tileRoot.GetChild(i).name;
            if (childName.StartsWith("ShowTile_", StringComparison.Ordinal) ||
                childName.StartsWith("Tile_", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private HashSet<Vector2Int> BuildOccupiedFloorCells(MapBlock[] blocks)
    {
        HashSet<Vector2Int> occupied = new();
        ResolveReferences();
        if (baseTemplate != null && baseTemplate.ActiveBase != null)
        {
            Vector3 origin = baseTemplate.FixedTileOriginWorld;
            Vector2Int baseOrigin = WorldToFloorCell(origin);
            for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
                for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                    occupied.Add(baseOrigin + new Vector2Int(x, y));
        }

        if (blocks == null)
            return occupied;

        for (int i = 0; i < blocks.Length; i++)
        {
            MapBlock block = blocks[i];
            if (block == null)
                continue;

            string blockName = block.name;
            bool rawPrototype =
                blockName.StartsWith("__RuntimeRoomPiecePrototype_", StringComparison.Ordinal) &&
                !blockName.EndsWith("(Clone)", StringComparison.Ordinal);
            if (rawPrototype || blockName.StartsWith("Outgoing_", StringComparison.Ordinal))
                continue;

            Transform tileRoot = block.transform.Find("Visual");
            if (tileRoot == null)
                tileRoot = block.transform;

            for (int childIndex = 0; childIndex < tileRoot.childCount; childIndex++)
            {
                Transform child = tileRoot.GetChild(childIndex);
                if (!IsSupportedFloorTile(child))
                    continue;
                Vector2Int cell = ResolveDestinationFloorCell(block, child);
                occupied.Add(cell);
            }
        }

        return occupied;
    }

    private static Vector2Int WorldToFloorCell(Vector3 worldPosition)
    {
        return new Vector2Int(
            Mathf.RoundToInt(worldPosition.x / RoomBaseTemplate.TileWorldSize),
            Mathf.RoundToInt(worldPosition.y / RoomBaseTemplate.TileWorldSize));
    }

    private static Vector2Int ResolveDestinationFloorCell(MapBlock block, Transform tile)
    {
        if (tile == null)
            return Vector2Int.zero;

        Vector3 destinationWorld = tile.position;
        if (block != null && block.HasEntryDestination)
            destinationWorld += block.EntryDestination - block.transform.position;
        return WorldToFloorCell(destinationWorld);
    }

    private static bool IsSupportedFloorTile(Transform child)
    {
        if (child == null)
            return false;

        return child.name.StartsWith("ShowTile_", StringComparison.Ordinal) ||
               child.name.StartsWith("Tile_", StringComparison.Ordinal);
    }

    /// <summary>
    /// MapBlock이 아닌 고정 4x4 Base에도 같은 SO의 Floor Variant를 적용합니다.
    /// 원본 Base Renderer/Collider는 그대로 두고 1x1 타일 16개를 위에 조립하므로
    /// 기존 NavMesh와 충돌 구조는 건드리지 않습니다.
    /// 고정 Base에는 아래 하판과 네 외곽면 양 끝의 손잡이를 함께 설치합니다.
    /// </summary>
    private void DecoratePersistentBase(bool rebuild)
    {
        ResolveReferences();
        BattleShowFloorTemplateSO template = ActiveFloorTemplate;
        GameObject activeBase = baseTemplate != null ? baseTemplate.ActiveBase : null;
        if (activeBase == null)
            return;

        Transform baseRoot = activeBase.transform;
        Transform oldTemplate = baseRoot.Find("PresentationTemplate");
        if (oldTemplate != null)
        {
            if (!rebuild)
                return;

            oldTemplate.name = "PresentationTemplate_Removing";
            oldTemplate.gameObject.SetActive(false);
            Destroy(oldTemplate.gameObject);
        }

        // SO가 비어 있을 때는 기존 Base 아트만 남기고 런타임 더미를 만들지 않습니다.
        if (template == null)
            return;

        SpriteRenderer sourceRenderer = FindLargestSpriteRenderer(activeBase);
        int sortingLayerId = sourceRenderer != null ? sourceRenderer.sortingLayerID : 0;
        int sourceSorting = sourceRenderer != null ? sourceRenderer.sortingOrder : -19;
        bool replaceFloorArt = HasUsableFloorVariant(template);

        ResolvePartSorting(
            template,
            sourceSorting,
            replaceFloorArt,
            out int floorSorting,
            out _,
            out int lowerSorting,
            out int handleSorting);

        GameObject templateObject = new("PresentationTemplate");
        Transform templateRoot = templateObject.transform;
        templateRoot.position = baseTemplate.FixedCenterWorld;
        templateRoot.rotation = Quaternion.identity;
        templateRoot.localScale = Vector3.one;
        templateRoot.SetParent(baseRoot, true);

        const int min = 0;
        const int max = RoomBaseTemplate.FixedBaseTiles - 1;
        const float centerOffset = (RoomBaseTemplate.FixedBaseTiles - 1) * 0.5f;

        if (replaceFloorArt)
        {
            for (int y = min; y <= max; y++)
            {
                for (int x = min; x <= max; x++)
                {
                    Sprite floorSprite = GetRandomFloorSprite();
                    if (floorSprite == null)
                        continue;

                    CreateTemplateSprite(
                        templateRoot,
                        $"BaseFloor_{x}_{y}",
                        new Vector3(x - centerOffset, y - centerOffset, 0f),
                        floorSprite,
                        template.FloorTint,
                        sortingLayerId,
                        floorSorting);
                }
            }
        }

        // 최초 4x4 Base에도 아래 하판은 기본 설치합니다.
        for (int x = min; x <= max; x++)
        {
            Sprite lowerSprite = ResolveLowerPlateSprite(template, x, min, max);
            if (lowerSprite == null)
                continue;

            CreateTemplateSprite(
                templateRoot,
                $"BaseLowerPlate_{x}",
                new Vector3(x - centerOffset, min - centerOffset - 1f, 0f),
                lowerSprite,
                template.PlateTint,
                sortingLayerId,
                lowerSorting);
        }

        // 고정 Base도 굴러오는 판과 같은 체결 구조가 보이도록
        // 네 면의 양 끝에 2개씩 배치합니다. 한 면 내부에는 반복하지 않습니다.
        if (template.HandlePlacement != BattleShowHandlePlacementMode.None)
        {
            CreateFixedFaceHandles(
                templateRoot,
                "DockHandle_Lower",
                new Vector3(min - centerOffset, min - centerOffset - 1f, 0f),
                new Vector3(max - centerOffset, min - centerOffset - 1f, 0f),
                template.LowerHandleSprite32,
                sortingLayerId,
                handleSorting,
                template.HandleTint);

            Sprite upperHandle = template.UpperHandleSprite32 != null
                ? template.UpperHandleSprite32
                : template.UpperPlateSprite32;
            CreateFixedFaceHandles(
                templateRoot,
                "DockHandle_Upper",
                new Vector3(min - centerOffset, max - centerOffset + 1f, 0f),
                new Vector3(max - centerOffset, max - centerOffset + 1f, 0f),
                upperHandle,
                sortingLayerId,
                handleSorting,
                template.HandleTint);

            CreateFixedFaceHandles(
                templateRoot,
                "DockHandle_Left",
                new Vector3(min - centerOffset - 1f, min - centerOffset, 0f),
                new Vector3(min - centerOffset - 1f, max - centerOffset, 0f),
                template.LeftHandleSprite32,
                sortingLayerId,
                handleSorting,
                template.HandleTint);

            CreateFixedFaceHandles(
                templateRoot,
                "DockHandle_Right",
                new Vector3(max - centerOffset + 1f, min - centerOffset, 0f),
                new Vector3(max - centerOffset + 1f, max - centerOffset, 0f),
                template.RightHandleSprite32,
                sortingLayerId,
                handleSorting,
                template.HandleTint);
        }
    }

    private static void CreateFixedFaceHandles(
        Transform templateRoot,
        string groupName,
        Vector3 negativeEnd,
        Vector3 positiveEnd,
        Sprite sprite,
        int sortingLayerId,
        int sortingOrder,
        Color tint)
    {
        if (templateRoot == null || sprite == null)
            return;

        GameObject groupObject = new(groupName);
        groupObject.transform.SetParent(templateRoot, false);
        CreateOptionalHandle(
            groupObject.transform,
            $"{groupName}_NegativeEnd",
            negativeEnd,
            sprite,
            tint,
            sortingLayerId,
            sortingOrder);
        CreateOptionalHandle(
            groupObject.transform,
            $"{groupName}_PositiveEnd",
            positiveEnd,
            sprite,
            tint,
            sortingLayerId,
            sortingOrder);
    }

    private void DecorateSlidingBlock(
        Transform slabRoot,
        bool rebuild,
        HashSet<Vector2Int> occupiedFloorCells)
    {
        if (slabRoot == null)
            return;

        // Reward Show Slab은 Visual 자식 아래에 타일이 있고,
        // 일반 전투 필드 런타임 MapBlock은 Root 바로 아래에 Tile_*이 있습니다.
        Transform visual = slabRoot.Find("Visual");
        if (visual == null)
            visual = slabRoot;

        Transform oldTemplate = visual.Find("PresentationTemplate");
        if (oldTemplate != null)
        {
            if (!rebuild)
            {
                SubscribeDockImpact(slabRoot.GetComponent<MapBlock>());
                return;
            }

            oldTemplate.name = "PresentationTemplate_Removing";
            oldTemplate.gameObject.SetActive(false);
            Destroy(oldTemplate.gameObject);
        }

        BattleShowFloorTemplateSO template = ActiveFloorTemplate;
        WarnTemplateState(template);

        bool foundTile = false;
        int templateSortingLayerId = 0;
        int sourceFloorSortingOrder = -18;
        bool foundSortingLayer = false;
        List<SpriteRenderer> floorRenderers = new();
        List<Transform> floorTiles = new();

        bool replaceFloorArt = HasUsableFloorVariant(template);
        Color floorTint = template != null ? template.FloorTint : Color.white;

        for (int i = 0; i < visual.childCount; i++)
        {
            Transform child = visual.GetChild(i);
            if (child == null)
                continue;

            if (!IsSupportedFloorTile(child))
                continue;

            SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
            if (renderer == null)
                continue;

            if (!foundSortingLayer)
            {
                templateSortingLayerId = renderer.sortingLayerID;
                sourceFloorSortingOrder = renderer.sortingOrder;
                foundSortingLayer = true;
            }
            floorRenderers.Add(renderer);
            floorTiles.Add(child);

            foundTile = true;
        }

        if (!foundTile)
            return;

        ResolvePartSorting(
            template,
            sourceFloorSortingOrder,
            replaceFloorArt,
            out int floorSorting,
            out int upperSort,
            out int lowerSort,
            out int handleSort);
        RaiseSlidingStructureAboveBase(
            ref templateSortingLayerId,
            ref floorSorting,
            ref upperSort,
            ref lowerSort,
            ref handleSort);

        for (int i = 0; i < floorRenderers.Count; i++)
        {
            SpriteRenderer renderer = floorRenderers[i];
            if (replaceFloorArt)
            {
                Sprite randomFloor = GetRandomFloorSprite(renderer.sprite);
                if (randomFloor != null)
                    renderer.sprite = randomFloor;
                renderer.color = floorTint;
            }

            // 바닥 본체는 해당 판의 상판/하판/핸들보다 항상 앞에 보입니다.
            renderer.sortingLayerID = templateSortingLayerId;
            renderer.sortingOrder = floorSorting;
        }

        GameObject templateObject = new("PresentationTemplate");
        templateObject.transform.SetParent(visual, false);
        Transform templateRoot = templateObject.transform;

        Color plateTint = template != null ? template.PlateTint : Color.white;

        // 비어 있는 Sprite 슬롯은 런타임 회색/흰색 더미로 대체하지 않습니다.
        // Floor Variants가 비어 있으면 위에서 기존 Tile Sprite를 그대로 유지하고,
        // 위/하판과 핸들은 Sprite가 지정된 부품만 생성합니다.

        CreateExteriorHardware(
            templateRoot,
            slabRoot.GetComponent<MapBlock>(),
            floorTiles,
            occupiedFloorCells,
            template,
            plateTint,
            templateSortingLayerId,
            lowerSort,
            handleSort);

        SubscribeDockImpact(slabRoot.GetComponent<MapBlock>());
    }

    private static bool HasUsableFloorVariant(BattleShowFloorTemplateSO template)
    {
        Sprite[] variants = template != null ? template.FloorVariants : null;
        if (variants == null)
            return false;

        for (int i = 0; i < variants.Length; i++)
        {
            if (variants[i] != null)
                return true;
        }

        return false;
    }

    private static SpriteRenderer FindLargestSpriteRenderer(GameObject root)
    {
        if (root == null)
            return null;

        SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        SpriteRenderer best = null;
        float bestArea = -1f;
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || !renderer.gameObject.activeInHierarchy)
                continue;

            float area = Mathf.Abs(renderer.bounds.size.x * renderer.bounds.size.y);
            if (area > bestArea)
            {
                bestArea = area;
                best = renderer;
            }
        }

        return best;
    }

    private static void ResolvePartSorting(
        BattleShowFloorTemplateSO template,
        int sourceFloorSorting,
        bool hasFloorOverlay,
        out int floorSorting,
        out int upperSorting,
        out int lowerSorting,
        out int handleSorting)
    {
        if (template == null)
        {
            floorSorting = sourceFloorSorting;
            upperSorting = floorSorting - 1;
            handleSorting = floorSorting - 1;
            lowerSorting = floorSorting - 2;
            return;
        }

        if (hasFloorOverlay)
        {
            int highestRequestedPart = Mathf.Max(
                template.UpperPlateSortingOrder,
                Mathf.Max(template.LowerPlateSortingOrder, template.HandleSortingOrder));
            floorSorting = Mathf.Max(
                sourceFloorSorting,
                Mathf.Max(template.FloorSortingOrder, highestRequestedPart + 1));
        }
        else
        {
            // Floor Variant가 비어 있으면 기존 바닥 Renderer가 최상단이 됩니다.
            floorSorting = sourceFloorSorting;
        }

        upperSorting = Mathf.Min(template.UpperPlateSortingOrder, floorSorting - 1);
        lowerSorting = Mathf.Min(template.LowerPlateSortingOrder, floorSorting - 2);
        handleSorting = Mathf.Clamp(
            template.HandleSortingOrder,
            lowerSorting + 1,
            floorSorting - 1);
    }

    /// <summary>
    /// 새 판의 핸들은 기존 4x4 Base 위에서 보이고,
    /// 새 판의 바닥은 그 핸들보다 한 단계 앞에 보이게 전체 구조를 같이 올립니다.
    /// </summary>
    private void RaiseSlidingStructureAboveBase(
        ref int sortingLayerId,
        ref int floorSorting,
        ref int upperSorting,
        ref int lowerSorting,
        ref int handleSorting)
    {
        if (baseTemplate == null || baseTemplate.ActiveBase == null)
            return;

        int selectedLayerValue = SortingLayer.GetLayerValueFromID(sortingLayerId);
        int baseTopSorting = int.MinValue;
        SpriteRenderer[] baseRenderers = baseTemplate.ActiveBase.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < baseRenderers.Length; i++)
        {
            SpriteRenderer renderer = baseRenderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            int candidateLayerValue = SortingLayer.GetLayerValueFromID(renderer.sortingLayerID);
            if (candidateLayerValue > selectedLayerValue)
            {
                sortingLayerId = renderer.sortingLayerID;
                selectedLayerValue = candidateLayerValue;
                baseTopSorting = renderer.sortingOrder;
            }
            else if (renderer.sortingLayerID == sortingLayerId)
            {
                baseTopSorting = Mathf.Max(baseTopSorting, renderer.sortingOrder);
            }
        }

        if (baseTopSorting == int.MinValue)
            return;

        int offset = Mathf.Max(0, baseTopSorting + 1 - handleSorting);
        floorSorting += offset;
        upperSorting += offset;
        lowerSorting += offset;
        handleSorting += offset;
    }

    private static Sprite ResolveLowerPlateSprite(
        BattleShowFloorTemplateSO template,
        int x,
        int minX,
        int maxX)
    {
        if (template == null)
            return null;

        if (minX == maxX)
        {
            return template.LowerPlateCenterSprite32 != null
                ? template.LowerPlateCenterSprite32
                : template.LowerPlateLeftSprite32 != null
                    ? template.LowerPlateLeftSprite32
                    : template.LowerPlateRightSprite32;
        }

        if (x == minX)
        {
            return template.LowerPlateLeftSprite32 != null
                ? template.LowerPlateLeftSprite32
                : template.LowerPlateCenterSprite32;
        }

        if (x == maxX)
        {
            return template.LowerPlateRightSprite32 != null
                ? template.LowerPlateRightSprite32
                : template.LowerPlateCenterSprite32;
        }

        return template.LowerPlateCenterSprite32;
    }

    private static void CreateExteriorHardware(
        Transform templateRoot,
        MapBlock block,
        List<Transform> floorTiles,
        HashSet<Vector2Int> occupiedFloorCells,
        BattleShowFloorTemplateSO template,
        Color plateTint,
        int sortingLayerId,
        int lowerSorting,
        int handleSorting)
    {
        if (template == null || floorTiles == null || occupiedFloorCells == null)
            return;

        List<Transform> exposedLeft = new();
        List<Transform> exposedRight = new();
        List<Transform> exposedUpper = new();
        List<Transform> exposedLower = new();
        HashSet<Vector2Int> blockFloorCells = new();

        for (int i = 0; i < floorTiles.Count; i++)
            blockFloorCells.Add(ResolveDestinationFloorCell(block, floorTiles[i]));

        for (int i = 0; i < floorTiles.Count; i++)
        {
            Transform tile = floorTiles[i];
            Vector2Int cell = ResolveDestinationFloorCell(block, tile);
            if (!occupiedFloorCells.Contains(cell + Vector2Int.left)) exposedLeft.Add(tile);
            if (!occupiedFloorCells.Contains(cell + Vector2Int.right)) exposedRight.Add(tile);
            if (!occupiedFloorCells.Contains(cell + Vector2Int.up)) exposedUpper.Add(tile);
            // 하판은 최종 필드가 아니라 이 판 자체의 아래쪽 윤곽을 사용합니다.
            // 다른 판이 아래에 도킹해도 처음 굴러올 때 있던 하판을 삭제하지 않습니다.
            if (!blockFloorCells.Contains(cell + Vector2Int.down)) exposedLower.Add(tile);
        }

        // 하판은 완성된 전체 필드가 아니라 이 판 안의 연속된 아래쪽 구간마다
        // Left / Center / Right를 다시 시작합니다. 도킹 뒤에도 각 판의 경계가 남아
        // 여러 판이 하나의 긴 하판으로 합쳐져 보이지 않습니다.
        if (exposedLower.Count > 0)
        {
            exposedLower.Sort((a, b) =>
            {
                Vector2Int cellA = ResolveDestinationFloorCell(block, a);
                Vector2Int cellB = ResolveDestinationFloorCell(block, b);
                int row = cellA.y.CompareTo(cellB.y);
                return row != 0 ? row : cellA.x.CompareTo(cellB.x);
            });

            int runStart = 0;
            for (int i = 1; i <= exposedLower.Count; i++)
            {
                bool endOfRun = i == exposedLower.Count;
                if (!endOfRun)
                {
                    Vector2Int previous = ResolveDestinationFloorCell(block, exposedLower[i - 1]);
                    Vector2Int current = ResolveDestinationFloorCell(block, exposedLower[i]);
                    endOfRun = current.y != previous.y || current.x != previous.x + 1;
                }

                if (!endOfRun)
                    continue;

                CreateLowerPlateRun(
                    templateRoot,
                    block,
                    exposedLower,
                    runStart,
                    i - 1,
                    template,
                    plateTint,
                    sortingLayerId,
                    lowerSorting);
                runStart = i;
            }
        }

        if (template.HandlePlacement == BattleShowHandlePlacementMode.None)
            return;

        CreateRandomExteriorHandles(
            templateRoot,
            exposedLeft,
            Vector2.left,
            "DockHandle_Left",
            template.LeftHandleSprite32,
            template,
            sortingLayerId,
            handleSorting);
        CreateRandomExteriorHandles(
            templateRoot,
            exposedRight,
            Vector2.right,
            "DockHandle_Right",
            template.RightHandleSprite32,
            template,
            sortingLayerId,
            handleSorting);

        // 기존 Upper Plate 슬롯에 상단 핸들 이미지를 넣어둔 SO도 바로 동작하도록
        // Upper Handle이 비어 있으면 Upper Plate를 상단 양 끝 핸들로 사용합니다.
        Sprite upperHandle = template.UpperHandleSprite32 != null
            ? template.UpperHandleSprite32
            : template.UpperPlateSprite32;
        CreateRandomExteriorHandles(
            templateRoot,
            exposedUpper,
            Vector2.up,
            "DockHandle_Upper",
            upperHandle,
            template,
            sortingLayerId,
            handleSorting);
        CreateRandomExteriorHandles(
            templateRoot,
            exposedLower,
            Vector2.down,
            "DockHandle_Lower",
            template.LowerHandleSprite32,
            template,
            sortingLayerId,
            handleSorting);
    }

    private static void CreateLowerPlateRun(
        Transform templateRoot,
        MapBlock block,
        List<Transform> exposedLower,
        int firstIndex,
        int lastIndex,
        BattleShowFloorTemplateSO template,
        Color plateTint,
        int sortingLayerId,
        int lowerSorting)
    {
        if (firstIndex < 0 || lastIndex < firstIndex || lastIndex >= exposedLower.Count)
            return;

        Vector2Int firstCell = ResolveDestinationFloorCell(block, exposedLower[firstIndex]);
        Vector2Int lastCell = ResolveDestinationFloorCell(block, exposedLower[lastIndex]);
        for (int i = firstIndex; i <= lastIndex; i++)
        {
            Transform tile = exposedLower[i];
            Vector2Int cell = ResolveDestinationFloorCell(block, tile);
            Sprite lowerSprite = ResolveLowerPlateSprite(template, cell.x, firstCell.x, lastCell.x);
            if (lowerSprite == null)
                continue;

            CreateTemplateSprite(
                templateRoot,
                $"LowerPlate_{cell.x}_{cell.y}",
                tile.localPosition + Vector3.down,
                lowerSprite,
                plateTint,
                sortingLayerId,
                lowerSorting);
        }
    }

    /// <summary>
    /// 굴러오는 판 하나의 노출 면에서 허용된 양 끝 중 1개 또는 2개를 랜덤 배치합니다.
    /// 생성된 손잡이는 판의 자식으로 남아 도킹 이후에도 위치가 바뀌지 않습니다.
    /// </summary>
    private static void CreateRandomExteriorHandles(
        Transform templateRoot,
        List<Transform> exposedTiles,
        Vector2 side,
        string groupName,
        Sprite sprite,
        BattleShowFloorTemplateSO template,
        int sortingLayerId,
        int sortingOrder)
    {
        if (sprite == null || exposedTiles == null || exposedTiles.Count == 0)
            return;

        Transform negativeEnd = exposedTiles[0];
        Transform positiveEnd = exposedTiles[0];
        for (int i = 1; i < exposedTiles.Count; i++)
        {
            Transform candidate = exposedTiles[i];
            if (IsBetterLocalFaceEndpoint(candidate, negativeEnd, side, false))
                negativeEnd = candidate;
            if (IsBetterLocalFaceEndpoint(candidate, positiveEnd, side, true))
                positiveEnd = candidate;
        }

        GameObject groupObject = new(groupName);
        groupObject.transform.SetParent(templateRoot, false);

        if (negativeEnd == positiveEnd)
        {
            CreateOptionalHandle(
                groupObject.transform,
                $"{groupName}_OnlyEnd",
                negativeEnd.localPosition + (Vector3)side,
                sprite,
                template.HandleTint,
                sortingLayerId,
                sortingOrder);
            return;
        }

        // 0=음의 끝 1개, 1=양의 끝 1개, 2=양 끝 2개.
        // 판이 처음 등장할 때 한 번만 뽑고 이후 자동 rebuild를 하지 않으므로 그대로 유지됩니다.
        int randomLayout = UnityEngine.Random.Range(0, 3);
        if (randomLayout != 1)
        {
            CreateOptionalHandle(
                groupObject.transform,
                $"{groupName}_NegativeEnd",
                negativeEnd.localPosition + (Vector3)side,
                sprite,
                template.HandleTint,
                sortingLayerId,
                sortingOrder);
        }

        if (randomLayout != 0)
        {
            CreateOptionalHandle(
                groupObject.transform,
                $"{groupName}_PositiveEnd",
                positiveEnd.localPosition + (Vector3)side,
                sprite,
                template.HandleTint,
                sortingLayerId,
                sortingOrder);
        }
    }

    private static bool IsBetterLocalFaceEndpoint(
        Transform candidate,
        Transform current,
        Vector2 side,
        bool positiveEnd)
    {
        float candidateAxis = Mathf.Abs(side.x) > 0.5f
            ? candidate.localPosition.y
            : candidate.localPosition.x;
        float currentAxis = Mathf.Abs(side.x) > 0.5f
            ? current.localPosition.y
            : current.localPosition.x;
        if (!Mathf.Approximately(candidateAxis, currentAxis))
            return positiveEnd
                ? candidateAxis > currentAxis
                : candidateAxis < currentAxis;

        float candidateOutward = Vector2.Dot(candidate.localPosition, side);
        float currentOutward = Vector2.Dot(current.localPosition, side);
        return candidateOutward > currentOutward;
    }

    private static void CreateOptionalHandle(
        Transform parent,
        string objectName,
        Vector3 localPosition,
        Sprite sprite,
        Color tint,
        int sortingLayerId,
        int sortingOrder)
    {
        if (sprite == null)
            return;

        CreateTemplateSprite(parent, objectName, localPosition, sprite, tint, sortingLayerId, sortingOrder);
    }

    private static SpriteRenderer CreateTemplateSprite(
        Transform parent,
        string objectName,
        Vector3 localPosition,
        Sprite sprite,
        Color tint,
        int sortingLayerId,
        int sortingOrder)
    {
        GameObject go = new(objectName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = tint;
        renderer.sortingLayerID = sortingLayerId;
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    private void SubscribeDockImpact(MapBlock block)
    {
        if (block == null || decoratedBlocks.Contains(block))
            return;

        block.Impacted += HandleBlockImpacted;
        decoratedBlocks.Add(block);
    }

    private void HandleBlockImpacted(MapBlock block, Vector3 _, Vector2 travelDirection, float strength)
    {
        if (block == null)
            return;

        BattleShowFloorTemplateSO template = ActiveFloorTemplate;
        if (template == null || template.DockHandlePunch <= 0.001f)
            return;

        Transform visual = block.transform.Find("Visual");
        if (visual == null)
            visual = block.transform;

        Transform presentationTemplate = visual.Find("PresentationTemplate");
        if (presentationTemplate == null)
            return;

        // MapBlock의 travelDirection은 실제 이동 방향이므로, 체결면은 그 이동 방향 쪽 면입니다.
        Vector2 contactSide = NormalizeCardinal(travelDirection);
        Transform handle = FindHandle(presentationTemplate, contactSide);
        if (handle == null)
            return;

        handle.DOKill();
        handle.localScale = Vector3.one;

        float punch = template.DockHandlePunch * Mathf.Clamp(strength, 0.45f, 1.8f);
        handle.DOPunchScale(
                new Vector3(punch, punch, 0f),
                Mathf.Max(0.03f, template.DockHandlePunchDuration),
                Mathf.Max(1, template.DockHandlePunchVibrato),
                0.45f)
            .SetUpdate(true);
    }

    private static Transform FindHandle(Transform templateRoot, Vector2 side)
    {
        if (templateRoot == null)
            return null;

        if (side == Vector2.left) return templateRoot.Find("DockHandle_Left");
        if (side == Vector2.right) return templateRoot.Find("DockHandle_Right");
        if (side == Vector2.up) return templateRoot.Find("DockHandle_Upper");
        if (side == Vector2.down) return templateRoot.Find("DockHandle_Lower");
        return null;
    }

    private static Vector2 NormalizeCardinal(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return Vector2.left;

        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
            return direction.x >= 0f ? Vector2.right : Vector2.left;

        return direction.y >= 0f ? Vector2.up : Vector2.down;
    }

    private void CleanupDecoratedBlocks()
    {
        if (decoratedBlocks.Count == 0)
            return;

        List<MapBlock> dead = null;
        foreach (MapBlock block in decoratedBlocks)
        {
            if (block != null)
                continue;

            dead ??= new List<MapBlock>();
            dead.Add(block);
        }

        if (dead == null)
            return;

        for (int i = 0; i < dead.Count; i++)
            decoratedBlocks.Remove(dead[i]);
    }

    private void UnsubscribeDecoratedBlocks()
    {
        foreach (MapBlock block in decoratedBlocks)
        {
            if (block != null)
                block.Impacted -= HandleBlockImpacted;
        }

        decoratedBlocks.Clear();
    }

    private void WarnTemplateState(BattleShowFloorTemplateSO template)
    {
        if (template == null && !warnedMissingTemplate)
        {
            warnedMissingTemplate = true;
            Debug.LogWarning("[BattleShowPresentationManager] Default Floor Template SO가 비어 있습니다. 기존 바닥 Sprite를 그대로 유지하며 추가 판/핸들은 만들지 않습니다.", this);
        }

    }

    // ------------------------------------------------------------------
    // 사회자 Sprite Sheet
    // ------------------------------------------------------------------

    public void PlayPresenterAnimation(bool restart = true)
    {
        ResolveReferences();
        if (presenterFrames == null || presenterFrames.Length == 0 || hud == null)
            return;

        if (presenterRoutine != null)
        {
            if (!restart)
                return;
            StopCoroutine(presenterRoutine);
        }

        presenterRoutine = StartCoroutine(PlayPresenterFrames());
    }

    public void StopPresenterAnimation()
    {
        if (presenterRoutine != null)
            StopCoroutine(presenterRoutine);
        presenterRoutine = null;
    }

    private IEnumerator PlayPresenterFrames()
    {
        int frame = 0;
        float delay = 1f / Mathf.Max(1f, presenterFps);

        while (true)
        {
            Sprite sprite = presenterFrames[frame];
            if (sprite != null && hud != null)
                hud.SetPresenterSprite(sprite);

            frame++;
            if (frame >= presenterFrames.Length)
            {
                if (!presenterLoop)
                    break;
                frame = 0;
            }

            yield return new WaitForSecondsRealtime(delay);
        }

        presenterRoutine = null;
    }

    // ------------------------------------------------------------------
    // 버드아이뷰 조명 Sprite Sheet
    // ------------------------------------------------------------------

    public void PlaySpotlightAnimation(bool restart = true)
    {
        if (spotlightFrames == null || spotlightFrames.Length == 0)
            return;

        ResolveHudSpotlightImages();

        if (spotlightRoutine != null)
        {
            if (!restart)
                return;
            StopCoroutine(spotlightRoutine);
        }

        spotlightRoutine = StartCoroutine(PlaySpotlightFrames());
    }

    public void StopSpotlightAnimation()
    {
        if (spotlightRoutine != null)
            StopCoroutine(spotlightRoutine);
        spotlightRoutine = null;
    }

    private IEnumerator PlaySpotlightFrames()
    {
        int frame = 0;
        float delay = 1f / Mathf.Max(1f, spotlightFps);

        while (true)
        {
            ResolveHudSpotlightImages();
            Sprite sprite = spotlightFrames[frame];

            if (sprite != null)
            {
                if (playerSpotlightImage != null)
                    playerSpotlightImage.sprite = sprite;
                if (presenterSpotlightImage != null)
                    presenterSpotlightImage.sprite = sprite;
            }

            frame++;
            if (frame >= spotlightFrames.Length)
            {
                if (!spotlightLoop)
                    break;
                frame = 0;
            }

            yield return new WaitForSecondsRealtime(delay);
        }

        spotlightRoutine = null;
    }

    private void ResolveHudSpotlightImages()
    {
        if (playerSpotlightImage != null && presenterSpotlightImage != null)
            return;

        Image[] images = FindObjectsByType<Image>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null)
                continue;

            if (image.name == "PlayerFloorSpotlight")
                playerSpotlightImage = image;
            else if (image.name == "PresenterFloorSpotlight")
                presenterSpotlightImage = image;
        }
    }

    // ------------------------------------------------------------------
    // LET'S ROLL! / CUT! 컷인
    // ------------------------------------------------------------------

    public void PlayLetsRoll() => PlayCue(letsRollFrames);
    public void PlayCut() => PlayCue(cutFrames);

    public void PlayCue(Sprite[] frames)
    {
        EnsureCueCanvas();
        if (frames == null || frames.Length == 0 || cueImage == null)
            return;

        if (cueRoutine != null)
            StopCoroutine(cueRoutine);

        cueRoutine = StartCoroutine(PlayCueFrames(frames));
    }

    public void StopCue()
    {
        if (cueRoutine != null)
            StopCoroutine(cueRoutine);

        cueRoutine = null;
        if (cueImage != null)
            cueImage.enabled = false;
    }

    private IEnumerator PlayCueFrames(Sprite[] frames)
    {
        cueImage.enabled = true;
        float delay = 1f / Mathf.Max(1f, cueFps);

        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null)
                cueImage.sprite = frames[i];

            yield return new WaitForSecondsRealtime(delay);
        }

        cueImage.enabled = false;
        cueRoutine = null;
    }

    private void EnsureCueCanvas()
    {
        if (cueCanvas != null)
            return;

        GameObject canvasObject = new("ShowCueCanvas");
        canvasObject.transform.SetParent(transform, false);

        cueCanvas = canvasObject.AddComponent<Canvas>();
        cueCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        cueCanvas.sortingOrder = cueCanvasSortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject imageObject = new("ShowCueImage");
        imageObject.transform.SetParent(canvasObject.transform, false);

        RectTransform rect = imageObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = cueAnchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = cueSize;
        rect.anchoredPosition = cueOffset;

        cueImage = imageObject.AddComponent<Image>();
        cueImage.preserveAspect = true;
        cueImage.raycastTarget = false;
        cueImage.enabled = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (defaultFloorTemplate == null)
        {
            Debug.LogWarning(
                "[BattleShowPresentationManager] Default Floor Template SO를 할당하세요. Create > Battle > Show > Floor Template에서 만들 수 있습니다.",
                this);
        }
    }
#endif
}
