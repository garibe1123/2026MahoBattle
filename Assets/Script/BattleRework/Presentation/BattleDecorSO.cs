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
        return (int)value == 4 ? BattleDecorAttachSide.Side : value;
    }

#if UNITY_EDITOR
    public int LegacyEmbeddedLightCount
    {
        get
        {
            int count = 0;
            if (parts == null)
                return count;

            for (int i = 0; i < parts.Count; i++)
            {
                BattleDecorPart part = parts[i];
                if (part != null && !part.IsStandaloneLight && part.AddLight2D)
                    count++;
            }

            return count;
        }
    }

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

    public int EditorAddStandaloneLight(Sprite placeholderSprite, Vector2 localPosition)
    {
        if (placeholderSprite == null)
            return -1;

        parts ??= new List<BattleDecorPart>();
        int lightNumber = 1;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] != null && parts[i].IsStandaloneLight)
                lightNumber++;
        }

        BattleDecorPart part = BattleDecorPart.CreateStandaloneLight(
            placeholderSprite,
            localPosition,
            $"Light 2D {lightNumber:00}");
        parts.Add(part);
        return parts.Count - 1;
    }

    public int EditorConvertEmbeddedLightsToStandalone()
    {
        if (parts == null || parts.Count == 0)
            return 0;

        int originalCount = parts.Count;
        int converted = 0;
        int nextLightNumber = 1;
        for (int i = 0; i < originalCount; i++)
        {
            if (parts[i] != null && parts[i].IsStandaloneLight)
                nextLightNumber++;
        }

        for (int i = 0; i < originalCount; i++)
        {
            BattleDecorPart source = parts[i];
            if (source == null || source.IsStandaloneLight || !source.AddLight2D || source.Sprite == null)
                continue;

            BattleDecorPart standalone = BattleDecorPart.CreateStandaloneFromEmbedded(
                source,
                $"Light 2D {nextLightNumber++:00}");
            source.DisableEmbeddedLight();
            parts.Add(standalone);
            converted++;
        }

        return converted;
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

    [SerializeField, HideInInspector] private bool standaloneLight;
    [SerializeField, HideInInspector] private bool addLight2D;
    [SerializeField, HideInInspector] private Vector2 lightLocalOffset;
    [SerializeField, HideInInspector] private float lightRotationDegrees;
    [SerializeField, HideInInspector] private Color lightColor = Color.white;
    [SerializeField, HideInInspector] private float lightIntensity = 1f;
    [SerializeField, HideInInspector] private float lightInnerRadius = 0.15f;
    [SerializeField, HideInInspector] private float lightOuterRadius = 1.25f;
    [SerializeField, HideInInspector] private float lightInnerAngle = 360f;
    [SerializeField, HideInInspector] private float lightOuterAngle = 360f;
    [SerializeField, HideInInspector] private float lightFalloffIntensity = 0.5f;
    [SerializeField, HideInInspector] private int lightBlendStyleIndex;
    [SerializeField, HideInInspector] private int lightOrder;
    [SerializeField, HideInInspector] private bool lightShadowsEnabled;
    [SerializeField, HideInInspector] private float lightShadowIntensity = 0.75f;

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

    public bool IsStandaloneLight => standaloneLight;
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

#if UNITY_EDITOR
    public static BattleDecorPart CreateStandaloneLight(Sprite placeholderSprite, Vector2 position, string displayName)
    {
        BattleDecorPart part = new(placeholderSprite, position)
        {
            label = displayName,
            standaloneLight = true,
            addLight2D = true,
            localScale = Vector2.one,
            rotationDegrees = 0f,
            sortingOffset = 0,
            tint = new Color(1f, 1f, 1f, 0f),
            lightLocalOffset = Vector2.zero,
            lightRotationDegrees = 0f,
            lightColor = Color.white,
            lightIntensity = 1f,
            lightInnerRadius = 0.15f,
            lightOuterRadius = 1.25f,
            lightInnerAngle = 360f,
            lightOuterAngle = 360f,
            lightFalloffIntensity = 0.5f,
            lightBlendStyleIndex = 0,
            lightOrder = 0,
            lightShadowsEnabled = false,
            lightShadowIntensity = 0.75f
        };
        part.Validate();
        return part;
    }

    public static BattleDecorPart CreateStandaloneFromEmbedded(BattleDecorPart source, string displayName)
    {
        BattleDecorPart part = CreateStandaloneLight(source.sprite, source.localPosition, displayName);

        Vector2 sourceScale = source.LocalScale;
        Vector2 scaledOffset = new(
            source.lightLocalOffset.x * sourceScale.x,
            source.lightLocalOffset.y * sourceScale.y);
        float radians = source.rotationDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        Vector2 rotatedOffset = new(
            scaledOffset.x * cos - scaledOffset.y * sin,
            scaledOffset.x * sin + scaledOffset.y * cos);

        part.localPosition = source.localPosition + rotatedOffset;
        part.lightRotationDegrees = NormalizeAngle(source.rotationDegrees + source.lightRotationDegrees);
        part.lightColor = source.lightColor;
        part.lightIntensity = source.lightIntensity;

        float radiusScale = Mathf.Max(sourceScale.x, sourceScale.y);
        part.lightInnerRadius = source.lightInnerRadius * radiusScale;
        part.lightOuterRadius = source.lightOuterRadius * radiusScale;
        part.lightInnerAngle = source.lightInnerAngle;
        part.lightOuterAngle = source.lightOuterAngle;
        part.lightFalloffIntensity = source.lightFalloffIntensity;
        part.lightBlendStyleIndex = source.lightBlendStyleIndex;
        part.lightOrder = source.lightOrder;
        part.lightShadowsEnabled = source.lightShadowsEnabled;
        part.lightShadowIntensity = source.lightShadowIntensity;
        part.Validate();
        return part;
    }

    public void DisableEmbeddedLight()
    {
        if (standaloneLight)
            return;

        addLight2D = false;
    }

    public void SetStandaloneLightSettings(
        Vector2 position,
        float directionDegrees,
        Color color,
        float intensity,
        float innerRadius,
        float outerRadius,
        float innerAngle,
        float outerAngle,
        float falloff,
        int blendStyle,
        int order,
        bool shadows,
        float shadowIntensity)
    {
        if (!standaloneLight)
            return;

        localPosition = position;
        lightLocalOffset = Vector2.zero;
        lightRotationDegrees = directionDegrees;
        lightColor = color;
        lightIntensity = intensity;
        lightInnerRadius = innerRadius;
        lightOuterRadius = outerRadius;
        lightInnerAngle = innerAngle;
        lightOuterAngle = outerAngle;
        lightFalloffIntensity = falloff;
        lightBlendStyleIndex = blendStyle;
        lightOrder = order;
        lightShadowsEnabled = shadows;
        lightShadowIntensity = shadowIntensity;
        Validate();
    }
#endif

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
[CustomEditor(typeof(BattleDecorSO))]
public sealed class BattleDecorSOEditor : Editor
{
    private const float PreviewHeight = 420f;
    private const float HeaderHeight = 26f;
    private const float LightIconSize = 22f;
    private const float LightPanelWidth = 224f;
    private const float LightPanelHeight = 350f;

    private readonly HashSet<int> selectedPartIndices = new();
    private readonly Dictionary<int, Vector2> dragStartPositions = new();

    private int selectedPartIndex = -1;
    private bool dragging;
    private bool dragUndoRecorded;
    private int previewControlId;
    private Vector2 dragStartLocalMouse;

    public override void OnInspectorGUI()
    {
        BattleDecorSO decor = (BattleDecorSO)target;

        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "parts");
        DrawObjectPartsInspector();
        serializedObject.ApplyModifiedProperties();

        if (decor.LegacyEmbeddedLightCount > 0)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                $"이전 방식의 Sprite Part 종속 Light2D가 {decor.LegacyEmbeddedLightCount}개 남아 있습니다. 아래 버튼으로 Preview 독립 Light로 변환할 수 있습니다.",
                MessageType.Warning);

            if (GUILayout.Button("CONVERT ATTACHED LIGHTS TO PREVIEW LIGHTS"))
            {
                Undo.RecordObject(decor, "Convert Battle Decor Lights");
                int converted = decor.EditorConvertEmbeddedLightsToStandalone();
                if (converted > 0)
                {
                    selectedPartIndices.Clear();
                    selectedPartIndex = -1;
                    EditorUtility.SetDirty(decor);
                    serializedObject.Update();
                }
            }
        }

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("BATTLE DECOR PREFAB PREVIEW", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Sprite Drop = Add / Click·Drag = Move / Shift+Click = Multi Select / Delete = Remove. LIGHT ADD로 Light2D를 독립 배치합니다. Light는 Preview에서만 청록색 전구 아이콘으로 표시되며, 겹친 Sprite보다 항상 우선 선택됩니다.",
            MessageType.Info);

        Rect previewRect = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
        DrawPreview(previewRect);
    }

    private void DrawObjectPartsInspector()
    {
        SerializedProperty partsProperty = serializedObject.FindProperty("parts");
        if (partsProperty == null)
            return;

        int visibleCount = 0;
        for (int i = 0; i < partsProperty.arraySize; i++)
        {
            SerializedProperty element = partsProperty.GetArrayElementAtIndex(i);
            SerializedProperty standalone = element.FindPropertyRelative("standaloneLight");
            if (standalone != null && standalone.boolValue)
                continue;
            visibleCount++;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField($"DESIGN PARTS ({visibleCount})", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Sprite Part 추가는 아래 Preview에 Sprite를 Drop해서 진행합니다.", MessageType.None);

        int displayIndex = 0;
        for (int i = 0; i < partsProperty.arraySize; i++)
        {
            SerializedProperty element = partsProperty.GetArrayElementAtIndex(i);
            SerializedProperty standalone = element.FindPropertyRelative("standaloneLight");
            if (standalone != null && standalone.boolValue)
                continue;

            EditorGUILayout.PropertyField(element, new GUIContent($"Element {displayIndex++}"), true);
        }
    }

    private void DrawPreview(Rect rect)
    {
        BattleDecorSO decor = (BattleDecorSO)target;
        PruneSelection(decor.Parts?.Count ?? 0);

        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

        Rect header = new(rect.x + 8f, rect.y + 4f, rect.width - 112f, HeaderHeight);
        Rect lightAddButton = new(rect.xMax - 96f, rect.y + 4f, 88f, 21f);

        int selectionCount = selectedPartIndices.Count;
        string sideLabel = decor.AttachSide == BattleDecorAttachSide.Side ? "   |   Side Basis = Left" : string.Empty;
        string headerText;

        if (selectionCount > 1)
            headerText = $"Selected: {selectionCount}   |   Drag Group   |   Delete   |   Snap {decor.EditorPositionSnap:0.###}{sideLabel}";
        else if (selectedPartIndex >= 0 && selectedPartIndex < decor.Parts.Count)
            headerText = $"Selected: {decor.Parts[selectedPartIndex].Label}   |   Drag   |   Delete   |   Snap {decor.EditorPositionSnap:0.###}{sideLabel}";
        else
            headerText = $"Drop Sprite = Add   |   Shift+Click = Multi Select   |   Snap {decor.EditorPositionSnap:0.###}{sideLabel}";

        GUI.Label(header, headerText, EditorStyles.miniBoldLabel);

        if (GUI.Button(lightAddButton, "+ LIGHT ADD"))
            AddStandaloneLight(decor);

        Rect canvas = new(rect.x + 8f, rect.y + HeaderHeight + 7f, rect.width - 16f, rect.height - HeaderHeight - 15f);
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
        DrawStandaloneLightIcons(decor, center, zoom);

        Rect lightPanel = DrawSelectedLightControlPanel(decor, canvas, center, zoom);
        HandleInput(decor, canvas, center, zoom, lightPanel);

        EditorGUI.DrawRect(new Rect(canvas.x, canvas.y, canvas.width, 1f), new Color(1f, 1f, 1f, 0.15f));
        EditorGUI.DrawRect(new Rect(canvas.x, canvas.yMax - 1f, canvas.width, 1f), new Color(1f, 1f, 1f, 0.15f));
        EditorGUI.DrawRect(new Rect(canvas.x, canvas.y, 1f, canvas.height), new Color(1f, 1f, 1f, 0.15f));
        EditorGUI.DrawRect(new Rect(canvas.xMax - 1f, canvas.y, 1f, canvas.height), new Color(1f, 1f, 1f, 0.15f));
    }

    private void AddStandaloneLight(BattleDecorSO decor)
    {
        Sprite placeholder = ResolveLightPlaceholderSprite(decor);
        if (placeholder == null)
        {
            EditorUtility.DisplayDialog("Battle Decor Light", "Light Runtime 엔트리를 저장하기 위한 기준 Sprite가 없습니다. 먼저 Preview에 Sprite를 하나 Drop하거나 Floor Template을 지정해주세요.", "OK");
            return;
        }

        Undo.RecordObject(decor, "Add Battle Decor Light");
        int added = decor.EditorAddStandaloneLight(placeholder, Vector2.zero);
        if (added < 0)
            return;

        selectedPartIndices.Clear();
        selectedPartIndices.Add(added);
        selectedPartIndex = added;
        dragging = false;
        dragStartPositions.Clear();
        GUIUtility.keyboardControl = previewControlId;
        EditorUtility.SetDirty(decor);
        serializedObject.Update();
        Repaint();
    }

    private static Sprite ResolveLightPlaceholderSprite(BattleDecorSO decor)
    {
        IReadOnlyList<BattleDecorPart> parts = decor.Parts;
        if (parts != null)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                BattleDecorPart part = parts[i];
                if (part != null && !part.IsStandaloneLight && part.Sprite != null)
                    return part.Sprite;
            }
        }

        return ResolvePreviewFloorSprite(decor.FloorTemplate);
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
                Rect cell = WorldRectToGui(new Rect(worldCenter.x - 0.5f, worldCenter.y - 0.5f, 1f, 1f), center, zoom);

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

        Rect carrierRect = WorldRectToGui(new Rect(-footprint.x * 0.5f, -footprint.y * 0.5f, footprint.x, footprint.y), center, zoom);
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
            if (part == null || part.Sprite == null || part.IsStandaloneLight)
                continue;

            Rect drawRect = ResolvePartGuiRect(part, center, zoom);
            Vector2 pivot = drawRect.center;
            Matrix4x4 oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(-part.RotationDegrees, pivot);
            DrawSpriteInRect(part.Sprite, drawRect, part.Flip, part.Tint);
            GUI.matrix = oldMatrix;

            if (!selectedPartIndices.Contains(i))
                continue;

            Handles.BeginGUI();
            Handles.color = i == selectedPartIndex ? new Color(1f, 0.80f, 0.08f, 1f) : new Color(1f, 0.55f, 0.08f, 0.90f);
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
                GUI.Label(new Rect(drawRect.x, drawRect.y - 18f, Mathf.Max(90f, drawRect.width), 18f), coord, EditorStyles.whiteMiniLabel);
            }
        }
    }

    private void DrawStandaloneLightIcons(BattleDecorSO decor, Vector2 center, float zoom)
    {
        IReadOnlyList<BattleDecorPart> parts = decor.Parts;
        if (parts == null)
            return;

        for (int i = 0; i < parts.Count; i++)
        {
            BattleDecorPart lightPart = parts[i];
            if (lightPart == null || !lightPart.IsStandaloneLight)
                continue;

            if (i == selectedPartIndex)
                DrawSelectedLightGuide(lightPart, center, zoom);

            Rect iconRect = ResolveLightIconRect(lightPart, center, zoom);
            Vector2 iconCenter = iconRect.center;
            bool selected = selectedPartIndices.Contains(i);

            Handles.BeginGUI();
            Handles.color = selected ? new Color(1f, 0.80f, 0.08f, 1f) : new Color(0.15f, 0.95f, 0.90f, 1f);
            Handles.DrawSolidDisc(iconCenter, Vector3.forward, selected ? 7.5f : 6.5f);
            Handles.color = new Color(0.02f, 0.12f, 0.13f, 0.95f);
            Handles.DrawSolidDisc(iconCenter, Vector3.forward, 3.5f);

            float ray = selected ? 11f : 9.5f;
            for (int r = 0; r < 8; r++)
            {
                float angle = r * 45f * Mathf.Deg2Rad;
                Vector2 dir = new(Mathf.Cos(angle), Mathf.Sin(angle));
                Handles.color = selected ? new Color(1f, 0.80f, 0.08f, 1f) : new Color(0.15f, 0.95f, 0.90f, 0.95f);
                Handles.DrawAAPolyLine(1.5f, iconCenter + dir * 7f, iconCenter + dir * ray);
            }
            Handles.EndGUI();

            GUI.Label(new Rect(iconRect.xMax + 2f, iconRect.y - 1f, 22f, 16f), "L", EditorStyles.whiteMiniLabel);
        }
    }

    private static void DrawSelectedLightGuide(BattleDecorPart lightPart, Vector2 center, float zoom)
    {
        Vector2 world = lightPart.LocalPosition;
        Vector2 gui = new(center.x + world.x * zoom, center.y - world.y * zoom);
        float radius = Mathf.Max(2f, lightPart.LightOuterRadius * zoom);

        Handles.BeginGUI();
        Handles.color = new Color(lightPart.LightColor.r, lightPart.LightColor.g, lightPart.LightColor.b, 0.65f);
        Handles.DrawWireDisc(gui, Vector3.forward, radius);

        float directionRadians = lightPart.LightRotationDegrees * Mathf.Deg2Rad;
        Vector2 direction = new(Mathf.Cos(directionRadians), -Mathf.Sin(directionRadians));
        Handles.DrawAAPolyLine(2f, gui, gui + direction * Mathf.Min(radius, 42f));

        if (lightPart.LightOuterAngle < 359.5f)
        {
            float halfAngle = lightPart.LightOuterAngle * 0.5f * Mathf.Deg2Rad;
            float baseAngle = -lightPart.LightRotationDegrees * Mathf.Deg2Rad;
            Vector2 rayA = new(Mathf.Cos(baseAngle - halfAngle), Mathf.Sin(baseAngle - halfAngle));
            Vector2 rayB = new(Mathf.Cos(baseAngle + halfAngle), Mathf.Sin(baseAngle + halfAngle));
            Handles.DrawAAPolyLine(1.25f, gui, gui + rayA * Mathf.Min(radius, 52f));
            Handles.DrawAAPolyLine(1.25f, gui, gui + rayB * Mathf.Min(radius, 52f));
        }

        Handles.EndGUI();
    }

    private Rect DrawSelectedLightControlPanel(BattleDecorSO decor, Rect canvas, Vector2 center, float zoom)
    {
        if (selectedPartIndex < 0 || selectedPartIndex >= decor.Parts.Count)
            return default;

        BattleDecorPart part = decor.Parts[selectedPartIndex];
        if (part == null || !part.IsStandaloneLight)
            return default;

        Rect panel = new(canvas.xMax - LightPanelWidth - 8f, canvas.y + 8f, LightPanelWidth, Mathf.Min(LightPanelHeight, canvas.height - 16f));

        EditorGUI.DrawRect(panel, new Color(0.08f, 0.09f, 0.105f, 0.96f));
        EditorGUI.DrawRect(new Rect(panel.x, panel.y, panel.width, 1f), new Color(0.15f, 0.95f, 0.90f, 0.8f));
        EditorGUI.DrawRect(new Rect(panel.x, panel.yMax - 1f, panel.width, 1f), new Color(0.15f, 0.95f, 0.90f, 0.8f));
        EditorGUI.DrawRect(new Rect(panel.x, panel.y, 1f, panel.height), new Color(0.15f, 0.95f, 0.90f, 0.8f));
        EditorGUI.DrawRect(new Rect(panel.xMax - 1f, panel.y, 1f, panel.height), new Color(0.15f, 0.95f, 0.90f, 0.8f));

        float x = panel.x + 8f;
        float y = panel.y + 7f;
        float w = panel.width - 16f;
        const float line = 20f;
        const float gap = 3f;

        GUI.Label(new Rect(x, y, w, line), "LIGHT 2D CONTROL", EditorStyles.boldLabel);
        y += line + 2f;

        Vector2 position = part.LocalPosition;
        float direction = part.LightRotationDegrees;
        Color color = part.LightColor;
        float intensity = part.LightIntensity;
        float innerRadius = part.LightInnerRadius;
        float outerRadius = part.LightOuterRadius;
        float innerAngle = part.LightInnerAngle;
        float outerAngle = part.LightOuterAngle;
        float falloff = part.LightFalloffIntensity;
        int blendStyle = part.LightBlendStyleIndex;
        int order = part.LightOrder;
        bool shadows = part.LightShadowsEnabled;
        float shadowIntensity = part.LightShadowIntensity;

        EditorGUI.BeginChangeCheck();
        position = EditorGUI.Vector2Field(new Rect(x, y, w, line), "Position", position); y += line + gap;
        direction = EditorGUI.FloatField(new Rect(x, y, w, line), "Direction", direction); y += line + gap;
        color = EditorGUI.ColorField(new Rect(x, y, w, line), "Color", color); y += line + gap;
        intensity = EditorGUI.FloatField(new Rect(x, y, w, line), "Intensity", intensity); y += line + gap;
        outerRadius = EditorGUI.FloatField(new Rect(x, y, w, line), "Outer Radius", outerRadius); y += line + gap;
        innerRadius = EditorGUI.FloatField(new Rect(x, y, w, line), "Inner Radius", innerRadius); y += line + gap;
        outerAngle = EditorGUI.Slider(new Rect(x, y, w, line), "Outer Angle", outerAngle, 0f, 360f); y += line + gap;
        innerAngle = EditorGUI.Slider(new Rect(x, y, w, line), "Inner Angle", innerAngle, 0f, 360f); y += line + gap;
        falloff = EditorGUI.Slider(new Rect(x, y, w, line), "Falloff", falloff, 0f, 1f); y += line + gap;
        blendStyle = EditorGUI.IntSlider(new Rect(x, y, w, line), "Blend Style", blendStyle, 0, 3); y += line + gap;
        order = EditorGUI.IntField(new Rect(x, y, w, line), "Order", order); y += line + gap;
        shadows = EditorGUI.Toggle(new Rect(x, y, w, line), "Shadows", shadows); y += line + gap;
        if (shadows && y + line <= panel.yMax - 5f)
            shadowIntensity = EditorGUI.Slider(new Rect(x, y, w, line), "Shadow", shadowIntensity, 0f, 1f);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(decor, "Edit Battle Decor Light");
            part.SetStandaloneLightSettings(position, direction, color, intensity, innerRadius, outerRadius, innerAngle, outerAngle, falloff, blendStyle, order, shadows, shadowIntensity);
            EditorUtility.SetDirty(decor);
            serializedObject.Update();
            Repaint();
        }

        return panel;
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

    private void HandleInput(BattleDecorSO decor, Rect canvas, Vector2 center, float zoom, Rect lightPanel)
    {
        Event e = Event.current;
        previewControlId = GUIUtility.GetControlID("BattleDecorPreview".GetHashCode(), FocusType.Keyboard, canvas);

        bool pointerOverLightPanel = lightPanel.width > 0f && lightPanel.Contains(e.mousePosition);
        if (!pointerOverLightPanel)
            EditorGUIUtility.AddCursorRect(canvas, dragging ? MouseCursor.Pan : MouseCursor.MoveArrow);

        if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && canvas.Contains(e.mousePosition) && !pointerOverLightPanel && TryGetDraggedSprites(out List<Sprite> draggedSprites))
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

        if (e.type == EventType.KeyDown && GUIUtility.keyboardControl == previewControlId && selectedPartIndices.Count > 0 && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
        {
            Undo.RecordObject(decor, selectedPartIndices.Count > 1 ? "Delete Battle Decor Items" : "Delete Battle Decor Item");

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

        if (pointerOverLightPanel)
            return;

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

        if (e.type == EventType.MouseDrag && e.button == 0 && dragging && GUIUtility.hotControl == previewControlId && selectedPartIndices.Count > 0)
        {
            if (!dragUndoRecorded)
            {
                Undo.RecordObject(decor, selectedPartIndices.Count > 1 ? "Move Battle Decor Items" : "Move Battle Decor Item");
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

        if (selectedPartIndex < 0 || selectedPartIndex >= partCount || !selectedPartIndices.Contains(selectedPartIndex))
            selectedPartIndex = ResolvePrimarySelection();
    }

    private int ResolvePrimarySelection()
    {
        foreach (int index in selectedPartIndices)
            return index;
        return -1;
    }

    private int FindPartAtPoint(BattleDecorSO decor, Vector2 mouse, Vector2 center, float zoom)
    {
        IReadOnlyList<BattleDecorPart> parts = decor.Parts;
        if (parts == null)
            return -1;

        for (int i = parts.Count - 1; i >= 0; i--)
        {
            BattleDecorPart part = parts[i];
            if (part == null || !part.IsStandaloneLight)
                continue;

            if (ResolveLightIconRect(part, center, zoom).Contains(mouse))
                return i;
        }

        for (int i = parts.Count - 1; i >= 0; i--)
        {
            BattleDecorPart part = parts[i];
            if (part == null || part.IsStandaloneLight || part.Sprite == null)
                continue;

            if (ResolvePartGuiRect(part, center, zoom).Contains(mouse))
                return i;
        }

        return -1;
    }

    private static Rect ResolveLightIconRect(BattleDecorPart part, Vector2 center, float zoom)
    {
        Vector2 gui = new(center.x + part.LocalPosition.x * zoom, center.y - part.LocalPosition.y * zoom);
        return new Rect(gui.x - LightIconSize * 0.5f, gui.y - LightIconSize * 0.5f, LightIconSize, LightIconSize);
    }

    private static Rect ResolvePartGuiRect(BattleDecorPart part, Vector2 center, float zoom)
    {
        Vector3 bounds = part.Sprite.bounds.size;
        Vector2 scale = part.LocalScale;
        float width = Mathf.Max(0.02f, Mathf.Abs(bounds.x * scale.x));
        float height = Mathf.Max(0.02f, Mathf.Abs(bounds.y * scale.y));
        Rect worldRect = new(part.LocalPosition.x - width * 0.5f, part.LocalPosition.y - height * 0.5f, width, height);
        return WorldRectToGui(worldRect, center, zoom);
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
        return new Vector2((guiPoint.x - center.x) / safeZoom, -(guiPoint.y - center.y) / safeZoom);
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
        Rect uv = new(textureRect.x / texture.width, textureRect.y / texture.height, textureRect.width / texture.width, textureRect.height / texture.height);

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
