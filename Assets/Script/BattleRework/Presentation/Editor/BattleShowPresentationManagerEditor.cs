#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BattleShowPresentationManager))]
public sealed class BattleShowPresentationManagerEditor : Editor
{
    private const float PreviewHeight = 340f;
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

        DrawPropertiesExcluding(
            serializedObject,
            SelectionFrameProperty,
            SelectionTailProperty,
            CombatFrameProperty,
            CombatTailProperty);

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(12f);

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
            if (GUILayout.Button("꼬리 기본 삼각형"))
            {
                Undo.RecordObject(
                    Manager,
                    "Apply Tail Triangle Preset");

                tailStyle.ApplyTrianglePreset();
                EditorUtility.SetDirty(Manager);
                Invalidate(state);
            }

            if (GUILayout.Button("꼬리 번개형"))
            {
                Undo.RecordObject(
                    Manager,
                    "Apply Tail Lightning Preset");

                tailStyle.ApplyLightningPreset();
                EditorUtility.SetDirty(Manager);
                Invalidate(state);
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

        EditorGUILayout.HelpBox(
            "Preview 조작: 휠 = 마우스 위치 기준 줌 / 가운데 마우스 드래그 = 화면 이동 / " +
            "프레임 점은 보이는 사각형보다 넓은 판정 범위에서 가장 가까운 OUTLINE 또는 INNER 점 하나를 선택합니다.",
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

        Rect tailRect =
            CalculateTailRect(
                bubbleRoot,
                tailStyle,
                compact,
                state.previewLeft);

        if (state.tailTexture != null)
        {
            Matrix4x4 oldMatrix =
                GUI.matrix;

            if (state.previewLeft)
            {
                GUIUtility.ScaleAroundPivot(
                    new Vector2(-1f, 1f),
                    tailRect.center);
            }

            GUI.DrawTexture(
                tailRect,
                state.tailTexture,
                ScaleMode.StretchToFill,
                true);

            GUI.matrix =
                oldMatrix;
        }

        if (!compact)
        {
            DrawTailCenterLine(
                tailRect,
                tailStyle,
                state.previewLeft);

            if (interactive)
            {
                DrawFrameCornerHandles(
                    bubbleRoot,
                    frameStyle,
                    state);

                DrawTailHandles(
                    tailRect,
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
        return new Vector2(
            Mathf.InverseLerp(
                bubbleRoot.xMin,
                bubbleRoot.xMax,
                gui.x) *
            PreviewDesignSize.x,
            (1f -
             Mathf.InverseLerp(
                 bubbleRoot.yMin,
                 bubbleRoot.yMax,
                 gui.y)) *
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
            Vector2 outlinePosition =
                LocalPointToGui(
                    outline[i],
                    bubbleRoot);

            float outlineDistanceSq =
                (e.mousePosition -
                 outlinePosition).sqrMagnitude;

            if (outlineDistanceSq <= hitRadiusSq &&
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

            if (innerDistanceSq <= hitRadiusSq &&
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

        float height =
            Mathf.Min(
                Mathf.Max(
                    34f,
                    bubbleRoot.height * 1.05f),
                compact ? 68f : 112f);

        float width =
            height * aspect;

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
                height * 0.5f,
                width,
                height);
        }

        return new Rect(
            bubbleRoot.xMin -
            width +
            overlap,
            bubbleRoot.center.y -
            height * 0.5f,
            width,
            height);
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

        string[] labels =
        {
            "ROOT",
            "P1",
            "P2",
            "P3",
            "TIP"
        };

        Color[] colors =
        {
            new(0.40f, 0.92f, 1f, 1f),
            new(1f, 0.78f, 0.20f, 1f),
            new(1f, 0.48f, 0.25f, 1f),
            new(0.90f, 0.28f, 0.80f, 1f),
            new(0.35f, 1f, 0.48f, 1f)
        };

        for (int i = 0; i < points.Length; i++)
        {
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
                    $"Move {state.title} Tail {labels[i]}");

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
                colors[i]);

            GUI.Label(
                new Rect(
                    position.x + 7f,
                    position.y - 9f,
                    45f,
                    18f),
                labels[i],
                EditorStyles.miniBoldLabel);
        }

        for (int i = 0; i < 4; i++)
        {
            DrawTailWidthHandle(
                state,
                tailRect,
                points,
                i);
        }
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

        if (index == 0)
        {
            tail.FindPropertyRelative(
                "rootY").floatValue =
                value.y;
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
