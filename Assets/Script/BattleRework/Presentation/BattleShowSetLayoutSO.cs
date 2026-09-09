using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class BattleShowTileObjectData
{
    [SerializeField] private string label = "Tile Object";
    [SerializeField] private Sprite sprite;
    [SerializeField] private Vector2Int gridPosition;
    [SerializeField] private Vector2 localOffset;
    [SerializeField, Range(0, 3)] private int quarterTurns;
    [SerializeField] private bool flipX;
    [SerializeField] private bool flipY;
    [SerializeField] private Vector2 scale = Vector2.one;
    [SerializeField] private Color tint = Color.white;
    [SerializeField] private int sortingOrder = -10;
    [SerializeField] private bool visibleInReward = true;
    [SerializeField] private bool visibleInMap = true;

    public string Label { get => label; set => label = string.IsNullOrWhiteSpace(value) ? "Tile Object" : value; }
    public Sprite Sprite { get => sprite; set => sprite = value; }
    public Vector2Int GridPosition { get => gridPosition; set => gridPosition = value; }
    public Vector2 LocalOffset { get => localOffset; set => localOffset = value; }
    public int QuarterTurns { get => quarterTurns; set => quarterTurns = ((value % 4) + 4) % 4; }
    public bool FlipX { get => flipX; set => flipX = value; }
    public bool FlipY { get => flipY; set => flipY = value; }
    public Vector2 Scale
    {
        get => scale;
        set => scale = new Vector2(
            Mathf.Approximately(value.x, 0f) ? 1f : value.x,
            Mathf.Approximately(value.y, 0f) ? 1f : value.y);
    }
    public Color Tint { get => tint; set => tint = value; }
    public int SortingOrder { get => sortingOrder; set => sortingOrder = value; }
    public bool VisibleInReward { get => visibleInReward; set => visibleInReward = value; }
    public bool VisibleInMap { get => visibleInMap; set => visibleInMap = value; }

    public BattleShowTileObjectData Clone()
    {
        return new BattleShowTileObjectData
        {
            label = label,
            sprite = sprite,
            gridPosition = gridPosition + Vector2Int.right,
            localOffset = localOffset,
            quarterTurns = quarterTurns,
            flipX = flipX,
            flipY = flipY,
            scale = scale,
            tint = tint,
            sortingOrder = sortingOrder,
            visibleInReward = visibleInReward,
            visibleInMap = visibleInMap
        };
    }

    public void Normalize()
    {
        quarterTurns = ((quarterTurns % 4) + 4) % 4;
        if (Mathf.Approximately(scale.x, 0f)) scale.x = 1f;
        if (Mathf.Approximately(scale.y, 0f)) scale.y = 1f;
        if (string.IsNullOrWhiteSpace(label)) label = "Tile Object";
    }
}

/// <summary>
/// Reward / Map의 TV 쇼 세트 전용 아트와 외곽 치장 타일 배치를 보관합니다.
///
/// 기존 BattleShowFloorTemplateSO는 전투/굴러오는 MapBlock의 기계식 바닥 템플릿이고,
/// 이 SO는 쇼 세트에서만 사용하는 사회자, 쇼 전용 바닥, 비게임플레이 장식 타일을 담당합니다.
/// Tile Objects는 충돌/네비/전투 타일을 만들지 않고 SpriteRenderer 장식만 생성합니다.
/// </summary>
[CreateAssetMenu(
    fileName = "BattleShowSetLayout_Default",
    menuName = "Battle/Show/Show Set Layout",
    order = 20)]
public sealed class BattleShowSetLayoutSO : ScriptableObject
{
    [Header("사회자")]
    [Tooltip("애니메이션이 없거나 정지 상태일 때 사용할 사회자 Sprite입니다.")]
    [SerializeField] private Sprite presenterSprite;

    [Tooltip("Animator Controller를 사용한다면 지정합니다. 비어 있으면 아래 Sprite Frame 배열을 직접 재생합니다.")]
    [SerializeField] private RuntimeAnimatorController presenterAnimatorController;

    [Tooltip("Animator Controller가 없을 때 재생할 사회자 Sprite 프레임입니다.")]
    [SerializeField] private Sprite[] presenterAnimationFrames;

    [SerializeField, Min(1f)] private float presenterFps = 8f;
    [SerializeField] private bool presenterLoop = true;
    [SerializeField] private bool presenterFlipX;
    [SerializeField, Min(0.25f)] private float presenterWorldHeight = 3.6f;
    [SerializeField] private float presenterPadYOffset = 0.20f;
    [SerializeField, Range(0f, 1f)] private float presenterRightPadding = 0.15f;

