#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BattleShowPresentationManager))]
public sealed class BattleShowPresentationManagerEditor : Editor
{
    private const float PreviewHeight = 290f;
    private const float CenterHandleSize = 12f;
    private const float WidthHandleSize = 9f;

    private const string SelectionTailStylePropertyName =
        "selectionSpeechBubbleTailStyle";

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

    private sealed class TailEditorState
    {
        public readonly string propertyName;
        public readonly string title;
        public readonly string previewLabel;

        public Texture2D previewTexture;
        public int previewHash = int.MinValue;
        public bool previewLeft;

        public int activeCenterHandle = -1;
        public int activeWidthHandle = -1;

        public TailEditorState(
            string propertyName,
            string title,
            string previewLabel)
        {
            this.propertyName = propertyName;
            this.title = title;
            this.previewLabel = previewLabel;
        }
    }

    private readonly TailEditorState selectionTailState =
        new(
            SelectionTailStylePropertyName,
            "선택씬 말풍선 꼬리 스타일",
            "SELECTION / REWARD / MAP");

    private readonly TailEditorState combatTailState =
        new(
            CombatTailStylePropertyName,
            "전투씬 말풍선 꼬리 스타일",
            "COMBAT REACTION");

    private BattleShowPresentationManager Manager =>
        target as BattleShowPresentationManager;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 두 Tail Style은 기본 Inspector 위치에서 숨기고,
        // Inspector 하단의 전용 변수 + Preview 블록에서 각각 한 번만 그립니다.
        DrawPropertiesExcluding(
            serializedObject,
            SelectionTailStylePropertyName,
            CombatTailStylePropertyName);

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(12f);

        DrawTailEditorSection(
            selectionTailState,
            Manager != null
                ? Manager.SelectionSpeechBubbleTailStyle
                : null);

        DrawSectionDivider();

        DrawTailEditorSection(
            combatTailState,
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

        // Unity Inspector 하단 기본 Preview 창에는 선택씬 프로파일을 표시합니다.
        // 두 프로파일의 상세 편집 Preview는 OnInspectorGUI 안에 각각 별도로 있습니다.
        DrawTailPreview(
            rect,
            Manager.SelectionSpeechBubbleTailStyle,
            selectionTailState,
            compact: true,
            interactive: false);
    }

    private void OnDisable()
    {
        ReleasePreviewTexture(selectionTailState);
        ReleasePreviewTexture(combatTailState);

        if (GUIUtility.hotControl != 0 &&
            (selectionTailState.activeCenterHandle >= 0 ||
             selectionTailState.activeWidthHandle >= 0 ||
             combatTailState.activeCenterHandle >= 0 ||
             combatTailState.activeWidthHandle >= 0))
        {
            GUIUtility.hotControl = 0;
        }

        ResetActiveHandles(selectionTailState);
        ResetActiveHandles(combatTailState);
    }

    private void DrawTailEditorSection(
        TailEditorState state,
        BattleSpeechBubbleTailStyle style)
    {
        BattleShowPresentationManager manager =
            Manager;

        if (manager == null ||
            style == null)
        {
            return;
        }

        SerializedProperty styleProperty =
            serializedObject.FindProperty(
                state.propertyName);

        if (styleProperty == null)
        {
            EditorGUILayout.HelpBox(
                $"{state.propertyName} SerializedProperty를 찾지 못했습니다.",
                MessageType.Error);
            return;
        }

        EditorGUILayout.LabelField(
            state.title,
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "이 블록은 다른 씬의 꼬리와 완전히 독립된 값입니다. " +
            "색 점(ROOT/P1/P2/P3/TIP)을 드래그하면 중심 Pivot, 흰 W 핸들을 드래그하면 Half Width가 수정됩니다. " +
            "Stroke Root→Tip 값을 다르게 주면 진행 방향에 따라 외곽선 두께도 변화합니다.",
            MessageType.Info);

        styleProperty.isExpanded = true;

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.PropertyField(
            styleProperty,
            GUIContent.none,
            includeChildren: true);

        bool propertyChanged =
            EditorGUI.EndChangeCheck();

        if (propertyChanged)
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
            if (GUILayout.Button("기본 삼각형"))
            {
                ApplyPreset(
                    state,
                    style,
                    lightning: false);
            }

            if (GUILayout.Button("번개형"))
            {
                ApplyPreset(
                    state,
                    style,
                    lightning: true);
            }

            state.previewLeft =
                GUILayout.Toggle(
                    state.previewLeft,
                    "왼쪽 Flip",
                    "Button",
                    GUILayout.Width(90f));
        }

        EditorGUILayout.Space(6f);

        Rect previewRect =
            GUILayoutUtility.GetRect(
                100f,
                PreviewHeight,
                GUILayout.ExpandWidth(true));

        DrawTailPreview(
            previewRect,
            style,
            state,
            compact: false,
            interactive: true);

