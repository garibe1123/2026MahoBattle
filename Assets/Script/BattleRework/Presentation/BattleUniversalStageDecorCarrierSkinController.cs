using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SpriteManager에 저장되는 Universal Stage Decor Carrier의 외형 설정/적용 컴포넌트입니다.
///
/// BattleUniversalStageDecorController는 배치/진입/퇴장 lifecycle만 소유하고,
/// 이 컴포넌트는 생성된 UniversalStageDecor_* Carrier에 다음 presentation만 적용합니다.
/// - 별도 BattleShowFloorTemplateSO의 Floor Variant
/// - 하판 / 4방향 Handle로 이루어진 기계식 외곽 Frame
/// - DecorObject_*의 실제 Renderer bounds를 footprint 안에 자동 Fit / Center
///
/// 실제 Grid / NavMesh / MapBlock 위치는 변경하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30220)]
public sealed class BattleUniversalStageDecorCarrierSkinController : MonoBehaviour
{
    private const string ClusterPrefix = "UniversalStageDecor_";
    private const string VisualRootName = "Visual";
    private const string CarrierTilePrefix = "DecorCarrierTile_";
    private const string DecorObjectPrefix = "DecorObject_";
    private const string FrameRootName = "UniversalDecorCarrierFrame";

    [Header("DECOR CARRIER FLOOR SO")]
    [Tooltip("카메라/조명/케이블 장식 Carrier에만 사용할 별도 바닥 SO입니다. 비어 있으면 기존 Field Floor를 그대로 유지합니다.")]
    [SerializeField] private BattleShowFloorTemplateSO decorCarrierFloorTemplate;

    [Header("DECOR ↔ CARRIER FIT")]
    [Tooltip("장식 Sprite/Prefab의 실제 Renderer Bounds를 읽어 1x1 / 2x3 / 3x3 Carrier 안에 자동으로 맞춥니다.")]
    [SerializeField] private bool autoFitDecorToCarrier = true;
    [Tooltip("Carrier 외곽에서 이 비율만큼 안쪽으로 여유를 둡니다. 0.10이면 가로/세로 각각 양 끝에 10%씩 비웁니다.")]
    [SerializeField, Range(0f, 0.30f)] private float decorFitPadding = 0.10f;
    [Tooltip("Auto Fit 이후 전체 장식에 추가로 적용할 배율입니다. 기본은 1입니다.")]
    [SerializeField, Range(0.50f, 1.35f)] private float fittedDecorScaleMultiplier = 1f;
    [Tooltip("Auto Fit 후 Carrier 중심 기준으로 추가 이동합니다. World Tile 단위가 아니라 Carrier local world-unit입니다.")]
    [SerializeField] private Vector2 fittedDecorOffset = Vector2.zero;
    [Tooltip("켜면 Sprite Pivot이 치우쳐 있어도 실제 보이는 Renderer Bounds 중심이 Carrier 중앙에 오도록 보정합니다.")]
    [SerializeField] private bool centerUsingRendererBounds = true;

    [Header("FRAME")]
    [Tooltip("Floor SO의 하판/핸들을 사용해 Carrier 외곽 프레임을 만듭니다.")]
    [SerializeField] private bool buildMechanicalFrame = true;
    [Tooltip("Frame Sprite가 32px 기준이어도 현재 Carrier 1칸 크기에 맞게 자동 Scale합니다.")]
    [SerializeField] private bool fitFramePartsToCell = true;

    [Header("RUNTIME REFRESH")]
    [SerializeField, Range(0.04f, 0.50f)] private float scanInterval = 0.10f;

    private readonly HashSet<int> processedClusters = new();
    private float nextScanAt;
    private BattleShowFloorTemplateSO lastTemplate;
    private float lastPadding;
    private float lastFitScale;
    private Vector2 lastFitOffset;
    private bool lastAutoFit;
    private bool lastBuildFrame;

    public BattleShowFloorTemplateSO DecorCarrierFloorTemplate => decorCarrierFloorTemplate;

    private void OnEnable()
    {
        InvalidateAll();
    }

    private void OnDisable()
    {
        processedClusters.Clear();
    }

