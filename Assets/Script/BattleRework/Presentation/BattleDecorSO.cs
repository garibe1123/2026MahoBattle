using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
#endif

public enum BattleDecorAttachSide
{
    Auto = 0,
    Top = 1,
    Side = 2,
    Bottom = 3
}

/// <summary>
/// 하나의 완성된 전투 데코 세트를 저장합니다.
/// Side 디자인은 항상 Field 왼쪽 설치 + 장비가 오른쪽을 바라보는 모습을 원본으로 저장합니다.
/// Runtime에서 오른쪽 Side가 선택되면 authored Parts가 자동으로 좌우 Mirror됩니다.
/// </summary>
[CreateAssetMenu(
    fileName = "BattleDecor_New",
    menuName = "MahoBattle/Presentation/Battle Decor",
    order = 30)]
public sealed class BattleDecorSO : ScriptableObject
{
    [Header("CARRIER")]
    [Tooltip("이 데코가 점유하는 Floor 크기입니다. 1~3칸 범위에서 사용합니다.")]
    [SerializeField] private Vector2Int footprint = new(2, 2);
    [Tooltip("이 데코 전용 바닥/하판/핸들 디자인입니다. 비어 있으면 현재 Field Floor를 그대로 사용합니다.")]
    [SerializeField] private BattleShowFloorTemplateSO floorTemplate;
    [Tooltip("랜덤 선택 가중치입니다.")]
    [SerializeField, Min(1)] private int weight = 1;

    [FormerlySerializedAs("allowRandomMirrorX")]
    [SerializeField, HideInInspector] private bool legacyAllowRandomMirrorX;

    [Header("FIELD ATTACHMENT")]
    [Tooltip("Auto = 상/하/좌/우 랜덤. Top/Bottom = 해당 면 고정. Side = 좌/우 랜덤입니다. Side 원본은 항상 왼쪽 설치 기준입니다.")]
    [SerializeField] private BattleDecorAttachSide attachSide = BattleDecorAttachSide.Auto;
    [Tooltip("체크하면 선택된 면의 특정 지점에 붙습니다. 해제하면 해당 면 안에서 위치를 랜덤 선택합니다.")]
    [SerializeField] private bool useFixedAttachPosition;
    [Tooltip("Top/Bottom은 0=왼쪽, 1=오른쪽. Side는 0=아래, 1=위입니다.")]
    [SerializeField, Range(0f, 1f)] private float attachPosition01 = 0.5f;
    [Tooltip("선택된 Floor 부착 위치에서 추가로 움직일 보정값입니다. 타일 크기 단위입니다. 오른쪽 Side에서는 X도 자동 Mirror됩니다.")]
    [SerializeField] private Vector2 attachOffsetTiles;

    [Header("DESIGN PARTS")]
    [Tooltip("Camera / Light Base / Light Head / Cable 등을 여기서 직접 조립합니다. Side는 반드시 왼쪽 설치 기준으로 배치합니다.")]
    [SerializeField] private List<BattleDecorPart> parts = new();

    [Header("EDITOR PREVIEW")]
    [Tooltip("Preview에서 Drag할 때 Position이 맞춰지는 단위입니다.")]
    [SerializeField, Min(0.01f)] private float editorPositionSnap = 0.1f;
    [Tooltip("Preview에서 Carrier 바깥을 추가로 보여주는 여백입니다.")]
    [SerializeField, Range(0.25f, 2f)] private float editorPreviewMargin = 2f;

    public Vector2Int Footprint => new(
        Mathf.Clamp(footprint.x, 1, 3),
        Mathf.Clamp(footprint.y, 1, 3));
    public BattleShowFloorTemplateSO FloorTemplate => floorTemplate;
    public int Weight => Mathf.Max(1, weight);
    public BattleDecorAttachSide AttachSide => NormalizeAttachSide(attachSide);
    public bool UseFixedAttachPosition => useFixedAttachPosition;
    public float AttachPosition01 => Mathf.Clamp01(attachPosition01);
    public Vector2 AttachOffsetTiles => attachOffsetTiles;
    public IReadOnlyList<BattleDecorPart> Parts => parts;
    public float EditorPositionSnap => Mathf.Max(0.01f, editorPositionSnap);
    public float EditorPreviewMargin => Mathf.Clamp(editorPreviewMargin, 0.25f, 2f);

