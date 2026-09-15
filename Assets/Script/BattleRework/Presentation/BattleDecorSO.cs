using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public enum BattleDecorAttachSide
{
    Auto = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
    Left = 4
}

/// <summary>
/// 하나의 완성된 전투 데코 세트를 저장합니다.
/// Runtime에서는 사실상 Prefab 설계도처럼 사용되며,
/// Floor + Frame + 여러 Sprite Part의 위치/회전/스케일/Sorting을 한 Asset 안에 저장합니다.
///
/// Cable / Camera / Light / Base 같은 의미 구분은 코드에서 하지 않습니다.
/// 어떤 Sprite를 어떤 위치/Sorting으로 둘지는 이 SO에서 완성된 디자인으로 직접 결정합니다.
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
    [Tooltip("런타임에서 세트 전체를 좌우 Mirror할 수 있습니다. 상하 반전/랜덤 180도 회전은 하지 않습니다.")]
    [SerializeField] private bool allowRandomMirrorX;

    [Header("FIELD ATTACHMENT")]
    [Tooltip("Decor Carrier가 Field 어느 면에 붙을지 지정합니다. Top은 Field 위쪽, Right는 오른쪽입니다. Auto는 기존처럼 면을 랜덤 선택합니다.")]
    [SerializeField] private BattleDecorAttachSide attachSide = BattleDecorAttachSide.Auto;
    [Tooltip("체크하면 선택된 면의 특정 지점에 붙습니다. 해제하면 해당 면의 실제 외곽 Floor 타일 중 하나를 랜덤 선택합니다.")]
    [SerializeField] private bool useFixedAttachPosition;
    [Tooltip("선택 면 안에서의 부착 지점입니다. Top/Bottom은 0=왼쪽, 1=오른쪽. Left/Right는 0=아래, 1=위입니다.")]
    [SerializeField, Range(0f, 1f)] private float attachPosition01 = 0.5f;
    [Tooltip("선택된 Floor 부착 위치에서 추가로 움직일 보정값입니다. 타일 크기 단위입니다.")]
    [SerializeField] private Vector2 attachOffsetTiles;

    [Header("DESIGN PARTS")]
    [Tooltip("Camera / Light Base / Light Head / Cable 등을 모두 여기서 직접 조립합니다. 리스트 순서는 Preview 선택 편의를 위한 것이고, 실제 앞뒤는 Sorting Offset이 결정합니다.")]
    [SerializeField] private List<BattleDecorPart> parts = new();

    [Header("EDITOR PREVIEW")]
    [Tooltip("Preview에서 Drag할 때 Position이 맞춰지는 단위입니다. 기본 0.1f입니다.")]
    [SerializeField, Min(0.01f)] private float editorPositionSnap = 0.1f;
    [Tooltip("Preview에서 Carrier 바깥을 추가로 보여주는 여백입니다.")]
    [SerializeField, Range(0.25f, 2f)] private float editorPreviewMargin = 0.75f;

    public Vector2Int Footprint => new(
        Mathf.Clamp(footprint.x, 1, 3),
        Mathf.Clamp(footprint.y, 1, 3));
    public BattleShowFloorTemplateSO FloorTemplate => floorTemplate;
    public int Weight => Mathf.Max(1, weight);
    public bool AllowRandomMirrorX => allowRandomMirrorX;
    public BattleDecorAttachSide AttachSide => attachSide;
    public bool UseFixedAttachPosition => useFixedAttachPosition;
    public float AttachPosition01 => Mathf.Clamp01(attachPosition01);
    public Vector2 AttachOffsetTiles => attachOffsetTiles;
    public IReadOnlyList<BattleDecorPart> Parts => parts;
    public float EditorPositionSnap => Mathf.Max(0.01f, editorPositionSnap);
    public float EditorPreviewMargin => Mathf.Max(0.25f, editorPreviewMargin);

    public bool HasVisual
    {
        get
        {
            if (parts == null)
                return false;
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] != null && parts[i].Sprite != null)
                    return true;
            return false;
        }
    }

    private void OnValidate()
    {
        footprint.x = Mathf.Clamp(footprint.x, 1, 3);
        footprint.y = Mathf.Clamp(footprint.y, 1, 3);
        weight = Mathf.Max(1, weight);
        attachPosition01 = Mathf.Clamp01(attachPosition01);
        editorPositionSnap = Mathf.Max(0.01f, editorPositionSnap);

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
    [Tooltip("Carrier Floor Sorting Order에 더해지는 값입니다. 예: Cable +1, Base +4, Camera/Light Head +6")]
    [SerializeField] private int sortingOffset = 4;
    [SerializeField] private Color tint = Color.white;
    [SerializeField] private bool flipX;

    public string Label => string.IsNullOrWhiteSpace(label) ? "Decor Part" : label;
    public Sprite Sprite => sprite;
    public Vector2 LocalPosition => localPosition;
    public Vector2 LocalScale => new(
        Mathf.Approximately(localScale.x, 0f) ? 1f : localScale.x,
        Mathf.Approximately(localScale.y, 0f) ? 1f : localScale.y);
    public float RotationDegrees => rotationDegrees;
    public int SortingOffset => sortingOffset;
    public Color Tint => IsLegacyUnsetTint(tint) ? Color.white : tint;
    public bool FlipX => flipX;

    public void SetLocalPosition(Vector2 value)
    {
        localPosition = value;
    }

    public void Validate()
    {
        if (Mathf.Approximately(localScale.x, 0f))
            localScale.x = 1f;
        if (Mathf.Approximately(localScale.y, 0f))
            localScale.y = 1f;
        if (IsLegacyUnsetTint(tint))
            tint = Color.white;
        rotationDegrees = Mathf.Repeat(rotationDegrees + 180f, 360f) - 180f;
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
/// Sprite Part를 Preview에서 직접 클릭/드래그하면 localPosition이 Asset에 저장됩니다.
/// </summary>
[CustomEditor(typeof(BattleDecorSO))]
public sealed class BattleDecorSOEditor : Editor
{
    private const float PreviewHeight = 390f;
    private const float HeaderHeight = 24f;
    private int selectedPartIndex = -1;
    private bool dragging;
    private int previewControlId;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("BATTLE DECOR PREFAB PREVIEW", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Preview 안의 Sprite를 클릭하고 드래그하면 Position이 BattleDecorSO에 바로 저장됩니다. " +
            "위치는 Editor Position Snap 단위로 맞춰집니다. Runtime에서는 이 배치를 그대로 사용합니다.",
            MessageType.Info);

        Rect previewRect = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
        DrawPreview(previewRect);
    }

    private void DrawPreview(Rect rect)
    {
        BattleDecorSO decor = (BattleDecorSO)target;
        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

        Rect header = new(rect.x + 8f, rect.y + 4f, rect.width - 16f, HeaderHeight);
        GUI.Label(
            header,
            selectedPartIndex >= 0 && selectedPartIndex < decor.Parts.Count
                ? $"Selected: {decor.Parts[selectedPartIndex].Label}   |   Drag = Move   |   Snap {decor.EditorPositionSnap:0.###}"
                : $"Click a part to move it   |   Snap {decor.EditorPositionSnap:0.###}",
            EditorStyles.miniBoldLabel);

        Rect canvas = new(rect.x + 8f, rect.y + HeaderHeight + 6f, rect.width - 16f, rect.height - HeaderHeight - 14f);
        EditorGUI.DrawRect(canvas, new Color(0.055f, 0.06f, 0.07f, 1f));

        Vector2Int footprint = decor.Footprint;
        float margin = decor.EditorPreviewMargin;
        float worldWidth = footprint.x + margin * 2f;
        float worldHeight = footprint.y + margin * 2f;
        float zoom = Mathf.Min(canvas.width / Mathf.Max(0.01f, worldWidth), canvas.height / Mathf.Max(0.01f, worldHeight));
        Vector2 center = canvas.center;

        DrawGridAndFloor(decor, canvas, center, zoom);
        DrawParts(decor, canvas, center, zoom);
        DrawAttachmentGuide(decor, center, zoom);
        HandleInput(decor, canvas, center, zoom);

        EditorGUI.DrawRect(new Rect(canvas.x, canvas.y, canvas.width, 1f), new Color(1f, 1f, 1f, 0.15f));
        EditorGUI.DrawRect(new Rect(canvas.x, canvas.yMax - 1f, canvas.width, 1f), new Color(1f, 1f, 1f, 0.15f));
        EditorGUI.DrawRect(new Rect(canvas.x, canvas.y, 1f, canvas.height), new Color(1f, 1f, 1f, 0.15f));
        EditorGUI.DrawRect(new Rect(canvas.xMax - 1f, canvas.y, 1f, canvas.height), new Color(1f, 1f, 1f, 0.15f));
    }

    private static void DrawGridAndFloor(BattleDecorSO decor, Rect canvas, Vector2 center, float zoom)
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

    private void DrawParts(BattleDecorSO decor, Rect canvas, Vector2 center, float zoom)
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
            DrawSpriteInRect(part.Sprite, drawRect, part.FlipX, part.Tint);
            GUI.matrix = oldMatrix;

            if (i == selectedPartIndex)
            {
                Handles.BeginGUI();
                Handles.color = new Color(1f, 0.80f, 0.08f, 1f);
                Handles.DrawAAPolyLine(2f,
                    new Vector3(drawRect.xMin, drawRect.yMin),
                    new Vector3(drawRect.xMax, drawRect.yMin),
                    new Vector3(drawRect.xMax, drawRect.yMax),
                    new Vector3(drawRect.xMin, drawRect.yMax),
                    new Vector3(drawRect.xMin, drawRect.yMin));
                Handles.EndGUI();

                string coord = $"({part.LocalPosition.x:0.###}, {part.LocalPosition.y:0.###})";
                Rect coordRect = new(drawRect.x, drawRect.y - 18f, Mathf.Max(90f, drawRect.width), 18f);
                GUI.Label(coordRect, coord, EditorStyles.whiteMiniLabel);
            }
        }
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
            case BattleDecorAttachSide.Right:
                point = new Vector2(halfWidth, Mathf.Lerp(-halfHeight, halfHeight, t));
                outward = Vector2.right;
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
        Vector2 guiEnd = guiPoint + guiOutward * 20f;

        Handles.BeginGUI();
        Handles.color = new Color(1f, 0.35f, 0.85f, 1f);
        Handles.DrawAAPolyLine(3f, guiPoint, guiEnd);
        Handles.DrawSolidDisc(guiPoint, Vector3.forward, 4f);
        Handles.EndGUI();
    }

    private void HandleInput(BattleDecorSO decor, Rect canvas, Vector2 center, float zoom)
    {
        Event e = Event.current;
        previewControlId = GUIUtility.GetControlID("BattleDecorPreview".GetHashCode(), FocusType.Passive, canvas);
        EditorGUIUtility.AddCursorRect(canvas, dragging ? MouseCursor.Pan : MouseCursor.MoveArrow);

        if (e.type == EventType.MouseDown && e.button == 0 && canvas.Contains(e.mousePosition))
        {
            selectedPartIndex = FindPartAtPoint(decor, e.mousePosition, center, zoom);
            dragging = selectedPartIndex >= 0;
            if (dragging)
                GUIUtility.hotControl = previewControlId;
            e.Use();
            Repaint();
            return;
        }

        if (e.type == EventType.MouseDrag && e.button == 0 && dragging &&
            GUIUtility.hotControl == previewControlId &&
            selectedPartIndex >= 0 && selectedPartIndex < decor.Parts.Count)
        {
            BattleDecorPart part = decor.Parts[selectedPartIndex];
            if (part != null)
            {
                Vector2 delta = new(e.delta.x / zoom, -e.delta.y / zoom);
                Vector2 next = part.LocalPosition + delta;
                float snap = decor.EditorPositionSnap;
                next.x = Mathf.Round(next.x / snap) * snap;
                next.y = Mathf.Round(next.y / snap) * snap;

                Undo.RecordObject(decor, "Move Battle Decor Part");
                part.SetLocalPosition(next);
                EditorUtility.SetDirty(decor);
            }

            e.Use();
            Repaint();
            return;
        }

        if (e.type == EventType.MouseUp && e.button == 0 && dragging)
        {
            dragging = false;
            if (GUIUtility.hotControl == previewControlId)
                GUIUtility.hotControl = 0;
            e.Use();
            Repaint();
        }
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
            if (variants[i] != null)
                return variants[i];
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