    private void OnValidate()
    {
        scanInterval = Mathf.Max(0.04f, scanInterval);
        fittedDecorScaleMultiplier = Mathf.Clamp(fittedDecorScaleMultiplier, 0.50f, 1.35f);
        decorFitPadding = Mathf.Clamp(decorFitPadding, 0f, 0.30f);

        if (Application.isPlaying)
            InvalidateAll();
    }

    private void LateUpdate()
    {
        if (SettingsChanged())
            InvalidateAll();

        if (Time.unscaledTime < nextScanAt)
            return;

        nextScanAt = Time.unscaledTime + Mathf.Max(0.04f, scanInterval);
        ProcessNewClusters();
    }

    private bool SettingsChanged()
    {
        return lastTemplate != decorCarrierFloorTemplate ||
               !Mathf.Approximately(lastPadding, decorFitPadding) ||
               !Mathf.Approximately(lastFitScale, fittedDecorScaleMultiplier) ||
               lastFitOffset != fittedDecorOffset ||
               lastAutoFit != autoFitDecorToCarrier ||
               lastBuildFrame != buildMechanicalFrame;
    }

    private void CacheSettings()
    {
        lastTemplate = decorCarrierFloorTemplate;
        lastPadding = decorFitPadding;
        lastFitScale = fittedDecorScaleMultiplier;
        lastFitOffset = fittedDecorOffset;
        lastAutoFit = autoFitDecorToCarrier;
        lastBuildFrame = buildMechanicalFrame;
    }

    private void InvalidateAll()
    {
        processedClusters.Clear();
        nextScanAt = 0f;
        CacheSettings();
    }

    private void ProcessNewClusters()
    {
        BattleUniversalStageDecorController universal = BattleUniversalStageDecorController.Instance;
        if (universal == null)
            return;

        Transform runtimeRoot = universal.transform;
        for (int i = 0; i < runtimeRoot.childCount; i++)
        {
            Transform cluster = runtimeRoot.GetChild(i);
            if (cluster == null || !cluster.name.StartsWith(ClusterPrefix, StringComparison.Ordinal))
                continue;

            int id = cluster.GetInstanceID();
            if (!processedClusters.Add(id))
                continue;

            ApplyCarrierPresentation(cluster);
        }
    }

    private void ApplyCarrierPresentation(Transform cluster)
    {
        Transform visual = cluster != null ? cluster.Find(VisualRootName) : null;
        if (visual == null)
            return;

        List<SpriteRenderer> tiles = CollectCarrierTiles(visual);
        if (tiles.Count == 0)
            return;

        if (!TryResolveCarrierGeometry(tiles, out CarrierGeometry geometry))
            return;

        ApplyFloorTemplate(visual, tiles, geometry);

        Transform decor = FindDirectChildByPrefix(visual, DecorObjectPrefix);
        if (decor != null && autoFitDecorToCarrier)
            FitDecorToCarrier(visual, decor, geometry);
    }

    private void ApplyFloorTemplate(
        Transform visual,
        List<SpriteRenderer> tiles,
        CarrierGeometry geometry)
    {
        BattleShowFloorTemplateSO template = decorCarrierFloorTemplate;
        if (template == null)
        {
            RemoveExistingFrame(visual);
            return;
        }

        Sprite[] floorVariants = template.FloorVariants;
        bool hasFloorVariants = HasAnySprite(floorVariants);
        int sourceFloorSorting = tiles[0] != null ? tiles[0].sortingOrder : template.FloorSortingOrder;
        int resolvedFloorSorting = Mathf.Max(sourceFloorSorting, template.FloorSortingOrder);
        int templateSortingOffset = resolvedFloorSorting - template.FloorSortingOrder;

        for (int i = 0; i < tiles.Count; i++)
        {
            SpriteRenderer tile = tiles[i];
            if (tile == null)
                continue;

            if (hasFloorVariants)
            {
                Sprite chosen = PickRandomSprite(floorVariants);
                if (chosen != null)
                {
                    tile.sprite = chosen;
                    ScaleRendererToCell(tile, geometry.cell);
                }
                tile.color = template.FloorTint;
            }

            tile.sortingOrder = resolvedFloorSorting;
        }

        RemoveExistingFrame(visual);
        if (!buildMechanicalFrame)
            return;

        BuildMechanicalFrame(visual, tiles[0], geometry, template, templateSortingOffset);
    }