    public bool HasVisual
    {
        get
        {
            if (parts == null)
                return false;

            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null && parts[i].Sprite != null)
                    return true;
            }

            return false;
        }
    }

    private static BattleDecorAttachSide NormalizeAttachSide(BattleDecorAttachSide value)
    {
        // 구 버전 Left = 4 Asset은 Side로 마이그레이션합니다.
        return (int)value == 4 ? BattleDecorAttachSide.Side : value;
    }

#if UNITY_EDITOR
    public int EditorAddPart(Sprite sprite, Vector2 localPosition)
    {
        if (sprite == null)
            return -1;

        parts ??= new List<BattleDecorPart>();
        BattleDecorPart part = new(sprite, localPosition);
        part.Validate();
        parts.Add(part);
        return parts.Count - 1;
    }

    public bool EditorRemovePartAt(int index)
    {
        if (parts == null || index < 0 || index >= parts.Count)
            return false;

        parts.RemoveAt(index);
        return true;
    }
#endif

    private void OnValidate()
    {
        footprint.x = Mathf.Clamp(footprint.x, 1, 3);
        footprint.y = Mathf.Clamp(footprint.y, 1, 3);
        weight = Mathf.Max(1, weight);
        attachSide = NormalizeAttachSide(attachSide);
        attachPosition01 = Mathf.Clamp01(attachPosition01);
        editorPositionSnap = Mathf.Max(0.01f, editorPositionSnap);
        editorPreviewMargin = Mathf.Clamp(editorPreviewMargin, 0.25f, 2f);

        if (parts == null)
            return;

        for (int i = 0; i < parts.Count; i++)
            parts[i]?.Validate();
    }
}

[Serializable]
public sealed class BattleDecorPart
{
    [SerializeField] private string label = "Decor Part";
    [SerializeField] private Sprite sprite;

    [Header("Transform")]
    [Tooltip("Carrier 중심 기준 Local Position입니다. BattleDecorSO Preview에서 클릭/드래그로 수정할 수 있습니다.")]
    [SerializeField] private Vector2 localPosition;
    [SerializeField] private Vector2 localScale = Vector2.one;
    [SerializeField] private float rotationDegrees;

    [Header("Render")]
    [Tooltip("Carrier Floor Sorting Order에 더해지는 값입니다.")]
    [SerializeField] private int sortingOffset = 4;
    [SerializeField] private Color tint = Color.white;
    [FormerlySerializedAs("flipX")]
    [Tooltip("이 Part만 추가로 좌우 반전합니다. Side 자동 Mirror와 별도로 적용됩니다.")]
    [SerializeField] private bool flip;

    [Header("OPTIONAL LIGHT 2D")]
    [Tooltip("체크하면 이 Part에 URP Light2D를 함께 생성합니다. 기본은 꺼짐입니다.")]
    [SerializeField] private bool addLight2D;
    [Tooltip("Part Pivot 기준 Light2D 위치입니다. 타일/Part 로컬 단위입니다.")]
    [SerializeField] private Vector2 lightLocalOffset;
    [Tooltip("Part 회전에 추가되는 Light 방향 각도입니다. 360도 광원에서는 체감되지 않습니다.")]
    [SerializeField] private float lightRotationDegrees;
    [SerializeField] private Color lightColor = Color.white;
    [SerializeField, Min(0f)] private float lightIntensity = 1f;
    [SerializeField, Min(0f)] private float lightInnerRadius = 0.15f;
    [SerializeField, Min(0.01f)] private float lightOuterRadius = 1.25f;
    [SerializeField, Range(0f, 360f)] private float lightInnerAngle = 360f;
    [SerializeField, Range(0f, 360f)] private float lightOuterAngle = 360f;
    [SerializeField, Range(0f, 1f)] private float lightFalloffIntensity = 0.5f;
    [Tooltip("2D Renderer Data의 Blend Style 인덱스입니다. 일반적으로 0을 사용합니다.")]
    [SerializeField, Range(0, 3)] private int lightBlendStyleIndex;
    [SerializeField] private int lightOrder;
    [Tooltip("ShadowCaster2D의 그림자를 받을 때만 켜세요. 비용이 있으므로 기본은 꺼짐입니다.")]
    [SerializeField] private bool lightShadowsEnabled;
    [SerializeField, Range(0f, 1f)] private float lightShadowIntensity = 0.75f;