    [Header("쇼 전용 바닥")]
    [Tooltip("Reward / Map의 Screen Carrier와 Presenter Carrier에만 사용하는 바닥 Sprite입니다. 비어 있으면 기존 BattleShowFloorTemplateSO 바닥을 유지합니다.")]
    [SerializeField] private Sprite[] showFloorSprites;
    [SerializeField] private Color showFloorTint = Color.white;

    [Header("Tile Obj Editor")]
    [Tooltip("한 칸의 월드 크기입니다. 현재 전투 타일 규격과 맞추려면 1을 사용합니다.")]
    [SerializeField, Min(0.125f)] private float cellWorldSize = 1f;

    [Tooltip("고정 4x4 Base의 좌하단 타일 중심을 (0,0)으로 잡은 뒤 전체 치장 타일 루트를 추가로 이동합니다.")]
    [SerializeField] private Vector2 worldOffset;

    [Tooltip("Tile Obj Editor에서 브러시로 사용할 Sprite 팔레트입니다. 팔레트 순서를 바꿔도 이미 배치된 오브젝트는 직접 Sprite 참조를 유지합니다.")]
    [SerializeField] private Sprite[] tilePalette;

    [Tooltip("Tile Obj Editor에서 배치한 외곽 치장 오브젝트입니다. 게임플레이 타일/Collider/NavMesh에는 관여하지 않습니다.")]
    [SerializeField] private List<BattleShowTileObjectData> tileObjects = new();

    public Sprite PresenterSprite => presenterSprite;
    public RuntimeAnimatorController PresenterAnimatorController => presenterAnimatorController;
    public Sprite[] PresenterAnimationFrames => presenterAnimationFrames;
    public float PresenterFps => presenterFps;
    public bool PresenterLoop => presenterLoop;
    public bool PresenterFlipX => presenterFlipX;
    public float PresenterWorldHeight => presenterWorldHeight;
    public float PresenterPadYOffset => presenterPadYOffset;
    public float PresenterRightPadding => presenterRightPadding;
    public Sprite[] ShowFloorSprites => showFloorSprites;
    public Color ShowFloorTint => showFloorTint;
    public float CellWorldSize => Mathf.Max(0.125f, cellWorldSize);
    public Vector2 WorldOffset => worldOffset;
    public Sprite[] TilePalette => tilePalette;
    public IReadOnlyList<BattleShowTileObjectData> TileObjects => tileObjects;

    public bool HasShowFloorSprites
    {
        get
        {
            if (showFloorSprites == null)
                return false;
            for (int i = 0; i < showFloorSprites.Length; i++)
                if (showFloorSprites[i] != null)
                    return true;
            return false;
        }
    }

    public Sprite GetRandomShowFloorSprite(Sprite fallback = null)
    {
        if (!HasShowFloorSprites)
            return fallback;

        int start = UnityEngine.Random.Range(0, showFloorSprites.Length);
        for (int i = 0; i < showFloorSprites.Length; i++)
        {
            Sprite candidate = showFloorSprites[(start + i) % showFloorSprites.Length];
            if (candidate != null)
                return candidate;
        }

        return fallback;
    }

#if UNITY_EDITOR
    public BattleShowTileObjectData AddTileObject(Sprite sprite, Vector2Int gridPosition)
    {
        tileObjects ??= new List<BattleShowTileObjectData>();
        BattleShowTileObjectData entry = new();
        entry.Sprite = sprite;
        entry.GridPosition = gridPosition;
        entry.Label = sprite != null ? sprite.name : "Tile Object";
        tileObjects.Add(entry);
        return entry;
    }

    public void RemoveTileObjectAt(int index)
    {
        if (tileObjects == null || index < 0 || index >= tileObjects.Count)
            return;
        tileObjects.RemoveAt(index);
    }

    public int DuplicateTileObjectAt(int index)
    {
        if (tileObjects == null || index < 0 || index >= tileObjects.Count)
            return -1;
        tileObjects.Add(tileObjects[index].Clone());
        return tileObjects.Count - 1;
    }

    public void ClearTileObjects()
    {
        tileObjects?.Clear();
    }
#endif

    private void OnValidate()
    {
        cellWorldSize = Mathf.Max(0.125f, cellWorldSize);
        presenterFps = Mathf.Max(1f, presenterFps);
        presenterWorldHeight = Mathf.Max(0.25f, presenterWorldHeight);
        presenterRightPadding = Mathf.Clamp01(presenterRightPadding);

        if (tileObjects == null)
            return;
        for (int i = 0; i < tileObjects.Count; i++)
            tileObjects[i]?.Normalize();
    }
}