    private void BuildMechanicalFrame(
        Transform visual,
        SpriteRenderer reference,
        CarrierGeometry geometry,
        BattleShowFloorTemplateSO template,
        int sortingOffset)
    {
        if (visual == null || reference == null || template == null)
            return;

        GameObject frameObject = new(FrameRootName);
        frameObject.transform.SetParent(visual, false);
        Transform frame = frameObject.transform;

        float cell = geometry.cell;
        float minX = geometry.localBounds.min.x + cell * 0.5f;
        float maxX = geometry.localBounds.max.x - cell * 0.5f;
        float minY = geometry.localBounds.min.y + cell * 0.5f;
        float maxY = geometry.localBounds.max.y - cell * 0.5f;
        int lowerSorting = template.LowerPlateSortingOrder + sortingOffset;
        int handleSorting = template.HandleSortingOrder + sortingOffset;

        // 하판: 기존 Show Floor Template과 동일하게 Carrier 아래 한 줄에 좌/중앙/우를 조립합니다.
        int columns = Mathf.Max(1, geometry.columns);
        for (int x = 0; x < columns; x++)
        {
            Sprite lower = ResolveLowerPlateSprite(template, x, columns);
            if (lower == null)
                continue;

            float px = columns == 1
                ? (minX + maxX) * 0.5f
                : Mathf.Lerp(minX, maxX, x / (float)(columns - 1));
            CreateFramePart(
                frame,
                $"LowerPlate_{x}",
                lower,
                new Vector3(px, geometry.localBounds.min.y - cell * 0.5f, 0f),
                reference,
                template.PlateTint,
                lowerSorting,
                cell);
        }

        if (template.HandlePlacement == BattleShowHandlePlacementMode.None)
            return;

        Sprite upper = template.UpperHandleSprite32 != null
            ? template.UpperHandleSprite32
            : template.UpperPlateSprite32;

        // 위/아래 면: 최좌/최우만. 한 칸 폭이면 중앙 하나만.
        CreateHorizontalFaceHandles(
            frame,
            "UpperHandle",
            upper,
            geometry.localBounds.max.y + cell * 0.5f,
            minX,
            maxX,
            reference,
            template.HandleTint,
            handleSorting,
            cell);

        CreateHorizontalFaceHandles(
            frame,
            "LowerHandle",
            template.LowerHandleSprite32,
            geometry.localBounds.min.y - cell * 0.5f,
            minX,
            maxX,
            reference,
            template.HandleTint,
            handleSorting,
            cell);

        // 좌/우 면: 최하/최상만. 한 칸 높이면 중앙 하나만.
        CreateVerticalFaceHandles(
            frame,
            "LeftHandle",
            template.LeftHandleSprite32,
            geometry.localBounds.min.x - cell * 0.5f,
            minY,
            maxY,
            reference,
            template.HandleTint,
            handleSorting,
            cell);

        CreateVerticalFaceHandles(
            frame,
            "RightHandle",
            template.RightHandleSprite32,
            geometry.localBounds.max.x + cell * 0.5f,
            minY,
            maxY,
            reference,
            template.HandleTint,
            handleSorting,
            cell);
    }

    private void FitDecorToCarrier(
        Transform visualRoot,
        Transform decor,
        CarrierGeometry geometry)
    {
        if (visualRoot == null || decor == null)
            return;

        if (!TryGetRendererBounds(decor, out Bounds before))
            return;

        float padding = Mathf.Clamp(decorFitPadding, 0f, 0.30f);
        float targetWidth = Mathf.Max(0.05f, geometry.localBounds.size.x * (1f - padding * 2f));
        float targetHeight = Mathf.Max(0.05f, geometry.localBounds.size.y * (1f - padding * 2f));
        float sourceWidth = Mathf.Max(0.001f, before.size.x);
        float sourceHeight = Mathf.Max(0.001f, before.size.y);

        float fit = Mathf.Min(targetWidth / sourceWidth, targetHeight / sourceHeight);
        fit *= Mathf.Max(0.01f, fittedDecorScaleMultiplier);

        decor.localScale = new Vector3(
            decor.localScale.x * fit,
            decor.localScale.y * fit,
            decor.localScale.z);

        if (!centerUsingRendererBounds)
        {
            decor.localPosition += new Vector3(fittedDecorOffset.x, fittedDecorOffset.y, 0f);
            return;
        }

        if (!TryGetRendererBounds(decor, out Bounds after))
            return;

        Vector3 currentCenterLocal = visualRoot.InverseTransformPoint(after.center);
        Vector3 desiredCenterLocal = geometry.localBounds.center +
                                     new Vector3(fittedDecorOffset.x, fittedDecorOffset.y, 0f);
        Vector3 delta = desiredCenterLocal - currentCenterLocal;
        delta.z = 0f;
        decor.localPosition += delta;
    }

