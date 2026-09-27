#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Sprites;

[CustomEditor(typeof(BattleShowPresentationManager))]
public sealed class BattleShowPresentationManagerEditor : Editor
{
    private const float PreviewHeight = 340f;
    private const float RewardPreviewHeight = 230f;
    private const float FrameHandleSize = 12f;
    private const float FrameHandleHitSize = 26f;
    private const float TailPointHandleSize = 12f;
    private const float TailWidthHandleSize = 9f;
    private const float MinPreviewZoom = 1f;
    private const float MaxPreviewZoom = 6f;
    private const float PreviewZoomStep = 0.20f;

    // 실제 선택씬은 1500x180이지만 편집성 때문에 두 Preview 모두
    // 전투씬과 같은 390x150 작업 좌표계로 정규화해서 보여줍니다.
    private static readonly Vector2 PreviewDesignSize =
        new(390f, 150f);

    private const string SelectionFrameProperty =
        "selectionSpeechBubbleFrameStyle";
    private const string SelectionTailProperty =
        "selectionSpeechBubbleTailStyle";
    private const string CombatFrameProperty =
        "combatSpeechBubbleFrameStyle";
    private const string CombatTailProperty =
        "combatSpeechBubbleTailStyle";

    // Reward 관련 값은 일반 Inspector에서 중복 표시하지 않고,
    // 아래의 접이식 Reward 탭에서만 정리해서 보여줍니다.
    private static readonly string[] DefaultInspectorExcludedProperties =
    {
        SelectionFrameProperty,
        SelectionTailProperty,
        CombatFrameProperty,
        CombatTailProperty,

        "rewardBasicBaseSprite",
        "rewardCommonBaseSprite",
        "rewardUncommonBaseSprite",
        "rewardRareBaseSprite",
        "rewardEpicBaseSprite",
        "rewardUniqueBaseSprite",

        "rewardBaseAnimationEnabled",
        "rewardBaseAnimationFrameSize",
        "rewardBaseAnimationFps",
        "rewardBaseAnimationLoop",

        "rewardItemPixelYOffset",
        "rewardItemFloatEnabled",
        "rewardItemFloatAmplitudePixels",
        "rewardItemFloatCyclesPerSecond",
        "rewardItemFloatPhaseStep",
        "rewardPreviewItemSprite",

        "rewardShowcaseSpacingWorld",
        "rewardShowcaseMinTileSpacingMultiplier",
        "rewardShowcasePositionOffsetWorld",
        "rewardHoverBoundsPaddingWorld",
        "rewardHoverStickyScreenRadius",

        "rewardCameraPositionOffsetWorld",
        "rewardCameraPadding",
        "rewardCameraMinSize",
        "rewardItemHoverCameraSize",
        "rewardItemHoverCameraPivotOffset",
        "rewardItemHoverMinZoom",
        "rewardItemHoverFollowSharpness",
        "rewardItemHoverZoomSharpness",
        "rewardCursorPanDistance",
        "rewardCursorTrackingSharpness",
        "rewardCursorZoomRatio",

        "rewardSpotlightColor",
        "rewardSpotlightPoolAlpha",
        "rewardSpotlightBeamAlpha",
        "rewardSpotlightWidth",
        "rewardSpotlightBeamLength",
        "rewardSpotlightBeamVerticalOffset",
        "rewardSpotlightFadeSharpness"
    };

    private bool rewardBaseTabOpen;
    private bool rewardLayoutFloatTabOpen;
    private bool rewardCameraTabOpen;
    private bool rewardSpotlightTabOpen;
    private bool rewardPreviewTabOpen;

    private static readonly string[] OutlineCornerProperties =
    {
        "outlineTopLeft",
        "outlineTopRight",
        "outlineBottomRight",
        "outlineBottomLeft"
    };

    private static readonly string[] InnerCornerProperties =
    {
        "fillTopLeft",
        "fillTopRight",
        "fillBottomRight",
        "fillBottomLeft"
    };

    private static readonly string[] TailPointProperties =
    {
        "rootY",
        "pivot1",
        "pivot2",
        "pivot3",
        "tip"
    };

    private static readonly string[] TailWidthProperties =
    {
        "rootHalfWidth",
        "pivot1HalfWidth",
        "pivot2HalfWidth",
        "pivot3HalfWidth"
    };

    private sealed class EditorState
    {
        public readonly string frameProperty;
        public readonly string tailProperty;
        public readonly string title;
        public readonly string previewLabel;
        public readonly Vector2 runtimeSize;
        public readonly string runtimeScale;

        public Texture2D frameTexture;
        public Rect frameLocalBounds;
        public int frameHash = int.MinValue;

        public Texture2D tailTexture;
        public int tailHash = int.MinValue;

        public bool previewLeft;
        // 0=AUTO, 1=OUTLINE only, 2=INNER only
        public int framePickMode;
        public float previewZoom = 1f;
        public Vector2 previewPan;
        public bool previewPanning;
        public Vector2 previewPanStartMouse;
        public Vector2 previewPanStart;

        public int activeFrameHandle = -1;
        public bool activeFrameIsInner;
        public int activeTailPoint = -1;
        public int activeTailWidth = -1;

        public Vector2 dragStartMouse;
        public Vector2 dragStartVector;

        public EditorState(
            string frameProperty,
            string tailProperty,
            string title,
            string previewLabel,
            Vector2 runtimeSize,
            string runtimeScale)
        {
            this.frameProperty = frameProperty;
            this.tailProperty = tailProperty;
            this.title = title;
            this.previewLabel = previewLabel;
            this.runtimeSize = runtimeSize;
            this.runtimeScale = runtimeScale;
        }
    }

    private readonly EditorState selectionState =
        new(
            SelectionFrameProperty,
            SelectionTailProperty,
            "선택씬 대화창 스타일",
            "SELECTION / REWARD / MAP",
            new Vector2(1500f, 180f),
            "FIXED POP 0.88 → 1.04 → 1.00");

    private readonly EditorState combatState =
        new(
            CombatFrameProperty,
            CombatTailProperty,
            "전투씬 대화창 스타일",
            "COMBAT REACTION",
            new Vector2(390f, 150f),
            "FIXED POP 0.86 → 1.055 → 1.00 / OUT 0.92");

    private BattleShowPresentationManager Manager =>
        target as BattleShowPresentationManager;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Reward는 작업 빈도가 높으므로 Inspector 최상단에 접이식 탭으로 둡니다.
        // 처음에는 모두 닫혀 있고 필요한 영역만 열어 튜닝합니다.
        DrawRewardTuningTabs();

        EditorGUILayout.Space(12f);
        DrawDivider();

        DrawPropertiesExcluding(
            serializedObject,
            DefaultInspectorExcludedProperties);

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(12f);
        DrawDivider();

        DrawSection(
            selectionState,
            Manager != null
                ? Manager.SelectionSpeechBubbleFrameStyle
                : null,
            Manager != null
                ? Manager.SelectionSpeechBubbleTailStyle
                : null);

        DrawDivider();