    public BattleDecorPart()
    {
    }

    public BattleDecorPart(Sprite sourceSprite, Vector2 position)
    {
        sprite = sourceSprite;
        localPosition = position;
        label = sourceSprite != null ? sourceSprite.name : "Decor Part";
        localScale = Vector2.one;
        sortingOffset = 4;
        tint = Color.white;
    }

    public string Label => string.IsNullOrWhiteSpace(label) ? "Decor Part" : label;
    public Sprite Sprite => sprite;
    public Vector2 LocalPosition => localPosition;
    public Vector2 LocalScale => new(
        Mathf.Approximately(localScale.x, 0f) ? 1f : Mathf.Abs(localScale.x),
        Mathf.Approximately(localScale.y, 0f) ? 1f : Mathf.Abs(localScale.y));
    public float RotationDegrees => rotationDegrees;
    public int SortingOffset => sortingOffset;
    public Color Tint => IsLegacyUnsetTint(tint) ? Color.white : tint;
    public bool Flip => flip;
    public bool FlipX => flip;

    public bool AddLight2D => addLight2D;
    public Vector2 LightLocalOffset => lightLocalOffset;
    public float LightRotationDegrees => lightRotationDegrees;
    public Color LightColor => lightColor;
    public float LightIntensity => Mathf.Max(0f, lightIntensity);
    public float LightInnerRadius => Mathf.Clamp(lightInnerRadius, 0f, LightOuterRadius);
    public float LightOuterRadius => Mathf.Max(0.01f, lightOuterRadius);
    public float LightInnerAngle => Mathf.Clamp(lightInnerAngle, 0f, LightOuterAngle);
    public float LightOuterAngle => Mathf.Clamp(lightOuterAngle, 0f, 360f);
    public float LightFalloffIntensity => Mathf.Clamp01(lightFalloffIntensity);
    public int LightBlendStyleIndex => Mathf.Clamp(lightBlendStyleIndex, 0, 3);
    public int LightOrder => lightOrder;
    public bool LightShadowsEnabled => lightShadowsEnabled;
    public float LightShadowIntensity => Mathf.Clamp01(lightShadowIntensity);

    public void SetLocalPosition(Vector2 value)
    {
        localPosition = value;
    }

    public void Validate()
    {
        localScale.x = Mathf.Approximately(localScale.x, 0f) ? 1f : Mathf.Abs(localScale.x);
        localScale.y = Mathf.Approximately(localScale.y, 0f) ? 1f : Mathf.Abs(localScale.y);

        if (IsLegacyUnsetTint(tint))
            tint = Color.white;

        rotationDegrees = NormalizeAngle(rotationDegrees);
        lightRotationDegrees = NormalizeAngle(lightRotationDegrees);
        lightIntensity = Mathf.Max(0f, lightIntensity);
        lightOuterRadius = Mathf.Max(0.01f, lightOuterRadius);
        lightInnerRadius = Mathf.Clamp(lightInnerRadius, 0f, lightOuterRadius);
        lightOuterAngle = Mathf.Clamp(lightOuterAngle, 0f, 360f);
        lightInnerAngle = Mathf.Clamp(lightInnerAngle, 0f, lightOuterAngle);
        lightFalloffIntensity = Mathf.Clamp01(lightFalloffIntensity);
        lightBlendStyleIndex = Mathf.Clamp(lightBlendStyleIndex, 0, 3);
        lightShadowIntensity = Mathf.Clamp01(lightShadowIntensity);
    }

    private static float NormalizeAngle(float value)
    {
        return Mathf.Repeat(value + 180f, 360f) - 180f;
    }

    private static bool IsLegacyUnsetTint(Color color)
    {
        return Mathf.Approximately(color.r, 0f) &&
               Mathf.Approximately(color.g, 0f) &&
               Mathf.Approximately(color.b, 0f) &&
               Mathf.Approximately(color.a, 0f);
    }
}