    private static List<SpriteRenderer> CollectCarrierTiles(Transform visual)
    {
        List<SpriteRenderer> result = new();
        if (visual == null)
            return result;

        for (int i = 0; i < visual.childCount; i++)
        {
            Transform child = visual.GetChild(i);
            if (child == null || !child.name.StartsWith(CarrierTilePrefix, StringComparison.Ordinal))
                continue;

            SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
            if (renderer != null)
                result.Add(renderer);
        }
        return result;
    }

    private static bool TryResolveCarrierGeometry(
        List<SpriteRenderer> tiles,
        out CarrierGeometry geometry)
    {
        geometry = default;
        if (tiles == null || tiles.Count == 0)
            return false;

        bool initialized = false;
        Bounds localBounds = default;
        float cell = 0f;
        int maxColumn = -1;
        int maxRow = -1;

        for (int i = 0; i < tiles.Count; i++)
        {
            SpriteRenderer tile = tiles[i];
            if (tile == null || tile.sprite == null)
                continue;

            Vector3 spriteSize = tile.sprite.bounds.size;
            Vector3 localScale = tile.transform.localScale;
            float localWidth = Mathf.Abs(spriteSize.x * localScale.x);
            float localHeight = Mathf.Abs(spriteSize.y * localScale.y);
            float candidateCell = Mathf.Max(0.01f, Mathf.Min(localWidth, localHeight));
            cell = cell <= 0f ? candidateCell : Mathf.Min(cell, candidateCell);

            Vector3 localCenter = tile.transform.localPosition;
            Bounds b = new(localCenter, new Vector3(localWidth, localHeight, 0.01f));
            if (!initialized)
            {
                localBounds = b;
                initialized = true;
            }
            else
            {
                localBounds.Encapsulate(b);
            }

            ParseTileCoordinate(tile.name, out int x, out int y);
            maxColumn = Mathf.Max(maxColumn, x);
            maxRow = Mathf.Max(maxRow, y);
        }

        if (!initialized)
            return false;

        geometry = new CarrierGeometry
        {
            localBounds = localBounds,
            cell = Mathf.Max(0.01f, cell),
            columns = Mathf.Max(1, maxColumn + 1),
            rows = Mathf.Max(1, maxRow + 1)
        };
        return true;
    }