        DrawSection(
            combatState,
            Manager != null
                ? Manager.CombatSpeechBubbleFrameStyle
                : null,
            Manager != null
                ? Manager.CombatSpeechBubbleTailStyle
                : null);
    }

    public override bool RequiresConstantRepaint()
    {
        return
            Manager != null &&
            (Manager.RewardItemFloatEnabled ||
             Manager.RewardBaseAnimationEnabled);
    }

    public override bool HasPreviewGUI()
    {
        return true;
    }

    public override void OnPreviewGUI(
        Rect rect,
        GUIStyle background)
    {
        if (Manager == null)
            return;

        DrawPreview(
            rect,
            Manager.SelectionSpeechBubbleFrameStyle,
            Manager.SelectionSpeechBubbleTailStyle,
            selectionState,
            compact: true,
            interactive: false);
    }

    private void OnDisable()
    {
        ReleaseStateTextures(selectionState);
        ReleaseStateTextures(combatState);

        if (GUIUtility.hotControl != 0)
            GUIUtility.hotControl = 0;
    }

    private void DrawRewardTuningTabs()
    {
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField(
            "REWARD SHOWCASE",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Reward 상품 연출값을 한 곳에서 관리합니다. 각 탭은 기본적으로 닫혀 있으며 필요한 항목만 열어 조절합니다. " +
            "Preview 탭에서는 Base 기준 Item 높이와 Float 폭/속도를 바로 바꾸면서 결과를 확인할 수 있습니다.",
            MessageType.Info);

        rewardBaseTabOpen =
            EditorGUILayout.BeginFoldoutHeaderGroup(
                rewardBaseTabOpen,
                "01. BASE SPRITE / SHEET ANIMATION");

        if (rewardBaseTabOpen)
        {
            EditorGUI.indentLevel++;

            DrawRewardProperty(
                "rewardBasicBaseSprite",
                "Basic Base Sprite");
            DrawRewardProperty(
                "rewardCommonBaseSprite",
                "Common Base Sprite");
            DrawRewardProperty(
                "rewardUncommonBaseSprite",
                "Uncommon Base Sprite");
            DrawRewardProperty(
                "rewardRareBaseSprite",
                "Rare Base Sprite");
            DrawRewardProperty(
                "rewardEpicBaseSprite",
                "Epic Base Sprite");
            DrawRewardProperty(
                "rewardUniqueBaseSprite",
                "Unique Base Sprite");

            EditorGUILayout.Space(5f);

            DrawRewardProperty(
                "rewardBaseAnimationEnabled",
                "Base Sheet Animation");
            DrawRewardProperty(
                "rewardBaseAnimationFrameSize",
                "Frame Size (px)");
            DrawRewardProperty(
                "rewardBaseAnimationFps",
                "Animation FPS");
            DrawRewardProperty(
                "rewardBaseAnimationLoop",
                "Loop");

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndFoldoutHeaderGroup();

        rewardLayoutFloatTabOpen =
            EditorGUILayout.BeginFoldoutHeaderGroup(
                rewardLayoutFloatTabOpen,
                "02. LAYOUT / FLOAT MOTION");

        if (rewardLayoutFloatTabOpen)
        {
            EditorGUI.indentLevel++;

            DrawRewardProperty(
                "rewardShowcaseSpacingWorld",
                "Base Center Spacing (World)");
            DrawRewardProperty(
                "rewardShowcaseMinTileSpacingMultiplier",
                "Minimum Tile Spacing");
            DrawRewardProperty(
                "rewardShowcasePositionOffsetWorld",
                "Showcase Position Offset (World)");

            EditorGUILayout.HelpBox(
                "Showcase Position Offset = 기존 TV/Display 중심 기준으로 Base + Item 진열대 전체를 이동합니다. X는 좌우, Y는 상하입니다.",
                MessageType.None);

            EditorGUILayout.Space(5f);

            DrawRewardProperty(
                "rewardItemPixelYOffset",
                "Item Y From Base (px)");
            DrawRewardProperty(
                "rewardItemFloatEnabled",
                "Float Base + Item Together");
            DrawRewardProperty(
                "rewardItemFloatAmplitudePixels",
                "Float Amplitude (±px)");
            DrawRewardProperty(
                "rewardItemFloatCyclesPerSecond",
                "Float Speed (cycle/s)");
            DrawRewardProperty(
                "rewardItemFloatPhaseStep",
                "Float Phase Step");

            EditorGUILayout.Space(5f);

            DrawRewardProperty(
                "rewardHoverBoundsPaddingWorld",
                "Hover Bounds Padding");
            DrawRewardProperty(
                "rewardHoverStickyScreenRadius",
                "Hover Sticky Radius (px)");

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndFoldoutHeaderGroup();

        rewardCameraTabOpen =
            EditorGUILayout.BeginFoldoutHeaderGroup(
                rewardCameraTabOpen,
                "03. CAMERA / CURSOR TRACKING");

        if (rewardCameraTabOpen)
        {
            EditorGUI.indentLevel++;

            DrawRewardProperty(
                "rewardCameraPositionOffsetWorld",
                "Camera Position Offset (World)");

            EditorGUILayout.HelpBox(
                "Camera Position Offset = Reward 카메라 기준점을 이동합니다. X는 좌우, Y는 상하이며 Overview와 Item Hover 모두에 적용됩니다.",
                MessageType.None);

            DrawRewardProperty(
                "rewardCameraPadding",
                "Overview Padding");
            DrawRewardProperty(
                "rewardCameraMinSize",
                "Overview Min Zoom");
            DrawRewardProperty(
                "rewardItemHoverCameraSize",
                "Hover Zoom");
            DrawRewardProperty(
                "rewardItemHoverCameraPivotOffset",
                "Hover Camera Pivot");
            DrawRewardProperty(
                "rewardItemHoverMinZoom",
                "Hover Min Zoom");
            DrawRewardProperty(
                "rewardItemHoverFollowSharpness",
                "Hover Follow Speed");
            DrawRewardProperty(
                "rewardItemHoverZoomSharpness",
                "Hover Zoom Speed");

            EditorGUILayout.Space(5f);

            DrawRewardProperty(
                "rewardCursorPanDistance",
                "Cursor Pan Distance");
            DrawRewardProperty(
                "rewardCursorTrackingSharpness",
                "Cursor Tracking Speed");
            DrawRewardProperty(
                "rewardCursorZoomRatio",
                "Cursor Zoom Ratio");

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndFoldoutHeaderGroup();

        rewardSpotlightTabOpen =
            EditorGUILayout.BeginFoldoutHeaderGroup(
                rewardSpotlightTabOpen,
                "04. SPOTLIGHT");

        if (rewardSpotlightTabOpen)
        {
            EditorGUI.indentLevel++;

            DrawRewardProperty(
                "rewardSpotlightColor",
                "Color");
            DrawRewardProperty(
                "rewardSpotlightPoolAlpha",
                "Pool Alpha");
            DrawRewardProperty(
                "rewardSpotlightBeamAlpha",
                "Beam Alpha");
            DrawRewardProperty(
                "rewardSpotlightWidth",
                "Beam Width");
            DrawRewardProperty(
                "rewardSpotlightBeamLength",
                "Beam Length");
            DrawRewardProperty(
                "rewardSpotlightBeamVerticalOffset",
                "Beam Vertical Offset");
            DrawRewardProperty(
                "rewardSpotlightFadeSharpness",
                "Fade Speed");

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndFoldoutHeaderGroup();

        rewardPreviewTabOpen =
            EditorGUILayout.BeginFoldoutHeaderGroup(
                rewardPreviewTabOpen,
                "05. LIVE PREVIEW / QUICK CONTROLS");

        if (rewardPreviewTabOpen)
        {
            EditorGUI.indentLevel++;

            DrawRewardProperty(
                "rewardPreviewItemSprite",
                "Preview Item Sprite");

            EditorGUILayout.Space(4f);

            EditorGUILayout.LabelField(
                "진열대 전체 위치",
                EditorStyles.boldLabel);

            DrawRewardProperty(
                "rewardShowcasePositionOffsetWorld",
                "Showcase Position Offset (World)");

            EditorGUILayout.Space(5f);

            EditorGUILayout.LabelField(
                "Reward 카메라 위치",
                EditorStyles.boldLabel);

            DrawRewardProperty(
                "rewardCameraPositionOffsetWorld",
                "Camera Position Offset (World)");

            EditorGUILayout.Space(6f);

            EditorGUILayout.LabelField(
                "Base 기준 Item 위치 / Float 빠른 조절",
                EditorStyles.boldLabel);

            DrawRewardIntSlider(
                "rewardItemPixelYOffset",
                "Item Y From Base (px)",
                -64,
                64);

            DrawRewardIntSlider(
                "rewardItemFloatAmplitudePixels",
                "Float Amplitude (±px)",
                0,
                24);

            DrawRewardFloatSlider(
                "rewardItemFloatCyclesPerSecond",
                "Float Speed (cycle/s)",
                0.05f,
                4f);

            DrawRewardFloatSlider(
                "rewardItemFloatPhaseStep",
                "Float Phase Step",
                0f,
                Mathf.PI);

            EditorGUILayout.Space(7f);

            Rect previewRect =
                GUILayoutUtility.GetRect(
                    160f,
                    RewardPreviewHeight,
                    GUILayout.ExpandWidth(true));

            DrawRewardShowcasePreview(
                previewRect);

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndFoldoutHeaderGroup();

        bool rewardChanged =
            EditorGUI.EndChangeCheck();

        bool applied =
            serializedObject.ApplyModifiedProperties();

        if ((rewardChanged || applied) &&
            Manager != null)
        {
            Manager.NotifyRewardTuningChanged();
            EditorUtility.SetDirty(Manager);
            Repaint();
            SceneView.RepaintAll();
        }

        serializedObject.Update();
    }

    private void DrawRewardProperty(
        string propertyName,
        string label)
    {
        SerializedProperty property =
            serializedObject.FindProperty(
                propertyName);

        if (property == null)
        {
            EditorGUILayout.HelpBox(
                $"Reward SerializedProperty를 찾지 못했습니다: {propertyName}",
                MessageType.Error);
            return;
        }

        EditorGUILayout.PropertyField(
            property,
            new GUIContent(
                label,
                property.tooltip),
            includeChildren: true);
    }

    private void DrawRewardIntSlider(
        string propertyName,
        string label,
        int min,
        int max)
    {
        SerializedProperty property =
            serializedObject.FindProperty(
                propertyName);

        if (property == null)
            return;

        EditorGUI.BeginChangeCheck();

        int next =
            EditorGUILayout.IntSlider(
                new GUIContent(
                    label,
                    property.tooltip),
                property.intValue,
                min,
                max);

        if (EditorGUI.EndChangeCheck())
        {
            property.intValue =
                next;

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(Manager);
            Repaint();
        }
    }

    private void DrawRewardFloatSlider(
        string propertyName,
        string label,
        float min,
        float max)
    {
        SerializedProperty property =
            serializedObject.FindProperty(
                propertyName);

        if (property == null)
            return;

        EditorGUI.BeginChangeCheck();

        float next =
            EditorGUILayout.Slider(
                new GUIContent(
                    label,
                    property.tooltip),
                property.floatValue,
                min,
                max);

        if (EditorGUI.EndChangeCheck())
        {
            property.floatValue =
                next;

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(Manager);
            Repaint();
        }
    }

    private void DrawRewardShowcasePreview(
        Rect rect)
    {
        if (Manager == null)
            return;

        EditorGUI.DrawRect(
            rect,
            new Color(
                0.075f,
                0.080f,
                0.095f,
                1f));

        Rect inner =
            new(
                rect.x + 12f,
                rect.y + 12f,
                Mathf.Max(
                    1f,
                    rect.width - 24f),
                Mathf.Max(
                    1f,
                    rect.height - 24f));

        EditorGUI.DrawRect(
            new Rect(
                inner.x,
                inner.yMax - 1f,
                inner.width,
                1f),
            new Color(
                1f,
                1f,
                1f,
                0.12f));

        GUIStyle titleStyle =
            new(EditorStyles.miniBoldLabel);

        titleStyle.normal.textColor =
            Color.white;

        GUI.Label(
            new Rect(
                inner.x + 4f,
                inner.y + 2f,
                inner.width - 8f,
                18f),
            $"LIVE REWARD SHOWCASE  //  FLOAT {Manager.RewardItemFloatAmplitudePixels}px  " +
            $"@ {Manager.RewardItemFloatCyclesPerSecond:0.00} cycle/s  //  " +
            $"BASE {Manager.RewardBaseAnimationFps:0.#} FPS  //  " +
            $"SPACING {Manager.RewardShowcaseSpacingWorld:0.00}",
            titleStyle);

        EquipmentRarity[] rarities =
        {
            EquipmentRarity.Common,
            EquipmentRarity.Rare,
            EquipmentRarity.Unique
        };

        float floorPpu =
            Mathf.Max(
                1f,
                Manager.GetFloorPixelsPerUnit());

        Vector2 floorWorldSize =
            Manager.GetFloorTileWorldSize();

        float floorWorldHeight =
            Mathf.Max(
                0.01f,
                floorWorldSize.y);

        float floorWorldWidth =
            Mathf.Max(
                0.01f,
                floorWorldSize.x);

        float floorPixelHeight =
            Mathf.Max(
                1f,
                floorWorldHeight *
                floorPpu);

        float desiredTileHeight =
            70f;

        float worldToGui =
            desiredTileHeight /
            floorWorldHeight;

        float centerSpacing =
            Manager.RewardShowcaseSpacingWorld *
            worldToGui;

        float tileWidth =
            floorWorldWidth *
            worldToGui;

        float tileHeight =
            floorWorldHeight *
            worldToGui;

        float requiredWidth =
            tileWidth +
            centerSpacing *
            (rarities.Length - 1);

        float availableWidth =
            Mathf.Max(
                40f,
                inner.width - 48f);

        float layoutScale =
            requiredWidth > availableWidth
                ? availableWidth /
                  requiredWidth
                : 1f;

        centerSpacing *=
            layoutScale;

        tileWidth *=
            layoutScale;

        tileHeight *=
            layoutScale;

        float previewPixelScale =
            tileHeight /
            floorPixelHeight;

        Vector2 showcaseOffset =
            Manager.RewardShowcasePositionOffsetWorld;

        float previewWorldScale =
            worldToGui *
            layoutScale;

        float centerX =
            inner.center.x +
            showcaseOffset.x *
            previewWorldScale;

        // GUI Y축은 아래 방향이 +이므로 World Y는 부호를 뒤집어 적용합니다.
        float baseCenterY =
            inner.y +
            inner.height *
            0.66f -
            showcaseOffset.y *
            previewWorldScale;

        double editorTime =
            EditorApplication.timeSinceStartup;

        float floatPhase =
            (float)editorTime *
            Manager.RewardItemFloatCyclesPerSecond *
            Mathf.PI *
            2f;

        Sprite previewItem =
            Manager.RewardPreviewItemSprite;

        for (int i = 0;
             i < rarities.Length;
             i++)
        {
            float x =
                centerX +
                (i -
                 (rarities.Length - 1) *
                 0.5f) *
                centerSpacing;

            float floatPixels =
                0f;

            if (Manager.RewardItemFloatEnabled)
            {
                floatPixels =
                    Mathf.Sin(
                        floatPhase +
                        i *
                        Manager.RewardItemFloatPhaseStep) *
                    Manager.RewardItemFloatAmplitudePixels;
            }

            // Runtime과 동일하게 Base + Item 전체가 한 유닛으로 같이 Float합니다.
            // GUI Y축은 아래가 +이므로 World 위쪽 이동은 Screen Y에서 빼줍니다.
            float floatingCenterY =
                baseCenterY -
                floatPixels *
                previewPixelScale;

            Rect baseRect =
                new(
                    x - tileWidth * 0.5f,
                    floatingCenterY - tileHeight * 0.5f,
                    tileWidth,
                    tileHeight);

            Sprite baseSprite =
                Manager.GetRewardBaseSprite(
                    rarities[i]);

            DrawRewardBaseFrameInRect(
                baseRect,
                baseSprite,
                editorTime,
                Color.white);

            float itemCenterY =
                floatingCenterY -
                Manager.RewardItemPixelYOffset *
                previewPixelScale;

            Rect itemRect;

            if (previewItem != null)
            {
                float itemWidth =
                    Mathf.Max(
                        8f,
                        previewItem.rect.width *
                        previewPixelScale);

                float itemHeight =
                    Mathf.Max(
                        8f,
                        previewItem.rect.height *
                        previewPixelScale);

                itemRect =
                    new Rect(
                        x - itemWidth * 0.5f,
                        itemCenterY -
                        itemHeight * 0.5f,
                        itemWidth,
                        itemHeight);

                DrawSpriteInRect(
                    itemRect,
                    previewItem,
                    Color.white);
            }
            else
            {
                float placeholder =
                    Mathf.Max(
                        16f,
                        12f *
                        layoutScale);

                itemRect =
                    new Rect(
                        x - placeholder * 0.5f,
                        itemCenterY -
                        placeholder * 0.5f,
                        placeholder,
                        placeholder);

                EditorGUI.DrawRect(
                    itemRect,
                    new Color(
                        0.93f,
                        0.96f,
                        1f,
                        0.95f));

                GUI.Label(
                    new Rect(
                        itemRect.x - 14f,
                        itemRect.y - 17f,
                        itemRect.width + 28f,
                        15f),
                    "ITEM",
                    EditorStyles.centeredGreyMiniLabel);
            }

            GUIStyle rarityStyle =
                new(EditorStyles.miniBoldLabel);

            rarityStyle.alignment =
                TextAnchor.MiddleCenter;

            rarityStyle.normal.textColor =
                new Color(
                    0.78f,
                    0.82f,
                    0.90f,
                    1f);

            GUI.Label(
                new Rect(
                    x -
                    Mathf.Max(
                        45f,
                        tileWidth * 0.7f),
                    baseCenterY +
                    tileHeight * 0.5f +
                    7f,
                    Mathf.Max(
                        90f,
                        tileWidth * 1.4f),
                    18f),
                rarities[i]
                    .ToString()
                    .ToUpperInvariant(),
                rarityStyle);
        }

        GUIStyle footerStyle =
            new(EditorStyles.miniLabel);

        footerStyle.normal.textColor =
            new Color(
                0.62f,
                0.66f,
                0.73f,
                1f);

        GUI.Label(
            new Rect(
                inner.x + 4f,
                inner.yMax - 22f,
                inner.width - 8f,
                18f),
            "BASE + ITEM FLOAT TOGETHER / BASE SPRITE SHEET LIVE / Pixel → Floor PPU 동일",
            footerStyle);
    }

    private void DrawRewardBaseFrameInRect(
        Rect rect,
        Sprite source,
        double editorTime,
        Color tint)
    {
        if (source == null ||
            source.texture == null ||
            Manager == null ||
            !Manager.RewardBaseAnimationEnabled)
        {
            DrawSpriteInRect(
                rect,
                source,
                tint);
            return;
        }

        Vector2Int frameSize =
            Manager.RewardBaseAnimationFrameSize;

        int cellWidth =
            Mathf.Max(
                1,
                frameSize.x);

        int cellHeight =
            Mathf.Max(
                1,
                frameSize.y);

        int width =
            Mathf.RoundToInt(
                source.rect.width);

        int height =
            Mathf.RoundToInt(
                source.rect.height);

        bool isSheet =
            width > cellWidth ||
            height > cellHeight;

        if (!isSheet)
        {
            DrawSpriteInRect(
                rect,
                source,
                tint);
            return;
        }

        int columns =
            Mathf.Max(
                1,
                width / cellWidth);

        int rows =
            Mathf.Max(
                1,
                height / cellHeight);

        int frameCount =
            Mathf.Max(
                1,
                columns *
                rows);

        int rawFrame =
            Mathf.Max(
                0,
                Mathf.FloorToInt(
                    (float)editorTime *
                    Manager.RewardBaseAnimationFps));

        int frameIndex =
            Manager.RewardBaseAnimationLoop
                ? rawFrame % frameCount
                : Mathf.Min(
                    rawFrame,
                    frameCount - 1);

        int row =
            frameIndex /
            columns;

        int column =
            frameIndex %
            columns;

        Rect sourceRect =
            source.rect;

        float pixelX =
            sourceRect.x +
            column *
            cellWidth;

        float pixelY =
            sourceRect.y +
            sourceRect.height -
            (row + 1) *
            cellHeight;

        if (pixelX + cellWidth >
                sourceRect.xMax + 0.01f ||
            pixelY <
                sourceRect.y - 0.01f)
        {
            DrawSpriteInRect(
                rect,
                source,
                tint);
            return;
        }

        Rect uv =
            new(
                pixelX /
                source.texture.width,
                pixelY /
                source.texture.height,
                cellWidth /
                (float)source.texture.width,
                cellHeight /
                (float)source.texture.height);

        DrawTextureRegionInRect(
            rect,
            source.texture,
            uv,
            cellWidth /
            (float)cellHeight,
            tint);
    }

    private static void DrawTextureRegionInRect(
        Rect rect,
        Texture texture,
        Rect uv,
        float contentAspect,
        Color tint)
    {
        if (texture == null ||
            rect.width <= 0f ||
            rect.height <= 0f)
        {
            return;
        }

        float rectAspect =
            rect.width /
            Mathf.Max(
                1f,
                rect.height);

        Rect fitted =
            rect;

        if (contentAspect > rectAspect)
        {
            float height =
                rect.width /
                Mathf.Max(
                    0.001f,
                    contentAspect);

            fitted.y +=
                (rect.height -
                 height) *
                0.5f;

            fitted.height =
                height;
        }
        else
        {
            float width =
                rect.height *
                contentAspect;

            fitted.x +=
                (rect.width -
                 width) *
                0.5f;

            fitted.width =
                width;
        }

        Color oldColor =
            GUI.color;

        GUI.color =
            tint;

        GUI.DrawTextureWithTexCoords(
            fitted,
            texture,
            uv,
            true);

        GUI.color =
            oldColor;
    }

    private static void DrawSpriteInRect(
        Rect rect,
        Sprite sprite,
        Color tint)
    {
        if (rect.width <= 0f ||
            rect.height <= 0f)
        {
            return;
        }

        if (sprite == null ||
            sprite.texture == null)
        {
            EditorGUI.DrawRect(
                rect,
                new Color(
                    0.22f,
                    0.24f,
                    0.29f,
                    1f));

            return;
        }

        Vector4 outerUv =
            DataUtility.GetOuterUV(
                sprite);

        Rect uv =
            Rect.MinMaxRect(
                outerUv.x,
                outerUv.y,
                outerUv.z,
                outerUv.w);

        float spriteAspect =
            sprite.rect.width /
            Mathf.Max(
                1f,
                sprite.rect.height);

        float rectAspect =
            rect.width /
            Mathf.Max(
                1f,
                rect.height);

        Rect fitted =
            rect;

        if (spriteAspect > rectAspect)
        {
            float height =
                rect.width /
                Mathf.Max(
                    0.001f,
                    spriteAspect);

            fitted.y +=
                (rect.height -
                 height) *
                0.5f;

            fitted.height =
                height;
        }
        else
        {
            float width =
                rect.height *
                spriteAspect;

            fitted.x +=
                (rect.width -
                 width) *
                0.5f;

            fitted.width =
                width;
        }

        Color oldColor =
            GUI.color;

        GUI.color =
            tint;

        GUI.DrawTextureWithTexCoords(
            fitted,
            sprite.texture,
            uv,
            true);

        GUI.color =
            oldColor;
    }

    private void DrawSection(
        EditorState state,
        BattleSpeechBubbleFrameStyle frameStyle,
        BattleSpeechBubbleTailStyle tailStyle)
    {
        if (Manager == null ||
            frameStyle == null ||
            tailStyle == null)
        {
            return;
        }

        frameStyle.EnsureCornerPointDefaults();
        tailStyle.EnsurePivotCountDefaults();

        SerializedProperty frame =
            serializedObject.FindProperty(
                state.frameProperty);

        SerializedProperty tail =
            serializedObject.FindProperty(
                state.tailProperty);

        if (frame == null ||
            tail == null)
        {
            EditorGUILayout.HelpBox(
                "대화창 Style SerializedProperty를 찾지 못했습니다.",
                MessageType.Error);
            return;
        }

        EditorGUILayout.LabelField(
            state.title,
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            $"Runtime Window = {state.runtimeSize.x:0}×{state.runtimeSize.y:0} / {state.runtimeScale}. " +
            "창 전체 크기/Pop Scale은 고정입니다. " +
            "FRAME은 빨강 OUTLINE 4점과 초록 INNER 4점의 위치로 직접 만집니다. " +
            "검은 Stroke 두께는 두 사변형 사이 거리입니다.",
            MessageType.Info);

        EditorGUILayout.LabelField(
            "FRAME / 대화창 본체",
            EditorStyles.boldLabel);

        frame.isExpanded = true;

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(
            frame,
            GUIContent.none,
            includeChildren: true);
        bool frameChanged =
            EditorGUI.EndChangeCheck();

        EditorGUILayout.Space(7f);

        EditorGUILayout.LabelField(
            "TAIL / 말풍선 꼬리",
            EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                "활성 Pivot 수",
                GUILayout.Width(88f));

            int currentPivotCount =
                tailStyle.ActivePivotCount;

            int nextPivotCount =
                GUILayout.Toolbar(
                    currentPivotCount,
                    new[]
                    {
                        "0 / TRI",
                        "1",
                        "2",
                        "3 / FULL"
                    });

            if (nextPivotCount != currentPivotCount)
            {
                Undo.RecordObject(
                    Manager,
                    "Change Speech Tail Pivot Count");

                SerializedProperty pivotCountProperty =
                    tail.FindPropertyRelative(
                        "pivotCount");

                if (pivotCountProperty != null)
                {
                    pivotCountProperty.intValue =
                        nextPivotCount;

                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();

                    tailStyle.EnsurePivotCountDefaults();
                    EditorUtility.SetDirty(Manager);

                    state.activeTailPoint = -1;
                    state.activeTailWidth = -1;

                    Invalidate(state);
                    Repaint();
                    SceneView.RepaintAll();
                }
            }
        }

        EditorGUILayout.HelpBox(
            "0 Pivot = ROOT → TIP만 사용하는 단순 삼각형 / 1~3 Pivot = 중간 꺾임점을 순서대로 추가합니다. " +
            "비활성 Pivot 값은 보존되므로 다시 늘리면 기존 위치가 복원됩니다.",
            MessageType.None);

        tail.isExpanded = true;

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(
            tail,
            GUIContent.none,
            includeChildren: true);
        bool tailChanged =
            EditorGUI.EndChangeCheck();

        if (frameChanged ||
            tailChanged)
        {
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();

            frameStyle.EnsureCornerPointDefaults();
            EditorUtility.SetDirty(Manager);

            Invalidate(state);
            Repaint();
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space(6f);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("기본 삼각형 (0 Pivot)"))
            {
                Undo.RecordObject(
                    Manager,
                    "Apply Tail Triangle Preset");

                tailStyle.ApplyTrianglePreset();
                serializedObject.Update();
                EditorUtility.SetDirty(Manager);
                state.activeTailPoint = -1;
                state.activeTailWidth = -1;
                Invalidate(state);
                Repaint();
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("번개형 (3 Pivot)"))
            {
                Undo.RecordObject(
                    Manager,
                    "Apply Tail Lightning Preset");

                tailStyle.ApplyLightningPreset();
                serializedObject.Update();
                EditorUtility.SetDirty(Manager);
                state.activeTailPoint = -1;
                state.activeTailWidth = -1;
                Invalidate(state);
                Repaint();
                SceneView.RepaintAll();
            }

            state.previewLeft =
                GUILayout.Toggle(
                    state.previewLeft,
                    "왼쪽 Flip",
                    "Button",
                    GUILayout.Width(90f));

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField(
                $"Zoom {state.previewZoom:0.00}×",
                GUILayout.Width(78f));

            if (GUILayout.Button(
                "Reset View",
                GUILayout.Width(82f)))
            {
                state.previewZoom = 1f;
                state.previewPan = Vector2.zero;
                Repaint();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                "Frame Pick",
                GUILayout.Width(72f));

            state.framePickMode =
                GUILayout.Toolbar(
                    state.framePickMode,
                    new[]
                    {
                        "AUTO",
                        "OUTLINE",
                        "INNER"
                    });
        }

        EditorGUILayout.HelpBox(
            "Preview 조작: 휠 = 마우스 위치 기준 줌 / 가운데 마우스 드래그 = 화면 이동 / " +
            "AUTO는 가장 가까운 점을 선택하고, OUTLINE/INNER는 겹친 점도 해당 레이어만 강제로 선택합니다. " +
            "클릭 판정은 보이는 점보다 넓게 잡혀 있습니다.",
            MessageType.None);

        EditorGUILayout.Space(6f);

        Rect previewRect =
            GUILayoutUtility.GetRect(
                100f,
                PreviewHeight,
                GUILayout.ExpandWidth(true));

        DrawPreview(
            previewRect,
            frameStyle,
            tailStyle,
            state,
            compact: false,
            interactive: true);

        EditorGUILayout.Space(5f);

        EditorGUILayout.LabelField(
            $"RED OUTLINE TL {Format(frameStyle.outlineTopLeft)} / " +
            $"TR {Format(frameStyle.outlineTopRight)} / " +
            $"BR {Format(frameStyle.outlineBottomRight)} / " +
            $"BL {Format(frameStyle.outlineBottomLeft)}",
            EditorStyles.miniLabel);

        EditorGUILayout.LabelField(
            $"GREEN INNER TL {Format(frameStyle.fillTopLeft)} / " +
            $"TR {Format(frameStyle.fillTopRight)} / " +
            $"BR {Format(frameStyle.fillBottomRight)} / " +
            $"BL {Format(frameStyle.fillBottomLeft)}",
            EditorStyles.miniLabel);

        EditorGUILayout.LabelField(
            "Preview: 빨강 = OUTLINE 꼭짓점 / 초록 = INNER 꼭짓점 / Tail 색 점 = Pivot / W = Tail Width",
            EditorStyles.miniBoldLabel);
    }

    private static string Format(Vector2 value)
    {
        return $"({value.x:0.#},{value.y:0.#})";
    }

    private static void DrawDivider()
    {
        EditorGUILayout.Space(14f);

        Rect line =
            GUILayoutUtility.GetRect(
                1f,
                1f,
                GUILayout.ExpandWidth(true));

        EditorGUI.DrawRect(
            line,
            new Color(
                0.28f,
                0.28f,
                0.28f,
                1f));

        EditorGUILayout.Space(14f);
    }

    private void DrawPreview(
        Rect rect,
        BattleSpeechBubbleFrameStyle frameStyle,
        BattleSpeechBubbleTailStyle tailStyle,
        EditorState state,
        bool compact,
        bool interactive)
    {
        if (frameStyle == null ||
            tailStyle == null ||
            state == null)
        {
            return;
        }

        EnsureFrameTexture(
            state,
            frameStyle);

        EnsureTailTexture(
            state,
            tailStyle,
            frameStyle.fillColor);

        DrawCheckerboard(
            rect,
            compact ? 10f : 14f);

        Rect baseBubbleRoot =
            CalculateBubbleRoot(
                rect,
                state.previewLeft,
                compact);

        if (interactive)
        {
            HandlePreviewNavigation(
                rect,
                baseBubbleRoot,
                state);
        }

        Rect bubbleRoot =
            ApplyPreviewView(
                baseBubbleRoot,
                state,
                compact);

        // Runtime/Preview 모두 Root 좌표계에 대해 같은 꼭짓점 데이터를 씁니다.
        if (state.frameTexture != null)
        {
            Rect frameRect =
                LocalBoundsToGuiRect(
                    state.frameLocalBounds,
                    bubbleRoot);

            GUI.DrawTexture(
                frameRect,
                state.frameTexture,
                ScaleMode.StretchToFill,
                true);
        }

        Rect tailRenderRect =
            CalculateTailRect(
                bubbleRoot,
                tailStyle,
                compact,
                state.previewLeft);

        Rect tailContentRect =
            BattleSpeechBubbleTailTextureBuilder.GetContentRect(
                tailRenderRect);

        if (state.tailTexture != null)
        {
            Matrix4x4 oldMatrix =
                GUI.matrix;

            if (state.previewLeft)
            {
                GUIUtility.ScaleAroundPivot(
                    new Vector2(-1f, 1f),
                    tailRenderRect.center);
            }

            GUI.DrawTexture(
                tailRenderRect,
                state.tailTexture,
                ScaleMode.StretchToFill,
                true);

            GUI.matrix =
                oldMatrix;
        }

        if (!compact)
        {
            DrawTailCenterLine(
                tailContentRect,
                tailStyle,
                state.previewLeft);

            if (interactive)
            {
                DrawFrameCornerHandles(
                    bubbleRoot,
                    frameStyle,
                    state);

                DrawTailHandles(
                    tailContentRect,
                    tailStyle,
                    state);
            }
        }

        DrawLegend(
            rect,
            frameStyle,
            state,
            compact);
    }

    private void HandlePreviewNavigation(
        Rect previewRect,
        Rect baseBubbleRoot,
        EditorState state)
    {
        Event e = Event.current;

        if (!previewRect.Contains(e.mousePosition))
            return;

        Rect current =
            ApplyPreviewView(
                baseBubbleRoot,
                state,
                compact: false);

        if (e.type == EventType.ScrollWheel)
        {
            float oldZoom =
                Mathf.Clamp(
                    state.previewZoom,
                    MinPreviewZoom,
                    MaxPreviewZoom);

            float zoomFactor =
                Mathf.Pow(
                    1f + PreviewZoomStep,
                    -e.delta.y);

            float newZoom =
                Mathf.Clamp(
                    oldZoom * zoomFactor,
                    MinPreviewZoom,
                    MaxPreviewZoom);

            if (!Mathf.Approximately(
                    oldZoom,
                    newZoom))
            {
                Vector2 mouse =
                    e.mousePosition;

                Vector2 normalized =
                    new(
                        Mathf.InverseLerp(
                            current.xMin,
                            current.xMax,
                            mouse.x),
                        Mathf.InverseLerp(
                            current.yMin,
                            current.yMax,
                            mouse.y));

                Vector2 newSize =
                    baseBubbleRoot.size *
                    newZoom;

                Vector2 newMin =
                    mouse -
                    Vector2.Scale(
                        normalized,
                        newSize);

                Vector2 newCenter =
                    newMin +
                    newSize * 0.5f;

                state.previewZoom =
                    newZoom;

                state.previewPan =
                    newCenter -
                    baseBubbleRoot.center;

                Repaint();
            }

            e.Use();
            return;
        }

        if (e.type == EventType.MouseDown &&
            e.button == 2)
        {
            state.activeFrameHandle = -1;
            state.activeTailPoint = -1;
            state.activeTailWidth = -1;
            state.previewPanning = true;
            state.previewPanStartMouse =
                e.mousePosition;
            state.previewPanStart =
                state.previewPan;

            GUIUtility.hotControl =
                GUIUtility.GetControlID(
                    9050,
                    FocusType.Passive);

            e.Use();
            return;
        }

        if (state.previewPanning &&
            e.type == EventType.MouseDrag &&
            e.button == 2)
        {
            state.previewPan =
                state.previewPanStart +
                (e.mousePosition -
                 state.previewPanStartMouse);

            Repaint();
            e.Use();
            return;
        }

        if (state.previewPanning &&
            (e.type == EventType.MouseUp ||
             e.rawType == EventType.MouseUp))
        {
            state.previewPanning = false;

            if (GUIUtility.hotControl != 0)
                GUIUtility.hotControl = 0;

            e.Use();
        }
    }

    private static Rect ApplyPreviewView(
        Rect baseRect,
        EditorState state,
        bool compact)
    {
        if (compact)
            return baseRect;

        float zoom =
            Mathf.Clamp(
                state.previewZoom,
                MinPreviewZoom,
                MaxPreviewZoom);

        Vector2 size =
            baseRect.size *
            zoom;

        return new Rect(
            baseRect.center +
            state.previewPan -
            size * 0.5f,
            size);
    }

    private static Rect CalculateBubbleRoot(
        Rect preview,
        bool left,
        bool compact)
    {
        float margin =
            compact ? 10f : 24f;

        float height =
            Mathf.Min(
                compact ? 74f : 118f,
                preview.height * 0.48f);

        float width =
            height *
            (PreviewDesignSize.x /
             PreviewDesignSize.y);

        width =
            Mathf.Min(
                width,
                preview.width * 0.62f);

        height =
            width *
            (PreviewDesignSize.y /
             PreviewDesignSize.x);

        float x =
            left
                ? preview.xMax - margin - width
                : preview.xMin + margin;

        return new Rect(
            x,
            preview.center.y -
            height * 0.5f,
            width,
            height);
    }

    private static Rect LocalBoundsToGuiRect(
        Rect localBounds,
        Rect bubbleRoot)
    {
        float sx =
            bubbleRoot.width /
            PreviewDesignSize.x;

        float sy =
            bubbleRoot.height /
            PreviewDesignSize.y;

        float x =
            bubbleRoot.xMin +
            localBounds.xMin * sx;

        float yTop =
            bubbleRoot.yMax -
            localBounds.yMax * sy;

        return new Rect(
            x,
            yTop,
            localBounds.width * sx,
            localBounds.height * sy);
    }

    private static Vector2 LocalPointToGui(
        Vector2 local,
        Rect bubbleRoot)
    {
        return new Vector2(
            bubbleRoot.xMin +
            (local.x /
             PreviewDesignSize.x) *
            bubbleRoot.width,
            bubbleRoot.yMax -
            (local.y /
             PreviewDesignSize.y) *
            bubbleRoot.height);
    }

    private static Vector2 GuiPointToLocal(
        Vector2 gui,
        Rect bubbleRoot)
    {
        // Mathf.InverseLerp는 0..1로 Clamp되므로 프레임 꼭짓점을
        // Root 바깥으로 드래그할 수 없게 됩니다.
        // 프레임 편집은 외곽/내부 사변형이 Root 경계를 자유롭게
        // 넘을 수 있어야 하므로 Clamp 없는 선형 좌표 변환을 사용합니다.
        float width =
            Mathf.Max(
                0.0001f,
                bubbleRoot.width);

        float height =
            Mathf.Max(
                0.0001f,
                bubbleRoot.height);

        float normalizedX =
            (gui.x - bubbleRoot.xMin) /
            width;

        float normalizedY =
            (bubbleRoot.yMax - gui.y) /
            height;

        return new Vector2(
            normalizedX *
            PreviewDesignSize.x,
            normalizedY *
            PreviewDesignSize.y);
    }

    private static Vector2[] GetRootCorners()
    {
        return new[]
        {
            new Vector2(
                0f,
                PreviewDesignSize.y),

            new Vector2(
                PreviewDesignSize.x,
                PreviewDesignSize.y),

            new Vector2(
                PreviewDesignSize.x,
                0f),

            Vector2.zero
        };
    }

    private void DrawFrameCornerHandles(
        Rect bubbleRoot,
        BattleSpeechBubbleFrameStyle style,
        EditorState state)
    {
        Vector2[] outline =
            style.GetOutlineCorners(
                PreviewDesignSize);

        Vector2[] inner =
            style.GetFillCorners(
                PreviewDesignSize);

        string[] labels =
        {
            "TL",
            "TR",
            "BR",
            "BL"
        };

        ResolveFrameHandleMouseDown(
            bubbleRoot,
            outline,
            inner,
            state);

        for (int i = 0; i < 4; i++)
        {
            DrawFrameCornerHandle(
                bubbleRoot,
                style,
                state,
                i,
                isInner: false,
                outline[i],
                "O-" + labels[i],
                new Color(
                    1f,
                    0.22f,
                    0.18f,
                    1f));

            DrawFrameCornerHandle(
                bubbleRoot,
                style,
                state,
                i,
                isInner: true,
                inner[i],
                "I-" + labels[i],
                new Color(
                    0.20f,
                    1f,
                    0.42f,
                    1f));
        }
    }

    private void ResolveFrameHandleMouseDown(
        Rect bubbleRoot,
        Vector2[] outline,
        Vector2[] inner,
        EditorState state)
    {
        Event e = Event.current;

        if (e.type != EventType.MouseDown ||
            e.button != 0)
        {
            return;
        }

        float bestDistanceSq =
            float.MaxValue;

        int bestCorner = -1;
        bool bestIsInner = false;

        float hitRadius =
            FrameHandleHitSize * 0.5f;

        float hitRadiusSq =
            hitRadius * hitRadius;

        for (int i = 0; i < 4; i++)
        {
            bool allowOutline =
                state.framePickMode != 2;

            bool allowInner =
                state.framePickMode != 1;

            Vector2 outlinePosition =
                LocalPointToGui(
                    outline[i],
                    bubbleRoot);

            float outlineDistanceSq =
                (e.mousePosition -
                 outlinePosition).sqrMagnitude;

            if (allowOutline &&
                outlineDistanceSq <= hitRadiusSq &&
                outlineDistanceSq < bestDistanceSq)
            {
                bestDistanceSq =
                    outlineDistanceSq;
                bestCorner = i;
                bestIsInner = false;
            }

            Vector2 innerPosition =
                LocalPointToGui(
                    inner[i],
                    bubbleRoot);

            float innerDistanceSq =
                (e.mousePosition -
                 innerPosition).sqrMagnitude;

            if (allowInner &&
                innerDistanceSq <= hitRadiusSq &&
                innerDistanceSq < bestDistanceSq)
            {
                bestDistanceSq =
                    innerDistanceSq;
                bestCorner = i;
                bestIsInner = true;
            }
        }

        if (bestCorner < 0)
            return;

        state.activeFrameHandle =
            bestCorner;

        state.activeFrameIsInner =
            bestIsInner;

        state.activeTailPoint = -1;
        state.activeTailWidth = -1;

        string label =
            (bestIsInner ? "I-" : "O-") +
            bestCorner;

        Undo.RecordObject(
            Manager,
            $"Move {state.title} {label}");

        GUIUtility.hotControl =
            GUIUtility.GetControlID(
                7300,
                FocusType.Passive);

        e.Use();
    }

    private void DrawFrameCornerHandle(
        Rect bubbleRoot,
        BattleSpeechBubbleFrameStyle style,
        EditorState state,
        int cornerIndex,
        bool isInner,
        Vector2 localPoint,
        string label,
        Color color)
    {
        Vector2 position =
            LocalPointToGui(
                localPoint,
                bubbleRoot);

        Rect marker =
            CenteredRect(
                position,
                FrameHandleSize);

        Rect hitRect =
            CenteredRect(
                position,
                FrameHandleHitSize);

        EditorGUIUtility.AddCursorRect(
            hitRect,
            MouseCursor.MoveArrow);

        Event e =
            Event.current;

        if (GUIUtility.hotControl != 0 &&
            state.activeFrameHandle == cornerIndex &&
            state.activeFrameIsInner == isInner)
        {
            if (e.type == EventType.MouseDrag)
            {
                Vector2 local =
                    GuiPointToLocal(
                        e.mousePosition,
                        bubbleRoot);

                Vector2[] root =
                    GetRootCorners();

                Vector2 offset =
                    local -
                    root[cornerIndex];

                // 지나치게 큰 오입력만 방지하고 형태 자체는 자유롭게 둡니다.
                offset.x =
                    Mathf.Clamp(
                        offset.x,
                        -240f,
                        240f);

                offset.y =
                    Mathf.Clamp(
                        offset.y,
                        -160f,
                        160f);

                WriteVector2Property(
                    state,
                    isInner
                        ? InnerCornerProperties[cornerIndex]
                        : OutlineCornerProperties[cornerIndex],
                    offset);

                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                state.activeFrameHandle = -1;
                e.Use();
            }
        }

        if (state.activeFrameHandle == cornerIndex &&
            state.activeFrameIsInner == isInner)
        {
            EditorGUI.DrawRect(
                CenteredRect(
                    position,
                    FrameHandleSize + 6f),
                Color.white);
        }

        EditorGUI.DrawRect(
            marker,
            color);

        GUI.Label(
            new Rect(
                position.x + 7f,
                position.y - 9f,
                48f,
                18f),
            label,
            EditorStyles.miniBoldLabel);
    }

    private Vector2 ReadVector2Property(
        string parentName,
        string childName)
    {
        serializedObject.Update();

        SerializedProperty parent =
            serializedObject.FindProperty(
                parentName);

        SerializedProperty child =
            parent != null
                ? parent.FindPropertyRelative(
                    childName)
                : null;

        return child != null
            ? child.vector2Value
            : Vector2.zero;
    }

    private void WriteVector2Property(
        EditorState state,
        string childName,
        Vector2 value)
    {
        serializedObject.Update();

        SerializedProperty parent =
            serializedObject.FindProperty(
                state.frameProperty);

        SerializedProperty child =
            parent != null
                ? parent.FindPropertyRelative(
                    childName)
                : null;

        if (child == null)
            return;

        child.vector2Value =
            value;

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(Manager);

        Invalidate(state);
        Repaint();
        SceneView.RepaintAll();
    }

    private static Rect CalculateTailRect(
        Rect bubbleRoot,
        BattleSpeechBubbleTailStyle style,
        bool compact,
        bool flipped)
    {
        float aspect =
            Mathf.Max(
                0.1f,
                style.uiSize.x /
                Mathf.Max(
                    1f,
                    style.uiSize.y));

        float contentHeight =
            Mathf.Min(
                Mathf.Max(
                    34f,
                    bubbleRoot.height * 1.05f),
                compact ? 68f : 112f);

        float width =
            contentHeight * aspect;

        float renderHeight =
            contentHeight *
            BattleSpeechBubbleTailTextureBuilder.VerticalDisplayScale;

        float overlap =
            Mathf.Clamp(
                style.overlap *
                (width /
                 Mathf.Max(
                     1f,
                     style.uiSize.x)),
                0f,
                width * 0.85f);

        if (!flipped)
        {
            return new Rect(
                bubbleRoot.xMax -
                overlap,
                bubbleRoot.center.y -
                renderHeight * 0.5f,
                width,
                renderHeight);
        }

        return new Rect(
            bubbleRoot.xMin -
            width +
            overlap,
            bubbleRoot.center.y -
            renderHeight * 0.5f,
            width,
            renderHeight);
    }

    private static void DrawTailCenterLine(
        Rect tailRect,
        BattleSpeechBubbleTailStyle style,
        bool flipped)
    {
        Vector2[] points =
            BattleSpeechBubbleTailTextureBuilder
                .GetCenterPointsNormalized(style);

        Handles.BeginGUI();
        Handles.color =
            new Color(
                1f,
                1f,
                1f,
                0.42f);

        for (int i = 1; i < points.Length; i++)
        {
            Handles.DrawLine(
                TailPointToGui(
                    points[i - 1],
                    tailRect,
                    flipped),
                TailPointToGui(
                    points[i],
                    tailRect,
                    flipped));
        }

        Handles.EndGUI();
    }

    private void DrawTailHandles(
        Rect tailRect,
        BattleSpeechBubbleTailStyle style,
        EditorState state)
    {
        Vector2[] points =
            BattleSpeechBubbleTailTextureBuilder
                .GetCenterPointsNormalized(style);

        for (int i = 0; i < points.Length; i++)
        {
            string label =
                i == 0
                    ? "ROOT"
                    : i == points.Length - 1
                        ? "TIP"
                        : $"P{i}";

            Color color =
                ResolveTailPointColor(
                    i,
                    points.Length);

            Vector2 position =
                TailPointToGui(
                    points[i],
                    tailRect,
                    state.previewLeft);

            Rect marker =
                CenteredRect(
                    position,
                    TailPointHandleSize);

            EditorGUIUtility.AddCursorRect(
                marker,
                MouseCursor.MoveArrow);

            int id =
                GUIUtility.GetControlID(
                    8100 + i,
                    FocusType.Passive,
                    marker);

            Event e =
                Event.current;

            if (e.type == EventType.MouseDown &&
                e.button == 0 &&
                marker.Contains(e.mousePosition))
            {
                GUIUtility.hotControl = id;
                state.activeTailPoint = i;
                state.activeTailWidth = -1;
                state.activeFrameHandle = -1;

                Undo.RecordObject(
                    Manager,
                    $"Move {state.title} Tail {label}");

                e.Use();
            }

            if (GUIUtility.hotControl == id &&
                state.activeTailPoint == i)
            {
                if (e.type == EventType.MouseDrag)
                {
                    Vector2 normalized =
                        GuiToTailPoint(
                            e.mousePosition,
                            tailRect,
                            state.previewLeft);

                    WriteTailPoint(
                        state,
                        i,
                        normalized);

                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    GUIUtility.hotControl = 0;
                    state.activeTailPoint = -1;
                    e.Use();
                }
            }

            EditorGUI.DrawRect(
                marker,
                color);

            GUI.Label(
                new Rect(
                    position.x + 7f,
                    position.y - 9f,
                    45f,
                    18f),
                label,
                EditorStyles.miniBoldLabel);
        }

        for (int i = 0; i < points.Length - 1; i++)
        {
            DrawTailWidthHandle(
                state,
                tailRect,
                points,
                i);
        }
    }

    private static Color ResolveTailPointColor(
        int index,
        int pointCount)
    {
        if (index <= 0)
        {
            return new Color(
                0.40f,
                0.92f,
                1f,
                1f);
        }

        if (index >= pointCount - 1)
        {
            return new Color(
                0.35f,
                1f,
                0.48f,
                1f);
        }

        return index switch
        {
            1 => new Color(
                1f,
                0.78f,
                0.20f,
                1f),
            2 => new Color(
                1f,
                0.48f,
                0.25f,
                1f),
            _ => new Color(
                0.90f,
                0.28f,
                0.80f,
                1f)
        };
    }

    private void DrawTailWidthHandle(
        EditorState state,
        Rect tailRect,
        Vector2[] points,
        int index)
    {
        BattleSpeechBubbleTailStyle style =
            state.tailProperty ==
            SelectionTailProperty
                ? Manager.SelectionSpeechBubbleTailStyle
                : Manager.CombatSpeechBubbleTailStyle;

        Vector2 center =
            TailPointToGui(
                points[index],
                tailRect,
                state.previewLeft);

        Vector2 tangent;

        if (index == 0)
        {
            tangent =
                TailPointToGui(
                    points[1],
                    tailRect,
                    state.previewLeft) -
                center;
        }
        else
        {
            Vector2 a =
                center -
                TailPointToGui(
                    points[index - 1],
                    tailRect,
                    state.previewLeft);

            Vector2 b =
                TailPointToGui(
                    points[index + 1],
                    tailRect,
                    state.previewLeft) -
                center;

            tangent =
                a.normalized +
                b.normalized;
        }

        if (tangent.sqrMagnitude <= 0.0001f)
            tangent = Vector2.right;
        else
            tangent.Normalize();

        Vector2 normal =
            new(-tangent.y, tangent.x);

        if (normal.y > 0f)
            normal = -normal;

        float width =
            index switch
            {
                0 => style.rootHalfWidth,
                1 => style.pivot1HalfWidth,
                2 => style.pivot2HalfWidth,
                _ => style.pivot3HalfWidth
            };

        Vector2 position =
            center +
            normal *
            width *
            tailRect.height;

        Rect marker =
            CenteredRect(
                position,
                TailWidthHandleSize);

        EditorGUIUtility.AddCursorRect(
            marker,
            MouseCursor.ResizeVertical);

        int id =
            GUIUtility.GetControlID(
                8200 + index,
                FocusType.Passive,
                marker);

        Event e =
            Event.current;

        if (e.type == EventType.MouseDown &&
            e.button == 0 &&
            marker.Contains(e.mousePosition))
        {
            GUIUtility.hotControl = id;
            state.activeTailWidth = index;
            state.activeTailPoint = -1;
            state.activeFrameHandle = -1;

            Undo.RecordObject(
                Manager,
                $"Resize {state.title} Tail Width");

            e.Use();
        }

        if (GUIUtility.hotControl == id &&
            state.activeTailWidth == index)
        {
            if (e.type == EventType.MouseDrag)
            {
                float projected =
                    Mathf.Abs(
                        Vector2.Dot(
                            e.mousePosition -
                            center,
                            normal));

                float normalized =
                    Mathf.Clamp(
                        projected /
                        Mathf.Max(
                            1f,
                            tailRect.height),
                        0.01f,
                        0.50f);

                WriteTailWidth(
                    state,
                    index,
                    normalized);

                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                state.activeTailWidth = -1;
                e.Use();
            }
        }

        GUI.DrawTexture(
            marker,
            EditorGUIUtility.whiteTexture);

        GUI.Label(
            new Rect(
                position.x + 5f,
                position.y - 8f,
                18f,
                16f),
            "W",
            EditorStyles.miniLabel);
    }

    private void WriteTailPoint(
        EditorState state,
        int index,
        Vector2 value)
    {
        serializedObject.Update();

        SerializedProperty tail =
            serializedObject.FindProperty(
                state.tailProperty);

        if (tail == null)
            return;

        BattleSpeechBubbleTailStyle activeStyle =
            state.tailProperty ==
            SelectionTailProperty
                ? Manager.SelectionSpeechBubbleTailStyle
                : Manager.CombatSpeechBubbleTailStyle;

        activeStyle?.EnsurePivotCountDefaults();

        int pointCount =
            activeStyle != null
                ? activeStyle.ActivePointCount
                : 5;

        if (index == 0)
        {
            tail.FindPropertyRelative(
                "rootY").floatValue =
                value.y;
        }
        else if (index == pointCount - 1)
        {
            tail.FindPropertyRelative(
                "tip").vector2Value =
                value;
        }
        else
        {
            tail.FindPropertyRelative(
                TailPointProperties[index]).vector2Value =
                value;
        }

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(Manager);

        Invalidate(state);
        Repaint();
        SceneView.RepaintAll();
    }

    private void WriteTailWidth(
        EditorState state,
        int index,
        float value)
    {
        serializedObject.Update();

        SerializedProperty tail =
            serializedObject.FindProperty(
                state.tailProperty);

        if (tail == null)
            return;

        tail.FindPropertyRelative(
            TailWidthProperties[index]).floatValue =
            value;

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(Manager);

        Invalidate(state);
        Repaint();
        SceneView.RepaintAll();
    }

    private static Vector2 TailPointToGui(
        Vector2 point,
        Rect rect,
        bool flipped)
    {
        float x =
            flipped
                ? 1f - point.x
                : point.x;

        return new Vector2(
            rect.x +
            x * rect.width,
            rect.yMax -
            point.y * rect.height);
    }

    private static Vector2 GuiToTailPoint(
        Vector2 mouse,
        Rect rect,
        bool flipped)
    {
        float x =
            Mathf.InverseLerp(
                rect.xMin,
                rect.xMax,
                mouse.x);

        if (flipped)
            x = 1f - x;

        float y =
            1f -
            Mathf.InverseLerp(
                rect.yMin,
                rect.yMax,
                mouse.y);

        return new Vector2(
            Mathf.Clamp01(x),
            Mathf.Clamp01(y));
    }

    private static Rect CenteredRect(
        Vector2 center,
        float size)
    {
        return new Rect(
            center.x - size * 0.5f,
            center.y - size * 0.5f,
            size,
            size);
    }

    private static void DrawCheckerboard(
        Rect rect,
        float cellSize)
    {
        Color a =
            new(0.34f, 0.35f, 0.38f, 1f);

        Color b =
            new(0.20f, 0.21f, 0.24f, 1f);

        int columns =
            Mathf.CeilToInt(
                rect.width /
                Mathf.Max(4f, cellSize));

        int rows =
            Mathf.CeilToInt(
                rect.height /
                Mathf.Max(4f, cellSize));

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                EditorGUI.DrawRect(
                    new Rect(
                        rect.x +
                        x * cellSize,
                        rect.y +
                        y * cellSize,
                        Mathf.Min(
                            cellSize,
                            rect.xMax -
                            (rect.x + x * cellSize)),
                        Mathf.Min(
                            cellSize,
                            rect.yMax -
                            (rect.y + y * cellSize))),
                    ((x + y) & 1) == 0
                        ? a
                        : b);
            }
        }
    }

    private static void DrawLegend(
        Rect rect,
        BattleSpeechBubbleFrameStyle style,
        EditorState state,
        bool compact)
    {
        GUIStyle label =
            new(EditorStyles.miniBoldLabel);

        label.normal.textColor =
            Color.white;

        GUI.Label(
            new Rect(
                rect.x + 8f,
                rect.y + 6f,
                rect.width - 16f,
                18f),
            $"{state.previewLabel} / " +
            $"{(state.previewLeft ? "LEFT" : "RIGHT")} / " +
            $"EDITOR VIEW 390×150 / ZOOM {state.previewZoom:0.00}×",
            label);

        if (compact)
            return;

        float y =
            rect.yMax - 25f;

        float x =
            rect.x + 10f;

        DrawLegendChip(
            ref x,
            y,
            new Color(
                0.27f,
                0.28f,
                0.31f,
                1f),
            "GRID = BACKGROUND",
            label);

        DrawLegendChip(
            ref x,
            y,
            new Color(
                1f,
                0.22f,
                0.18f,
                1f),
            "RED = OUTLINE POINT",
            label);

        DrawLegendChip(
            ref x,
            y,
            new Color(
                0.20f,
                1f,
                0.42f,
                1f),
            "GREEN = INNER POINT",
            label);
    }

    private static void DrawLegendChip(
        ref float x,
        float y,
        Color color,
        string text,
        GUIStyle style)
    {
        EditorGUI.DrawRect(
            new Rect(
                x,
                y + 2f,
                14f,
                14f),
            color);

        float width =
            Mathf.Max(
                50f,
                style.CalcSize(
                    new GUIContent(text)).x + 4f);

        GUI.Label(
            new Rect(
                x + 19f,
                y,
                width,
                18f),
            text,
            style);

        x +=
            19f +
            width +
            14f;
    }

    private void EnsureFrameTexture(
        EditorState state,
        BattleSpeechBubbleFrameStyle style)
    {
        style.EnsureCornerPointDefaults();

        int hash =
            style.ComputeHash();

        if (state.frameTexture != null &&
            state.frameHash == hash)
        {
            return;
        }

        ReleaseFrameTexture(state);

        state.frameHash =
            hash;

        state.frameTexture =
            BattleSpeechBubbleFrameTextureBuilder.BuildFrameTexture(
                style,
                PreviewDesignSize,
                $"BattleSpeechFrame_{state.previewLabel}_Preview",
                out state.frameLocalBounds);
    }

    private void EnsureTailTexture(
        EditorState state,
        BattleSpeechBubbleTailStyle style,
        Color fillColor)
    {
        int hash =
            style.ComputeHash();

        unchecked
        {
            hash =
                hash * 31 +
                fillColor.GetHashCode();
        }

        if (state.tailTexture != null &&
            state.tailHash == hash)
        {
            return;
        }

        ReleaseTailTexture(state);

        state.tailHash =
            hash;

        state.tailTexture =
            BattleSpeechBubbleTailTextureBuilder.BuildTexture(
                style,
                fillColor,
                $"BattleSpeechTail_{state.previewLabel}_Preview");
    }

    private void Invalidate(EditorState state)
    {
        state.frameHash = int.MinValue;
        state.tailHash = int.MinValue;

        ReleaseStateTextures(state);
    }

    private static void ReleaseStateTextures(
        EditorState state)
    {
        ReleaseFrameTexture(state);
        ReleaseTailTexture(state);
    }

    private static void ReleaseFrameTexture(
        EditorState state)
    {
        if (state.frameTexture == null)
            return;

        DestroyImmediate(
            state.frameTexture);

        state.frameTexture = null;
    }

    private static void ReleaseTailTexture(
        EditorState state)
    {
        if (state.tailTexture == null)
            return;

        DestroyImmediate(
            state.tailTexture);

        state.tailTexture = null;
    }
}
#endif
