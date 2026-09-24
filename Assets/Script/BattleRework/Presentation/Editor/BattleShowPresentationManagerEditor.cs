#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BattleShowPresentationManager))]
public sealed class BattleShowPresentationManagerEditor : Editor
{
    private const float PreviewHeight = 320f;
    private const float CenterHandleSize = 12f;
    private const float WidthHandleSize = 9f;

    private const string SelectionFrameStylePropertyName =
        "selectionSpeechBubbleFrameStyle";

    private const string SelectionTailStylePropertyName =
        "selectionSpeechBubbleTailStyle";

    private const string CombatFrameStylePropertyName =
        "combatSpeechBubbleFrameStyle";

    private const string CombatTailStylePropertyName =
        "combatSpeechBubbleTailStyle";

    private static readonly string[] CenterPointPropertyNames =
    {
        "rootY",
        "pivot1",
        "pivot2",
        "pivot3",
        "tip"
    };

    private static readonly string[] WidthPropertyNames =
    {
        "rootHalfWidth",
        "pivot1HalfWidth",
        "pivot2HalfWidth",
        "pivot3HalfWidth"
    };

    private static readonly string[] ScalePreviewLabels =
    {
        "START",
        "OVERSHOOT",
        "SETTLED",
        "SHUTDOWN"
    };

    private sealed class SpeechEditorState
    {
        public readonly string framePropertyName;
        public readonly string tailPropertyName;
        public readonly string title;
        public readonly string previewLabel;

        public Texture2D previewTexture;
        public int previewHash = int.MinValue;
        public bool previewLeft;
        public int previewScaleMode = 2;

        public int activeCenterHandle = -1;
        public int activeWidthHandle = -1;

        public SpeechEditorState(
            string framePropertyName,
            string tailPropertyName,
            string title,
            string previewLabel)
        {
            this.framePropertyName = framePropertyName;
            this.tailPropertyName = tailPropertyName;
            this.title = title;
            this.previewLabel = previewLabel;
        }
    }

    private readonly SpeechEditorState selectionState =
        new(
            SelectionFrameStylePropertyName,
            SelectionTailStylePropertyName,
            "선택씬 대화창 스타일",
            "SELECTION / REWARD / MAP");

    private readonly SpeechEditorState combatState =
        new(
            CombatFrameStylePropertyName,
            CombatTailStylePropertyName,
            "전투씬 대화창 스타일",
            "COMBAT REACTION");

    private BattleShowPresentationManager Manager =>
        target as BattleShowPresentationManager;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Frame/Tail 스타일은 기본 Inspector 중간에 중복 표시하지 않고
        // 하단의 전용 변수 + Preview 블록에서 각각 한 번만 그립니다.
        DrawPropertiesExcluding(
            serializedObject,
            SelectionFrameStylePropertyName,
            SelectionTailStylePropertyName,
            CombatFrameStylePropertyName,
            CombatTailStylePropertyName);

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(12f);

        DrawSpeechEditorSection(
            selectionState,
            Manager != null
                ? Manager.SelectionSpeechBubbleFrameStyle
                : null,
            Manager != null
                ? Manager.SelectionSpeechBubbleTailStyle
                : null);

        DrawSectionDivider();

        DrawSpeechEditorSection(
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

        DrawSpeechPreview(
            rect,
            Manager.SelectionSpeechBubbleFrameStyle,
            Manager.SelectionSpeechBubbleTailStyle,
            selectionState,
            compact: true,
            interactive: false);
    }

    private void OnDisable()
    {
        ReleasePreviewTexture(selectionState);
        ReleasePreviewTexture(combatState);

        if (GUIUtility.hotControl != 0 &&
            (selectionState.activeCenterHandle >= 0 ||
             selectionState.activeWidthHandle >= 0 ||
             combatState.activeCenterHandle >= 0 ||
             combatState.activeWidthHandle >= 0))
        {
            GUIUtility.hotControl = 0;
        }

        ResetActiveHandles(selectionState);
        ResetActiveHandles(combatState);
    }