    private void CreateFramePart(
        Transform parent,
        string objectName,
        Sprite sprite,
        Vector3 localPosition,
        SpriteRenderer reference,
        Color tint,
        int sortingOrder,
        float cell)
    {
        if (parent == null || sprite == null)
            return;

        GameObject go = new(objectName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = tint;
        renderer.sharedMaterial = reference != null ? reference.sharedMaterial : null;
        renderer.sortingLayerID = reference != null ? reference.sortingLayerID : 0;
        renderer.sortingOrder = sortingOrder;

        if (fitFramePartsToCell)
            ScaleRendererToCell(renderer, cell);
    }

    private void CreateHorizontalFaceHandles(
        Transform parent,
        string prefix,
        Sprite sprite,
        float y,
        float leftX,
        float rightX,
        SpriteRenderer reference,
        Color tint,
        int sortingOrder,
        float cell)
    {
        if (sprite == null)
            return;

        if (Mathf.Abs(rightX - leftX) < cell * 0.5f)
        {
            CreateFramePart(parent, prefix, sprite, new Vector3((leftX + rightX) * 0.5f, y, 0f), reference, tint, sortingOrder, cell);
            return;
        }

        CreateFramePart(parent, prefix + "_L", sprite, new Vector3(leftX, y, 0f), reference, tint, sortingOrder, cell);
        CreateFramePart(parent, prefix + "_R", sprite, new Vector3(rightX, y, 0f), reference, tint, sortingOrder, cell);
    }

    private void CreateVerticalFaceHandles(
        Transform parent,
        string prefix,
        Sprite sprite,
        float x,
        float bottomY,
        float topY,
        SpriteRenderer reference,
        Color tint,
        int sortingOrder,
        float cell)
    {
        if (sprite == null)
            return;

        if (Mathf.Abs(topY - bottomY) < cell * 0.5f)
        {
            CreateFramePart(parent, prefix, sprite, new Vector3(x, (bottomY + topY) * 0.5f, 0f), reference, tint, sortingOrder, cell);
            return;
        }

        CreateFramePart(parent, prefix + "_B", sprite, new Vector3(x, bottomY, 0f), reference, tint, sortingOrder, cell);
        CreateFramePart(parent, prefix + "_T", sprite, new Vector3(x, topY, 0f), reference, tint, sortingOrder, cell);
    }

    private static void ScaleRendererToCell(SpriteRenderer renderer, float cell)
    {
        if (renderer == null || renderer.sprite == null)
            return;

        Vector3 size = renderer.sprite.bounds.size;
        float sx = cell / Mathf.Max(0.001f, size.x);
        float sy = cell / Mathf.Max(0.001f, size.y);
        renderer.transform.localScale = new Vector3(sx, sy, 1f);
    }

    private static Sprite ResolveLowerPlateSprite(BattleShowFloorTemplateSO template, int x, int columns)
    {
        if (template == null)
            return null;

        if (columns <= 1)
            return template.LowerPlateCenterSprite32 != null
                ? template.LowerPlateCenterSprite32
                : template.LowerPlateLeftSprite32 != null
                    ? template.LowerPlateLeftSprite32
                    : template.LowerPlateRightSprite32;

        if (x <= 0)
            return template.LowerPlateLeftSprite32 != null
                ? template.LowerPlateLeftSprite32
                : template.LowerPlateCenterSprite32;

        if (x >= columns - 1)
            return template.LowerPlateRightSprite32 != null
                ? template.LowerPlateRightSprite32
                : template.LowerPlateCenterSprite32;

        return template.LowerPlateCenterSprite32;
    }

    private static Sprite PickRandomSprite(Sprite[] sprites)
    {
        if (sprites == null || sprites.Length == 0)
            return null;

        int start = UnityEngine.Random.Range(0, sprites.Length);
        for (int i = 0; i < sprites.Length; i++)
        {
            Sprite sprite = sprites[(start + i) % sprites.Length];
            if (sprite != null)
                return sprite;
        }
        return null;
    }

    private static bool HasAnySprite(Sprite[] sprites)
    {
        if (sprites == null)
            return false;
        for (int i = 0; i < sprites.Length; i++)
            if (sprites[i] != null)
                return true;
        return false;
    }

    private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
            return false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return found;
    }

    private static Transform FindDirectChildByPrefix(Transform parent, string prefix)
    {
        if (parent == null)
            return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name.StartsWith(prefix, StringComparison.Ordinal))
                return child;
        }
        return null;
    }

    private static void RemoveExistingFrame(Transform visual)
    {
        if (visual == null)
            return;
        Transform old = visual.Find(FrameRootName);
        if (old == null)
            return;

        old.name = FrameRootName + "_Removing";
        old.gameObject.SetActive(false);
        Destroy(old.gameObject);
    }

    private static void ParseTileCoordinate(string objectName, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (string.IsNullOrEmpty(objectName) || !objectName.StartsWith(CarrierTilePrefix, StringComparison.Ordinal))
            return;

        string suffix = objectName.Substring(CarrierTilePrefix.Length);
        string[] split = suffix.Split('_');
        if (split.Length > 0)
            int.TryParse(split[0], out x);
        if (split.Length > 1)
            int.TryParse(split[1], out y);
    }

    private struct CarrierGeometry
    {
        public Bounds localBounds;
        public float cell;
        public int columns;
        public int rows;
    }
}