#if UNITY_EDITOR
/// <summary>
/// BattleDecorSO를 작은 2D Prefab Editor처럼 사용하기 위한 Inspector Preview입니다.
/// Sprite Drop, Click/Drag, Shift Multi Select, Delete를 지원합니다.
/// </summary>
[CustomEditor(typeof(BattleDecorSO))]
public sealed class BattleDecorSOEditor : Editor
{
    private const float PreviewHeight = 390f;
    private const float HeaderHeight = 24f;

    private readonly HashSet<int> selectedPartIndices = new();
    private readonly Dictionary<int, Vector2> dragStartPositions = new();

    private int selectedPartIndex = -1;
    private bool dragging;
    private bool dragUndoRecorded;
    private int previewControlId;
    private Vector2 dragStartLocalMouse;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("BATTLE DECOR PREFAB PREVIEW", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Sprite Drop = Add / Click·Drag = Move / Shift+Click = Multi Select / Delete = Remove. " +
            "Side는 왼쪽 설치 + 오른쪽을 바라보는 원본으로 편집합니다. Light2D가 켜진 Part는 청록색 원으로 범위를 표시합니다.",
            MessageType.Info);

        Rect previewRect = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
        DrawPreview(previewRect);
    }

    private void DrawPreview(Rect rect)
    {
        BattleDecorSO decor = (BattleDecorSO)target;
        PruneSelection(decor.Parts?.Count ?? 0);

        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

        Rect header = new(rect.x + 8f, rect.y + 4f, rect.width - 16f, HeaderHeight);
        int selectionCount = selectedPartIndices.Count;
        string sideLabel = decor.AttachSide == BattleDecorAttachSide.Side ? "   |   Side Basis = Left" : string.Empty;
        string headerText;

        if (selectionCount > 1)
            headerText = $"Selected: {selectionCount} Parts   |   Drag = Move Group   |   Delete = Remove   |   Snap {decor.EditorPositionSnap:0.###}{sideLabel}";
        else if (selectedPartIndex >= 0 && selectedPartIndex < decor.Parts.Count)
            headerText = $"Selected: {decor.Parts[selectedPartIndex].Label}   |   Drag = Move   |   Delete = Remove   |   Snap {decor.EditorPositionSnap:0.###}{sideLabel}";
        else
            headerText = $"Drop Sprite = Add   |   Click = Select   |   Shift+Click = Multi Select   |   Snap {decor.EditorPositionSnap:0.###}{sideLabel}";

        GUI.Label(header, headerText, EditorStyles.miniBoldLabel);

        Rect canvas = new(rect.x + 8f, rect.y + HeaderHeight + 6f, rect.width - 16f, rect.height - HeaderHeight - 14f);
        EditorGUI.DrawRect(canvas, new Color(0.055f, 0.06f, 0.07f, 1f));

        Vector2Int footprint = decor.Footprint;
        float margin = decor.EditorPreviewMargin;
        float worldWidth = footprint.x + margin * 2f;
        float worldHeight = footprint.y + margin * 2f;
        float zoom = Mathf.Min(canvas.width / Mathf.Max(0.01f, worldWidth), canvas.height / Mathf.Max(0.01f, worldHeight));
        Vector2 center = canvas.center;

        DrawGridAndFloor(decor, center, zoom);
        DrawParts(decor, center, zoom);
        DrawAttachmentGuide(decor, center, zoom);
        HandleInput(decor, canvas, center, zoom);

        EditorGUI.DrawRect(new Rect(canvas.x, canvas.y, canvas.width, 1f), new Color(1f, 1f, 1f, 0.15f));
        EditorGUI.DrawRect(new Rect(canvas.x, canvas.yMax - 1f, canvas.width, 1f), new Color(1f, 1f, 1f, 0.15f));
        EditorGUI.DrawRect(new Rect(canvas.x, canvas.y, 1f, canvas.height), new Color(1f, 1f, 1f, 0.15f));
        EditorGUI.DrawRect(new Rect(canvas.xMax - 1f, canvas.y, 1f, canvas.height), new Color(1f, 1f, 1f, 0.15f));
    }

    private static void DrawGridAndFloor(BattleDecorSO decor, Vector2 center, float zoom)
    {
        Vector2Int footprint = decor.Footprint;
        float x0 = -(footprint.x - 1) * 0.5f;
        float y0 = -(footprint.y - 1) * 0.5f;
        Sprite floorSprite = ResolvePreviewFloorSprite(decor.FloorTemplate);

        for (int y = 0; y < footprint.y; y++)
        {
            for (int x = 0; x < footprint.x; x++)
            {
                Vector2 worldCenter = new(x0 + x, y0 + y);
                Rect cell = WorldRectToGui(
                    new Rect(worldCenter.x - 0.5f, worldCenter.y - 0.5f, 1f, 1f),
                    center,
                    zoom);

                if (floorSprite != null)
                    DrawSpriteInRect(floorSprite, cell, false, Color.white);
                else
                    EditorGUI.DrawRect(cell, new Color(0.10f, 0.115f, 0.13f, 1f));

                Handles.BeginGUI();
                Handles.color = new Color(1f, 1f, 1f, 0.18f);
                Handles.DrawAAPolyLine(1f,
                    new Vector3(cell.xMin, cell.yMin),
                    new Vector3(cell.xMax, cell.yMin),
                    new Vector3(cell.xMax, cell.yMax),
                    new Vector3(cell.xMin, cell.yMax),
                    new Vector3(cell.xMin, cell.yMin));
                Handles.EndGUI();
            }
        }

        Rect carrierRect = WorldRectToGui(
            new Rect(-footprint.x * 0.5f, -footprint.y * 0.5f, footprint.x, footprint.y),
            center,
            zoom);

        Handles.BeginGUI();
        Handles.color = new Color(0.15f, 0.90f, 0.95f, 0.85f);
        Handles.DrawAAPolyLine(2f,
            new Vector3(carrierRect.xMin, carrierRect.yMin),
            new Vector3(carrierRect.xMax, carrierRect.yMin),
            new Vector3(carrierRect.xMax, carrierRect.yMax),
            new Vector3(carrierRect.xMin, carrierRect.yMax),
            new Vector3(carrierRect.xMin, carrierRect.yMin));
        Handles.EndGUI();
    }

    private void DrawParts(BattleDecorSO decor, Vector2 center, float zoom)
    {
        IReadOnlyList<BattleDecorPart> parts = decor.Parts;
        if (parts == null)
            return;

        for (int i = 0; i < parts.Count; i++)
        {
            BattleDecorPart part = parts[i];
            if (part == null || part.Sprite == null)
                continue;

            Rect drawRect = ResolvePartGuiRect(part, center, zoom);
            Vector2 pivot = drawRect.center;
            Matrix4x4 oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(-part.RotationDegrees, pivot);
            DrawSpriteInRect(part.Sprite, drawRect, part.Flip, part.Tint);
            GUI.matrix = oldMatrix;

            if (part.AddLight2D)
                DrawLightGuide(part, center, zoom);

            if (!selectedPartIndices.Contains(i))
                continue;

            Handles.BeginGUI();
            Handles.color = i == selectedPartIndex
                ? new Color(1f, 0.80f, 0.08f, 1f)
                : new Color(1f, 0.55f, 0.08f, 0.90f);
            Handles.DrawAAPolyLine(i == selectedPartIndex ? 2.5f : 2f,
                new Vector3(drawRect.xMin, drawRect.yMin),
                new Vector3(drawRect.xMax, drawRect.yMin),
                new Vector3(drawRect.xMax, drawRect.yMax),
                new Vector3(drawRect.xMin, drawRect.yMax),
                new Vector3(drawRect.xMin, drawRect.yMin));
            Handles.EndGUI();

            if (i == selectedPartIndex)
            {
                string coord = $"({part.LocalPosition.x:0.###}, {part.LocalPosition.y:0.###})";
                Rect coordRect = new(drawRect.x, drawRect.y - 18f, Mathf.Max(90f, drawRect.width), 18f);
                GUI.Label(coordRect, coord, EditorStyles.whiteMiniLabel);
            }
        }
    }

    private static void DrawLightGuide(BattleDecorPart part, Vector2 center, float zoom)
    {
        Vector2 scale = part.LocalScale;
        Vector2 offset = new(part.LightLocalOffset.x * scale.x, part.LightLocalOffset.y * scale.y);
        float radians = part.RotationDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        Vector2 rotatedOffset = new(
            offset.x * cos - offset.y * sin,
            offset.x * sin + offset.y * cos);
        Vector2 world = part.LocalPosition + rotatedOffset;
        Vector2 gui = new(center.x + world.x * zoom, center.y - world.y * zoom);
        float radius = part.LightOuterRadius * zoom * Mathf.Max(scale.x, scale.y);

        Handles.BeginGUI();
        Handles.color = new Color(0.20f, 0.95f, 0.90f, 0.55f);
        Handles.DrawWireDisc(gui, Vector3.forward, Mathf.Max(2f, radius));
        Handles.DrawSolidDisc(gui, Vector3.forward, 2.5f);

        float directionDegrees = part.RotationDegrees + part.LightRotationDegrees;
        float directionRadians = directionDegrees * Mathf.Deg2Rad;
        Vector2 direction = new(Mathf.Cos(directionRadians), -Mathf.Sin(directionRadians));
        Handles.DrawAAPolyLine(1.5f, gui, gui + direction * Mathf.Min(radius, 30f));
        Handles.EndGUI();
    }

    private static void DrawAttachmentGuide(BattleDecorSO decor, Vector2 center, float zoom)
    {
        if (decor.AttachSide == BattleDecorAttachSide.Auto)
            return;

        Vector2Int footprint = decor.Footprint;
        float halfWidth = footprint.x * 0.5f;
        float halfHeight = footprint.y * 0.5f;
        float t = decor.UseFixedAttachPosition ? decor.AttachPosition01 : 0.5f;
        Vector2 point;
        Vector2 outward;

        switch (decor.AttachSide)
        {
            case BattleDecorAttachSide.Top:
                point = new Vector2(Mathf.Lerp(-halfWidth, halfWidth, t), halfHeight);
                outward = Vector2.up;
                break;
            case BattleDecorAttachSide.Bottom:
                point = new Vector2(Mathf.Lerp(-halfWidth, halfWidth, t), -halfHeight);
                outward = Vector2.down;
                break;
            default:
                point = new Vector2(-halfWidth, Mathf.Lerp(-halfHeight, halfHeight, t));
                outward = Vector2.left;
                break;
        }

        Vector2 guiPoint = new(center.x + point.x * zoom, center.y - point.y * zoom);
        Vector2 guiOutward = new(outward.x, -outward.y);

        Handles.BeginGUI();
        Handles.color = new Color(1f, 0.35f, 0.85f, 1f);
        Handles.DrawAAPolyLine(3f, guiPoint, guiPoint + guiOutward * 20f);
        Handles.DrawSolidDisc(guiPoint, Vector3.forward, 4f);
        Handles.EndGUI();
    }

    private void HandleInput(BattleDecorSO decor, Rect canvas, Vector2 center, float zoom)
    {
        Event e = Event.current;
        previewControlId = GUIUtility.GetControlID("BattleDecorPreview".GetHashCode(), FocusType.Keyboard, canvas);
        EditorGUIUtility.AddCursorRect(canvas, dragging ? MouseCursor.Pan : MouseCursor.MoveArrow);

        if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) &&
            canvas.Contains(e.mousePosition) &&
            TryGetDraggedSprites(out List<Sprite> draggedSprites))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                Vector2 dropPosition = GuiPointToLocalPosition(e.mousePosition, center, zoom, decor.EditorPositionSnap);
                Undo.RecordObject(decor, "Add Battle Decor Part");
                selectedPartIndices.Clear();

                int lastAdded = -1;
                for (int i = 0; i < draggedSprites.Count; i++)
                {
                    Vector2 position = dropPosition + new Vector2(i * decor.EditorPositionSnap, 0f);
                    int added = decor.EditorAddPart(draggedSprites[i], position);
                    if (added < 0)
                        continue;

                    selectedPartIndices.Add(added);
                    lastAdded = added;
                }

                if (lastAdded >= 0)
                {
                    selectedPartIndex = lastAdded;
                    GUIUtility.keyboardControl = previewControlId;
                    EditorUtility.SetDirty(decor);
                    serializedObject.Update();
                }
            }

            e.Use();
            Repaint();
            return;
        }

        if (e.type == EventType.KeyDown &&
            GUIUtility.keyboardControl == previewControlId &&
            selectedPartIndices.Count > 0 &&
            (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
        {
            Undo.RecordObject(decor, selectedPartIndices.Count > 1
                ? "Delete Battle Decor Parts"
                : "Delete Battle Decor Part");

            List<int> indices = new(selectedPartIndices);
            indices.Sort((a, b) => b.CompareTo(a));
            for (int i = 0; i < indices.Count; i++)
                decor.EditorRemovePartAt(indices[i]);

            selectedPartIndices.Clear();
            selectedPartIndex = -1;
            dragging = false;
            dragStartPositions.Clear();
            EditorUtility.SetDirty(decor);
            serializedObject.Update();

            e.Use();
            Repaint();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 0 && canvas.Contains(e.mousePosition))
        {
            int hitIndex = FindPartAtPoint(decor, e.mousePosition, center, zoom);

            if (e.shift)
            {
                if (hitIndex >= 0)
                {
                    if (selectedPartIndices.Contains(hitIndex))
                    {
                        selectedPartIndices.Remove(hitIndex);
                        if (selectedPartIndex == hitIndex)
                            selectedPartIndex = ResolvePrimarySelection();
                    }
                    else
                    {
                        selectedPartIndices.Add(hitIndex);
                        selectedPartIndex = hitIndex;
                    }
                }
            }
            else if (hitIndex < 0)
            {
                selectedPartIndices.Clear();
                selectedPartIndex = -1;
            }
            else if (!selectedPartIndices.Contains(hitIndex))
            {
                selectedPartIndices.Clear();
                selectedPartIndices.Add(hitIndex);
                selectedPartIndex = hitIndex;
            }
            else
            {
                selectedPartIndex = hitIndex;
            }

            GUIUtility.keyboardControl = previewControlId;
            dragging = hitIndex >= 0 && selectedPartIndices.Contains(hitIndex);
            dragUndoRecorded = false;

            if (dragging)
            {
                CaptureDragStart(decor, e.mousePosition, center, zoom);
                GUIUtility.hotControl = previewControlId;
            }
            else
            {
                dragStartPositions.Clear();
            }

            e.Use();
            Repaint();
            return;
        }

        if (e.type == EventType.MouseDrag && e.button == 0 && dragging &&
            GUIUtility.hotControl == previewControlId && selectedPartIndices.Count > 0)
        {
            if (!dragUndoRecorded)
            {
                Undo.RecordObject(decor, selectedPartIndices.Count > 1
                    ? "Move Battle Decor Parts"
                    : "Move Battle Decor Part");
                dragUndoRecorded = true;
            }

            MoveSelectedParts(decor, e.mousePosition, center, zoom);
            EditorUtility.SetDirty(decor);
            e.Use();
            Repaint();
            return;
        }

        if (e.type == EventType.MouseUp && e.button == 0 && dragging)
        {
            dragging = false;
            dragUndoRecorded = false;
            dragStartPositions.Clear();
            if (GUIUtility.hotControl == previewControlId)
                GUIUtility.hotControl = 0;
            e.Use();
            Repaint();
        }
    }

    private void CaptureDragStart(BattleDecorSO decor, Vector2 mousePosition, Vector2 center, float zoom)
    {
        dragStartPositions.Clear();
        dragStartLocalMouse = GuiPointToLocalPositionUnsnapped(mousePosition, center, zoom);

        foreach (int index in selectedPartIndices)
        {
            if (index < 0 || index >= decor.Parts.Count)
                continue;

            BattleDecorPart part = decor.Parts[index];
            if (part != null)
                dragStartPositions[index] = part.LocalPosition;
        }
    }

    private void MoveSelectedParts(BattleDecorSO decor, Vector2 mousePosition, Vector2 center, float zoom)
    {
        if (dragStartPositions.Count == 0)
            return;

        Vector2 currentMouse = GuiPointToLocalPositionUnsnapped(mousePosition, center, zoom);
        Vector2 delta = currentMouse - dragStartLocalMouse;
        float snap = decor.EditorPositionSnap;
        delta.x = Mathf.Round(delta.x / snap) * snap;
        delta.y = Mathf.Round(delta.y / snap) * snap;

        foreach (KeyValuePair<int, Vector2> entry in dragStartPositions)
        {
            int index = entry.Key;
            if (index < 0 || index >= decor.Parts.Count)
                continue;

            BattleDecorPart part = decor.Parts[index];
            part?.SetLocalPosition(entry.Value + delta);
        }
    }

    private void PruneSelection(int partCount)
    {
        selectedPartIndices.RemoveWhere(index => index < 0 || index >= partCount);

        if (selectedPartIndex < 0 ||
            selectedPartIndex >= partCount ||
            !selectedPartIndices.Contains(selectedPartIndex))
            selectedPartIndex = ResolvePrimarySelection();
    }

    private int ResolvePrimarySelection()
    {
        foreach (int index in selectedPartIndices)
            return index;
        return -1;
    }

    private static bool TryGetDraggedSprites(out List<Sprite> sprites)
    {
        sprites = new List<Sprite>();
        UnityEngine.Object[] references = DragAndDrop.objectReferences;
        if (references == null || references.Length == 0)
            return false;

        for (int i = 0; i < references.Length; i++)
        {
            if (references[i] is Sprite sprite && sprite != null)
                sprites.Add(sprite);
        }

        return sprites.Count > 0;
    }

    private static Vector2 GuiPointToLocalPosition(Vector2 guiPoint, Vector2 center, float zoom, float snap)
    {
        Vector2 local = GuiPointToLocalPositionUnsnapped(guiPoint, center, zoom);
        float safeSnap = Mathf.Max(0.01f, snap);
        local.x = Mathf.Round(local.x / safeSnap) * safeSnap;
        local.y = Mathf.Round(local.y / safeSnap) * safeSnap;
        return local;
    }

    private static Vector2 GuiPointToLocalPositionUnsnapped(Vector2 guiPoint, Vector2 center, float zoom)
    {
        float safeZoom = Mathf.Max(0.0001f, zoom);
        return new Vector2(
            (guiPoint.x - center.x) / safeZoom,
            -(guiPoint.y - center.y) / safeZoom);
    }

    private int FindPartAtPoint(BattleDecorSO decor, Vector2 mouse, Vector2 center, float zoom)
    {
        IReadOnlyList<BattleDecorPart> parts = decor.Parts;
        if (parts == null)
            return -1;

        for (int i = parts.Count - 1; i >= 0; i--)
        {
            BattleDecorPart part = parts[i];
            if (part == null || part.Sprite == null)
                continue;

            if (ResolvePartGuiRect(part, center, zoom).Contains(mouse))
                return i;
        }

        return -1;
    }

    private static Rect ResolvePartGuiRect(BattleDecorPart part, Vector2 center, float zoom)
    {
        Vector3 bounds = part.Sprite.bounds.size;
        Vector2 scale = part.LocalScale;
        float width = Mathf.Max(0.02f, Mathf.Abs(bounds.x * scale.x));
        float height = Mathf.Max(0.02f, Mathf.Abs(bounds.y * scale.y));
        Rect worldRect = new(
            part.LocalPosition.x - width * 0.5f,
            part.LocalPosition.y - height * 0.5f,
            width,
            height);
        return WorldRectToGui(worldRect, center, zoom);
    }

    private static Rect WorldRectToGui(Rect worldRect, Vector2 center, float zoom)
    {
        float x = center.x + worldRect.xMin * zoom;
        float y = center.y - worldRect.yMax * zoom;
        return new Rect(x, y, worldRect.width * zoom, worldRect.height * zoom);
    }

    private static Sprite ResolvePreviewFloorSprite(BattleShowFloorTemplateSO template)
    {
        Sprite[] variants = template != null ? template.FloorVariants : null;
        if (variants == null)
            return null;

        for (int i = 0; i < variants.Length; i++)
        {
            if (variants[i] != null)
                return variants[i];
        }

        return null;
    }

    private static void DrawSpriteInRect(Sprite sprite, Rect rect, bool flipX, Color tint)
    {
        if (sprite == null || sprite.texture == null)
            return;

        Rect textureRect = sprite.textureRect;
        Texture texture = sprite.texture;
        Rect uv = new(
            textureRect.x / texture.width,
            textureRect.y / texture.height,
            textureRect.width / texture.width,
            textureRect.height / texture.height);

        if (flipX)
        {
            uv.x += uv.width;
            uv.width = -uv.width;
        }

        Color old = GUI.color;
        GUI.color = tint;
        GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
        GUI.color = old;
    }
}
#endif