    private void DrawSpeechEditorSection(
        SpeechEditorState state,
        BattleSpeechBubbleFrameStyle frameStyle,
        BattleSpeechBubbleTailStyle tailStyle)
    {
        BattleShowPresentationManager manager =
            Manager;

        if (manager == null ||
            frameStyle == null ||
            tailStyle == null)
        {
            return;
        }

        SerializedProperty frameProperty =
            serializedObject.FindProperty(
                state.framePropertyName);

        SerializedProperty tailProperty =
            serializedObject.FindProperty(
                state.tailPropertyName);

        if (frameProperty == null ||
            tailProperty == null)
        {
            EditorGUILayout.HelpBox(
                $"{state.framePropertyName} / {state.tailPropertyName} SerializedProperty를 찾지 못했습니다.",
                MessageType.Error);
            return;
        }

        EditorGUILayout.LabelField(
            state.title,
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "FRAME은 대화창 네모 본체의 Size / Start·Overshoot·Settled·Shutdown Scale / " +
            "좌·하·우·상 Outline Outset / Fill Shrink를 조절합니다. " +
            "TAIL은 Pivot/폭/Stroke를 조절합니다. Preview의 체커보드는 실제 배경 영역이고, " +
            "검정은 OUTLINE, 아이보리는 FILL입니다.",
            MessageType.Info);

        EditorGUILayout.Space(4f);

        EditorGUILayout.LabelField(
            "FRAME / 대화창 본체",
            EditorStyles.boldLabel);

        frameProperty.isExpanded = true;

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.PropertyField(
            frameProperty,
            GUIContent.none,
            includeChildren: true);

        bool frameChanged =
            EditorGUI.EndChangeCheck();

        EditorGUILayout.Space(7f);

        EditorGUILayout.LabelField(
            "TAIL / 말풍선 꼬리",
            EditorStyles.boldLabel);

        tailProperty.isExpanded = true;

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.PropertyField(
            tailProperty,
            GUIContent.none,
            includeChildren: true);

        bool tailChanged =
            EditorGUI.EndChangeCheck();

        if (frameChanged ||
            tailChanged)
        {
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();

            EditorUtility.SetDirty(manager);
            InvalidatePreview(state);
            Repaint();
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space(6f);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("꼬리 기본 삼각형"))
            {
                ApplyTailPreset(
                    state,
                    tailStyle,
                    lightning: false);
            }

            if (GUILayout.Button("꼬리 번개형"))
            {
                ApplyTailPreset(
                    state,
                    tailStyle,
                    lightning: true);
            }

            state.previewLeft =
                GUILayout.Toggle(
                    state.previewLeft,
                    "왼쪽 Flip",
                    "Button",
                    GUILayout.Width(90f));
        }

        EditorGUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Preview Scale",
            EditorStyles.miniBoldLabel);

        state.previewScaleMode =
            GUILayout.Toolbar(
                Mathf.Clamp(
                    state.previewScaleMode,
                    0,
                    ScalePreviewLabels.Length - 1),
                ScalePreviewLabels);

        EditorGUILayout.Space(6f);

        Rect previewRect =
            GUILayoutUtility.GetRect(
                100f,
                PreviewHeight,
                GUILayout.ExpandWidth(true));

        DrawSpeechPreview(
            previewRect,
            frameStyle,
            tailStyle,
            state,
            compact: false,
            interactive: true);

        EditorGUILayout.Space(5f);

        EditorGUILayout.LabelField(
            $"Frame Outline L/B/R/T : " +
            $"{frameStyle.outlineLeft:0.#} / " +
            $"{frameStyle.outlineBottom:0.#} / " +
            $"{frameStyle.outlineRight:0.#} / " +
            $"{frameStyle.outlineTop:0.#}",
            EditorStyles.miniLabel);

        EditorGUILayout.LabelField(
            $"Fill Shrink L/B/R/T : " +
            $"{frameStyle.fillInsetLeft:0.#} / " +
            $"{frameStyle.fillInsetBottom:0.#} / " +
            $"{frameStyle.fillInsetRight:0.#} / " +
            $"{frameStyle.fillInsetTop:0.#}",
            EditorStyles.miniLabel);

        EditorGUILayout.LabelField(
            $"Tail Stroke Root → Tip : " +
            $"{tailStyle.strokeRoot:0.#} / " +
            $"{tailStyle.strokePivot1:0.#} / " +
            $"{tailStyle.strokePivot2:0.#} / " +
            $"{tailStyle.strokePivot3:0.#} / " +
            $"{tailStyle.strokeTip:0.#}",
            EditorStyles.miniLabel);

        EditorGUILayout.LabelField(
            "Preview 드래그: 색 점 = Tail Pivot / 흰 W = Tail Half Width",
            EditorStyles.miniLabel);
    }

    private static void DrawSectionDivider()
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

    private void ApplyTailPreset(
        SpeechEditorState state,
        BattleSpeechBubbleTailStyle style,
        bool lightning)
    {
        BattleShowPresentationManager manager =
            Manager;

        if (manager == null ||
            style == null)
        {
            return;
        }

        Undo.RecordObject(
            manager,
            lightning
                ? $"Apply {state.title} Tail Lightning Preset"
                : $"Apply {state.title} Tail Triangle Preset");

        if (lightning)
            style.ApplyLightningPreset();
        else
            style.ApplyTrianglePreset();

        EditorUtility.SetDirty(manager);

        serializedObject.Update();

        InvalidatePreview(state);
        Repaint();
        SceneView.RepaintAll();
    }

    private void DrawSpeechPreview(
        Rect rect,
        BattleSpeechBubbleFrameStyle frameStyle,
        BattleSpeechBubbleTailStyle tailStyle,
        SpeechEditorState state,
        bool compact,
        bool interactive)
    {
        if (frameStyle == null ||
            tailStyle == null ||
            state == null)
        {
            return;
        }

        EnsurePreviewTexture(
            state,
            tailStyle,
            frameStyle.fillColor);

        DrawCheckerboard(
            rect,
            compact ? 10f : 14f);

        float previewScale =
            ResolvePreviewScale(
                frameStyle,
                state.previewScaleMode);

        Rect bubbleRoot =
            CalculateBubbleRootPreviewRect(
                rect,
                frameStyle,
                state.previewLeft,
                compact,
                previewScale);

        float scaleX =
            bubbleRoot.width /
            Mathf.Max(
                1f,
                frameStyle.size.x);

        float scaleY =
            bubbleRoot.height /
            Mathf.Max(
                1f,
                frameStyle.size.y);

        Rect outlineRect =
            new Rect(
                bubbleRoot.xMin -
                frameStyle.outlineLeft * scaleX,
                bubbleRoot.yMin -
                frameStyle.outlineTop * scaleY,
                bubbleRoot.width +
                (frameStyle.outlineLeft +
                 frameStyle.outlineRight) * scaleX,
                bubbleRoot.height +
                (frameStyle.outlineTop +
                 frameStyle.outlineBottom) * scaleY);

        Rect fillRect =
            new Rect(
                bubbleRoot.xMin +
                frameStyle.fillInsetLeft * scaleX,
                bubbleRoot.yMin +
                frameStyle.fillInsetTop * scaleY,
                Mathf.Max(
                    1f,
                    bubbleRoot.width -
                    (frameStyle.fillInsetLeft +
                     frameStyle.fillInsetRight) * scaleX),
                Mathf.Max(
                    1f,
                    bubbleRoot.height -
                    (frameStyle.fillInsetTop +
                     frameStyle.fillInsetBottom) * scaleY));

        EditorGUI.DrawRect(
            outlineRect,
            frameStyle.outlineColor);

        EditorGUI.DrawRect(
            fillRect,
            frameStyle.fillColor);

        if (state.previewTexture != null)
        {
            Rect tailRect =
                CalculateTailPreviewRect(
                    bubbleRoot,
                    tailStyle,
                    compact,
                    state.previewLeft);

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
                state.previewTexture,
                ScaleMode.StretchToFill,
                true);

            GUI.matrix =
                oldMatrix;

            if (!compact)
            {
                DrawCenterLine(
                    tailRect,
                    tailStyle,
                    state.previewLeft);

                if (interactive)
                {
                    DrawInteractiveHandles(
                        tailRect,
                        tailStyle,
                        state);
                }
                else
                {
                    DrawPassiveCenterMarkers(
                        tailRect,
                        tailStyle,
                        state.previewLeft);
                }
            }
        }

        DrawPreviewLegend(
            rect,
            frameStyle,
            state,
            previewScale,
            compact);
    }

    private static Rect CalculateBubbleRootPreviewRect(
        Rect previewRect,
        BattleSpeechBubbleFrameStyle frameStyle,
        bool flipped,
        bool compact,
        float previewScale)
    {
        float margin =
            compact ? 10f : 22f;

        float availableWidth =
            Mathf.Max(
                80f,
                previewRect.width * (compact ? 0.58f : 0.60f));

        float availableHeight =
            Mathf.Max(
                50f,
                previewRect.height * (compact ? 0.52f : 0.50f));

        float aspect =
            Mathf.Max(
                0.1f,
                frameStyle.size.x /
                Mathf.Max(
                    1f,
                    frameStyle.size.y));

        float width =
            availableWidth;

        float height =
            width / aspect;

        if (height > availableHeight)
        {
            height = availableHeight;
            width = height * aspect;
        }

        previewScale =
            Mathf.Clamp(
                previewScale,
                0.35f,
                1.45f);

        width *= previewScale;
        height *= previewScale;

        float centerY =
            previewRect.center.y + 10f;

        float x =
            flipped
                ? previewRect.xMax - margin - width
                : previewRect.xMin + margin;

        return new Rect(
            x,
            centerY - height * 0.5f,
            width,
            height);
    }

    private static float ResolvePreviewScale(
        BattleSpeechBubbleFrameStyle style,
        int mode)
    {
        return mode switch
        {
            0 => style.startScale,
            1 => style.overshootScale,
            3 => style.shutdownScale,
            _ => style.settledScale
        };
    }

    private static Rect CalculateTailPreviewRect(
        Rect bubbleRoot,
        BattleSpeechBubbleTailStyle style,
        bool compact,
        bool flipped)
    {
        float sourceAspect =
            Mathf.Max(
                0.1f,
                style.uiSize.x /
                Mathf.Max(
                    1f,
                    style.uiSize.y));

        float tailHeight =
            Mathf.Min(
                Mathf.Max(
                    34f,
                    bubbleRoot.height * 1.05f),
                compact ? 68f : 112f);

        float tailWidth =
            tailHeight *
            sourceAspect;

        float overlapScale =
            tailWidth /
            Mathf.Max(
                1f,
                style.uiSize.x);

        float previewOverlap =
            Mathf.Clamp(
                style.overlap *
                overlapScale,
                0f,
                tailWidth * 0.85f);

        if (!flipped)
        {
            return new Rect(
                bubbleRoot.xMax -
                previewOverlap,
                bubbleRoot.center.y -
                tailHeight * 0.5f,
                tailWidth,
                tailHeight);
        }

        return new Rect(
            bubbleRoot.xMin -
            tailWidth +
            previewOverlap,
            bubbleRoot.center.y -
            tailHeight * 0.5f,
            tailWidth,
            tailHeight);
    }

    private static void DrawCheckerboard(
        Rect rect,
        float cellSize)
    {
        Color a =
            new Color(
                0.34f,
                0.35f,
                0.38f,
                1f);

        Color b =
            new Color(
                0.20f,
                0.21f,
                0.24f,
                1f);

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
                Rect cell =
                    new Rect(
                        rect.x + x * cellSize,
                        rect.y + y * cellSize,
                        Mathf.Min(
                            cellSize,
                            rect.xMax -
                            (rect.x + x * cellSize)),
                        Mathf.Min(
                            cellSize,
                            rect.yMax -
                            (rect.y + y * cellSize)));

                if (cell.width <= 0f ||
                    cell.height <= 0f)
                {
                    continue;
                }

                EditorGUI.DrawRect(
                    cell,
                    ((x + y) & 1) == 0
                        ? a
                        : b);
            }
        }
    }

    private static void DrawPreviewLegend(
        Rect rect,
        BattleSpeechBubbleFrameStyle frameStyle,
        SpeechEditorState state,
        float previewScale,
        bool compact)
    {
        GUIStyle labelStyle =
            new(EditorStyles.miniBoldLabel)
            {
                normal =
                {
                    textColor = Color.white
                }
            };

        Rect titleRect =
            new Rect(
                rect.x + 8f,
                rect.y + 6f,
                rect.width - 16f,
                18f);

        GUI.Label(
            titleRect,
            $"{state.previewLabel} / " +
            $"{(state.previewLeft ? "LEFT" : "RIGHT")} / " +
            $"{ScalePreviewLabels[Mathf.Clamp(state.previewScaleMode, 0, ScalePreviewLabels.Length - 1)]} " +
            $"x{previewScale:0.###}",
            labelStyle);

        if (compact)
            return;

        float y =
            rect.yMax - 25f;

        float x =
            rect.x + 10f;

        DrawLegendChip(
            ref x,
            y,
            new Color(0.27f, 0.28f, 0.31f, 1f),
            "GRID = BACKGROUND",
            labelStyle);

        DrawLegendChip(
            ref x,
            y,
            frameStyle.outlineColor,
            "OUTLINE",
            labelStyle);

        DrawLegendChip(
            ref x,
            y,
            frameStyle.fillColor,
            "FILL",
            labelStyle);
    }

    private static void DrawLegendChip(
        ref float x,
        float y,
        Color color,
        string label,
        GUIStyle style)
    {
        Rect swatch =
            new Rect(
                x,
                y + 2f,
                14f,
                14f);

        EditorGUI.DrawRect(
            swatch,
            color);

        float labelWidth =
            Mathf.Max(
                46f,
                style.CalcSize(
                    new GUIContent(label)).x + 4f);

        GUI.Label(
            new Rect(
                x + 19f,
                y,
                labelWidth,
                18f),
            label,
            style);

        x +=
            19f +
            labelWidth +
            14f;
    }

    private static void DrawCenterLine(
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
                0.45f);

        for (int i = 1; i < points.Length; i++)
        {
            Handles.DrawLine(
                NormalizedToGui(
                    points[i - 1],
                    tailRect,
                    flipped),
                NormalizedToGui(
                    points[i],
                    tailRect,
                    flipped));
        }

        Handles.EndGUI();
    }

    private void DrawInteractiveHandles(
        Rect tailRect,
        BattleSpeechBubbleTailStyle style,
        SpeechEditorState state)
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
            new Color(0.40f, 0.92f, 1f, 1f),
            new Color(1f, 0.78f, 0.20f, 1f),
            new Color(1f, 0.48f, 0.25f, 1f),
            new Color(0.90f, 0.28f, 0.80f, 1f),
            new Color(0.35f, 1f, 0.48f, 1f)
        };

        for (int i = 0; i < points.Length; i++)
        {
            DrawCenterHandle(
                state,
                i,
                labels[i],
                colors[i],
                points,
                tailRect);
        }

        for (int i = 0; i < 4; i++)
        {
            DrawWidthHandle(
                state,
                i,
                points,
                tailRect);
        }
    }

    private void DrawCenterHandle(
        SpeechEditorState state,
        int index,
        string label,
        Color color,
        Vector2[] points,
        Rect tailRect)
    {
        Vector2 position =
            NormalizedToGui(
                points[index],
                tailRect,
                state.previewLeft);

        Rect marker =
            CenteredRect(
                position,
                CenterHandleSize);

        EditorGUIUtility.AddCursorRect(
            marker,
            MouseCursor.MoveArrow);

        int controlId =
            GUIUtility.GetControlID(
                4100 + index,
                FocusType.Passive,
                marker);

        Event current =
            Event.current;

        if (current.type == EventType.MouseDown &&
            current.button == 0 &&
            marker.Contains(current.mousePosition))
        {
            GUIUtility.hotControl =
                controlId;

            state.activeCenterHandle =
                index;

            state.activeWidthHandle =
                -1;

            Undo.RecordObject(
                Manager,
                $"Move {state.title} {label}");

            current.Use();
        }

        if (GUIUtility.hotControl == controlId &&
            state.activeCenterHandle == index)
        {
            if (current.type == EventType.MouseDrag)
            {
                Vector2 normalized =
                    GuiToNormalized(
                        current.mousePosition,
                        tailRect,
                        state.previewLeft);

                WriteCenterPoint(
                    state,
                    index,
                    normalized);

                current.Use();
            }
            else if (current.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                state.activeCenterHandle = -1;
                current.Use();
            }
        }

        EditorGUI.DrawRect(
            marker,
            color);

        GUI.Label(
            new Rect(
                position.x + 7f,
                position.y - 9f,
                44f,
                17f),
            label,
            EditorStyles.miniBoldLabel);
    }

    private void DrawWidthHandle(
        SpeechEditorState state,
        int index,
        Vector2[] points,
        Rect tailRect)
    {
        Vector2 center =
            NormalizedToGui(
                points[index],
                tailRect,
                state.previewLeft);

        Vector2 tangent =
            ResolveGuiTangent(
                points,
                index,
                tailRect,
                state.previewLeft);

        Vector2 normal =
            new Vector2(
                -tangent.y,
                tangent.x);

        if (normal.sqrMagnitude <= 0.0001f)
            normal = Vector2.up;
        else
            normal.Normalize();

        if (normal.y > 0f)
            normal = -normal;

        BattleSpeechBubbleTailStyle style =
            state.tailPropertyName == SelectionTailStylePropertyName
                ? Manager.SelectionSpeechBubbleTailStyle
                : Manager.CombatSpeechBubbleTailStyle;

        float halfWidth =
            ReadWidthValue(
                style,
                index);

        Vector2 handlePosition =
            center +
            normal *
            halfWidth *
            tailRect.height;

        Rect marker =
            CenteredRect(
                handlePosition,
                WidthHandleSize);

        EditorGUIUtility.AddCursorRect(
            marker,
            MouseCursor.ResizeVertical);

        int controlId =
            GUIUtility.GetControlID(
                5100 + index,
                FocusType.Passive,
                marker);

        Event current =
            Event.current;

        if (current.type == EventType.MouseDown &&
            current.button == 0 &&
            marker.Contains(current.mousePosition))
        {
            GUIUtility.hotControl =
                controlId;

            state.activeWidthHandle =
                index;

            state.activeCenterHandle =
                -1;

            Undo.RecordObject(
                Manager,
                $"Resize {state.title} Tail Width {index}");

            current.Use();
        }

        if (GUIUtility.hotControl == controlId &&
            state.activeWidthHandle == index)
        {
            if (current.type == EventType.MouseDrag)
            {
                float projected =
                    Mathf.Abs(
                        Vector2.Dot(
                            current.mousePosition -
                            center,
                            normal));

                float normalizedWidth =
                    Mathf.Clamp(
                        projected /
                        Mathf.Max(
                            1f,
                            tailRect.height),
                        GetWidthMinimum(index),
                        0.50f);

                WriteWidthValue(
                    state,
                    index,
                    normalizedWidth);

                current.Use();
            }
            else if (current.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                state.activeWidthHandle = -1;
                current.Use();
            }
        }

        Color oldColor =
            GUI.color;

        GUI.color =
            Color.white;

        GUI.DrawTexture(
            marker,
            EditorGUIUtility.whiteTexture,
            ScaleMode.StretchToFill);

        GUI.color =
            oldColor;

        GUI.Label(
            new Rect(
                handlePosition.x + 5f,
                handlePosition.y - 8f,
                18f,
                16f),
            "W",
            EditorStyles.miniLabel);
    }

    private static void DrawPassiveCenterMarkers(
        Rect tailRect,
        BattleSpeechBubbleTailStyle style,
        bool flipped)
    {
        Vector2[] points =
            BattleSpeechBubbleTailTextureBuilder
                .GetCenterPointsNormalized(style);

        for (int i = 0; i < points.Length; i++)
        {
            Vector2 position =
                NormalizedToGui(
                    points[i],
                    tailRect,
                    flipped);

            EditorGUI.DrawRect(
                CenteredRect(
                    position,
                    7f),
                Color.white);
        }
    }

    private void WriteCenterPoint(
        SpeechEditorState state,
        int index,
        Vector2 normalized)
    {
        BattleShowPresentationManager manager =
            Manager;

        if (manager == null)
            return;

        normalized.x =
            Mathf.Clamp01(normalized.x);

        normalized.y =
            Mathf.Clamp01(normalized.y);

        serializedObject.Update();

        SerializedProperty style =
            serializedObject.FindProperty(
                state.tailPropertyName);

        if (style == null)
            return;

        if (index == 0)
        {
            SerializedProperty rootY =
                style.FindPropertyRelative("rootY");

            rootY.floatValue =
                normalized.y;
        }
        else
        {
            SerializedProperty point =
                style.FindPropertyRelative(
                    CenterPointPropertyNames[index]);

            point.vector2Value =
                normalized;
        }

        serializedObject.ApplyModifiedProperties();

        EditorUtility.SetDirty(manager);

        InvalidatePreview(state);
        Repaint();
        SceneView.RepaintAll();
    }

    private void WriteWidthValue(
        SpeechEditorState state,
        int index,
        float value)
    {
        BattleShowPresentationManager manager =
            Manager;

        if (manager == null ||
            index < 0 ||
            index >= WidthPropertyNames.Length)
        {
            return;
        }

        serializedObject.Update();

        SerializedProperty style =
            serializedObject.FindProperty(
                state.tailPropertyName);

        if (style == null)
            return;

        SerializedProperty width =
            style.FindPropertyRelative(
                WidthPropertyNames[index]);

        width.floatValue =
            value;

        serializedObject.ApplyModifiedProperties();

        EditorUtility.SetDirty(manager);

        InvalidatePreview(state);
        Repaint();
        SceneView.RepaintAll();
    }

    private static float ReadWidthValue(
        BattleSpeechBubbleTailStyle style,
        int index)
    {
        if (style == null)
            return 0.1f;

        return index switch
        {
            0 => style.rootHalfWidth,
            1 => style.pivot1HalfWidth,
            2 => style.pivot2HalfWidth,
            3 => style.pivot3HalfWidth,
            _ => 0.1f
        };
    }

    private static float GetWidthMinimum(int index)
    {
        return index switch
        {
            0 => 0.08f,
            1 => 0.04f,
            2 => 0.03f,
            3 => 0.01f,
            _ => 0.01f
        };
    }

    private static Vector2 ResolveGuiTangent(
        Vector2[] points,
        int index,
        Rect tailRect,
        bool flipped)
    {
        if (index <= 0)
        {
            return
                NormalizedToGui(
                    points[1],
                    tailRect,
                    flipped) -
                NormalizedToGui(
                    points[0],
                    tailRect,
                    flipped);
        }

        if (index >= points.Length - 1)
        {
            return
                NormalizedToGui(
                    points[points.Length - 1],
                    tailRect,
                    flipped) -
                NormalizedToGui(
                    points[points.Length - 2],
                    tailRect,
                    flipped);
        }

        Vector2 previous =
            (
                NormalizedToGui(
                    points[index],
                    tailRect,
                    flipped) -
                NormalizedToGui(
                    points[index - 1],
                    tailRect,
                    flipped)
            ).normalized;

        Vector2 next =
            (
                NormalizedToGui(
                    points[index + 1],
                    tailRect,
                    flipped) -
                NormalizedToGui(
                    points[index],
                    tailRect,
                    flipped)
            ).normalized;

        Vector2 combined =
            previous + next;

        if (combined.sqrMagnitude <= 0.0001f)
            return next;

        return combined.normalized;
    }

    private static Vector2 NormalizedToGui(
        Vector2 point,
        Rect rect,
        bool flipped)
    {
        float normalizedX =
            flipped
                ? 1f - point.x
                : point.x;

        return new Vector2(
            rect.x +
            normalizedX *
            rect.width,
            rect.yMax -
            point.y *
            rect.height);
    }

    private static Vector2 GuiToNormalized(
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

    private void EnsurePreviewTexture(
        SpeechEditorState state,
        BattleSpeechBubbleTailStyle style,
        Color fillColor)
    {
        int hash =
            style != null
                ? style.ComputeHash()
                : 0;

        unchecked
        {
            hash =
                hash * 31 +
                fillColor.GetHashCode();
        }

        if (state.previewTexture != null &&
            state.previewHash == hash)
        {
            return;
        }

        ReleasePreviewTexture(state);

        state.previewHash =
            hash;

        state.previewTexture =
            BattleSpeechBubbleTailTextureBuilder
                .BuildTexture(
                    style,
                    fillColor,
                    $"BattleSpeechTail_{state.previewLabel}_EditorPreview");
    }

    private static void ResetActiveHandles(
        SpeechEditorState state)
    {
        if (state == null)
            return;

        state.activeCenterHandle = -1;
        state.activeWidthHandle = -1;
    }

    private void InvalidatePreview(
        SpeechEditorState state)
    {
        if (state == null)
            return;

        state.previewHash =
            int.MinValue;

        ReleasePreviewTexture(state);
    }

    private static void ReleasePreviewTexture(
        SpeechEditorState state)
    {
        if (state == null ||
            state.previewTexture == null)
        {
            return;
        }

        DestroyImmediate(
            state.previewTexture);

        state.previewTexture = null;
    }
}
#endif