        EditorGUILayout.Space(4f);

        EditorGUILayout.LabelField(
            $"Stroke Root → Tip : " +
            $"{style.strokeRoot:0.#} / " +
            $"{style.strokePivot1:0.#} / " +
            $"{style.strokePivot2:0.#} / " +
            $"{style.strokePivot3:0.#} / " +
            $"{style.strokeTip:0.#}",
            EditorStyles.miniLabel);

        EditorGUILayout.LabelField(
            "드래그: 색 점 = Pivot / 흰 W = Half Width",
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

    private void ApplyPreset(
        TailEditorState state,
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
                ? $"Apply {state.title} Lightning Preset"
                : $"Apply {state.title} Triangle Preset");

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

    private void DrawTailPreview(
        Rect rect,
        BattleSpeechBubbleTailStyle style,
        TailEditorState state,
        bool compact,
        bool interactive)
    {
        if (style == null ||
            state == null)
        {
            return;
        }

        EnsurePreviewTexture(
            state,
            style);

        EditorGUI.DrawRect(
            rect,
            new Color(
                0.055f,
                0.060f,
                0.070f,
                1f));

        float margin =
            compact ? 8f : 18f;

        float bubbleHeight =
            Mathf.Min(
                compact ? 72f : 112f,
                rect.height - margin * 2f);

        float bubbleWidth =
            Mathf.Max(
                80f,
                rect.width * 0.50f);

        Rect bubbleOuter =
            new Rect(
                state.previewLeft
                    ? rect.xMax - margin - bubbleWidth
                    : rect.x + margin,
                rect.center.y -
                bubbleHeight * 0.5f,
                bubbleWidth,
                bubbleHeight);

        EditorGUI.DrawRect(
            bubbleOuter,
            new Color(
                0.012f,
                0.012f,
                0.018f,
                1f));

        Rect bubbleInner =
            new Rect(
                bubbleOuter.x + 8f,
                bubbleOuter.y + 8f,
                Mathf.Max(
                    1f,
                    bubbleOuter.width - 16f),
                Mathf.Max(
                    1f,
                    bubbleOuter.height - 16f));

        EditorGUI.DrawRect(
            bubbleInner,
            new Color(
                0.97f,
                0.97f,
                0.94f,
                1f));

        if (state.previewTexture == null)
            return;

        Rect tailRect =
            CalculateTailPreviewRect(
                bubbleOuter,
                style,
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
                style,
                state.previewLeft);

            if (interactive)
            {
                DrawInteractiveHandles(
                    tailRect,
                    style,
                    state);
            }
            else
            {
                DrawPassiveCenterMarkers(
                    tailRect,
                    style,
                    state.previewLeft);
            }
        }

        GUI.Label(
            new Rect(
                rect.x + 8f,
                rect.y + 6f,
                rect.width - 16f,
                18f),
            state.previewLeft
                ? $"{state.previewLabel} / LEFT"
                : $"{state.previewLabel} / RIGHT",
            EditorStyles.miniBoldLabel);
    }

    private static Rect CalculateTailPreviewRect(
        Rect bubbleOuter,
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
                bubbleOuter.height * 0.88f,
                compact ? 66f : 104f);

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
                bubbleOuter.xMax -
                previewOverlap,
                bubbleOuter.center.y -
                tailHeight * 0.5f,
                tailWidth,
                tailHeight);
        }

        return new Rect(
            bubbleOuter.xMin -
            tailWidth +
            previewOverlap,
            bubbleOuter.center.y -
            tailHeight * 0.5f,
            tailWidth,
            tailHeight);
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
                0.36f);

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
        TailEditorState state)
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
        TailEditorState state,
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
        TailEditorState state,
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

        // 항상 Preview 상단 쪽에 폭 핸들을 배치합니다.
        if (normal.y > 0f)
            normal = -normal;

        BattleSpeechBubbleTailStyle style =
            state.propertyName == SelectionTailStylePropertyName
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
                $"Resize {state.title} Width {index}");

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
        TailEditorState state,
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
                state.propertyName);

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
        TailEditorState state,
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
                state.propertyName);

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
        TailEditorState state,
        BattleSpeechBubbleTailStyle style)
    {
        int hash =
            style != null
                ? style.ComputeHash()
                : 0;

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
                    new Color(
                        0.97f,
                        0.97f,
                        0.94f,
                        1f),
                    $"BattleSpeechTail_{state.previewLabel}_EditorPreview");
    }

    private static void ResetActiveHandles(
        TailEditorState state)
    {
        if (state == null)
            return;

        state.activeCenterHandle = -1;
        state.activeWidthHandle = -1;
    }

    private void InvalidatePreview(
        TailEditorState state)
    {
        if (state == null)
            return;

        state.previewHash =
            int.MinValue;

        ReleasePreviewTexture(state);
    }

    private static void ReleasePreviewTexture(
        TailEditorState state)
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
