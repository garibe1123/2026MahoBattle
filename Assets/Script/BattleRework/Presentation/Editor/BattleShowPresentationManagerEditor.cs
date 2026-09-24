#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BattleShowPresentationManager))]
public sealed class BattleShowPresentationManagerEditor : Editor
{
    private const float PreviewHeight = 290f;
    private const float CenterHandleSize = 12f;
    private const float WidthHandleSize = 9f;

    private const string TailStylePropertyName =
        "speechBubbleTailStyle";

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

    private Texture2D previewTexture;
    private int previewHash = int.MinValue;
    private bool previewLeft;

    private int activeCenterHandle = -1;
    private int activeWidthHandle = -1;

    private BattleShowPresentationManager Manager =>
        target as BattleShowPresentationManager;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Tail Style은 기본 Inspector 위치에서는 제외하고
        // 아래 Preview 바로 위의 전용 편집 패널에서만 그립니다.
        DrawPropertiesExcluding(
            serializedObject,
            TailStylePropertyName);

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10f);
        DrawTailEditorSection();
    }

    public override bool HasPreviewGUI()
    {
        return true;
    }

    public override void OnPreviewGUI(
        Rect rect,
        GUIStyle background)
    {
        DrawTailPreview(
            rect,
            compact: true,
            interactive: false);
    }

    private void OnDisable()
    {
        ReleasePreviewTexture();

        if (GUIUtility.hotControl != 0 &&
            (activeCenterHandle >= 0 ||
             activeWidthHandle >= 0))
        {
            GUIUtility.hotControl = 0;
        }

        activeCenterHandle = -1;
        activeWidthHandle = -1;
    }

    private void DrawTailEditorSection()
    {
        BattleShowPresentationManager manager =
            Manager;

        if (manager == null)
            return;

        SerializedProperty styleProperty =
            serializedObject.FindProperty(
                TailStylePropertyName);

        if (styleProperty == null)
        {
            EditorGUILayout.HelpBox(
                "speechBubbleTailStyle SerializedProperty를 찾지 못했습니다.",
                MessageType.Error);
            return;
        }

        EditorGUILayout.LabelField(
            "말풍선 꼬리 스타일",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "아래 변수와 Preview는 같은 Runtime Builder를 사용합니다. " +
            "Preview의 ROOT/P1/P2/P3/TIP을 직접 드래그하면 Pivot이 수정되고, " +
            "흰색 W 핸들을 드래그하면 해당 Pivot의 Half Width가 수정됩니다. " +
            "Stroke 두께는 바로 위 Stroke Root→Tip 값으로 조절하세요.",
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
            InvalidatePreview();
            Repaint();
            SceneView.RepaintAll();
        }

        BattleSpeechBubbleTailStyle style =
            manager.SpeechBubbleTailStyle;

        EditorGUILayout.Space(6f);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("기본 삼각형"))
            {
                ApplyPreset(
                    style,
                    lightning: false);
            }

            if (GUILayout.Button("번개형"))
            {
                ApplyPreset(
                    style,
                    lightning: true);
            }

            previewLeft =
                GUILayout.Toggle(
                    previewLeft,
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

    private void ApplyPreset(
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
                ? "Apply Speech Tail Lightning Preset"
                : "Apply Speech Tail Triangle Preset");

        if (lightning)
            style.ApplyLightningPreset();
        else
            style.ApplyTrianglePreset();

        EditorUtility.SetDirty(manager);

        serializedObject.Update();

        InvalidatePreview();
        Repaint();
        SceneView.RepaintAll();
    }

    private void DrawTailPreview(
        Rect rect,
        bool compact,
        bool interactive)
    {
        BattleShowPresentationManager manager =
            Manager;

        if (manager == null)
            return;

        BattleSpeechBubbleTailStyle style =
            manager.SpeechBubbleTailStyle;

        EnsurePreviewTexture(style);

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
                previewLeft
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

        if (previewTexture == null)
            return;

        Rect tailRect =
            CalculateTailPreviewRect(
                bubbleOuter,
                style,
                compact,
                previewLeft);

        Matrix4x4 oldMatrix =
            GUI.matrix;

        if (previewLeft)
        {
            GUIUtility.ScaleAroundPivot(
                new Vector2(-1f, 1f),
                tailRect.center);
        }

        GUI.DrawTexture(
            tailRect,
            previewTexture,
            ScaleMode.StretchToFill,
            true);

        GUI.matrix =
            oldMatrix;

        if (!compact)
        {
            DrawCenterLine(
                tailRect,
                style,
                previewLeft);

            if (interactive)
            {
                DrawInteractiveHandles(
                    tailRect,
                    style,
                    previewLeft);
            }
            else
            {
                DrawPassiveCenterMarkers(
                    tailRect,
                    style,
                    previewLeft);
            }
        }

        GUI.Label(
            new Rect(
                rect.x + 8f,
                rect.y + 6f,
                rect.width - 16f,
                18f),
            previewLeft
                ? "LEFT PREVIEW"
                : "RIGHT PREVIEW",
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
        bool flipped)
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
            DrawCenterHandle(
                i,
                labels[i],
                colors[i],
                points,
                tailRect,
                flipped);
        }

        for (int i = 0; i < 4; i++)
        {
            DrawWidthHandle(
                i,
                points,
                tailRect,
                flipped);
        }
    }

    private void DrawCenterHandle(
        int index,
        string label,
        Color color,
        Vector2[] points,
        Rect tailRect,
        bool flipped)
    {
        Vector2 position =
            NormalizedToGui(
                points[index],
                tailRect,
                flipped);

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

            activeCenterHandle =
                index;

            activeWidthHandle =
                -1;

            Undo.RecordObject(
                Manager,
                $"Move Speech Tail {label}");

            current.Use();
        }

        if (GUIUtility.hotControl == controlId &&
            activeCenterHandle == index)
        {
            if (current.type == EventType.MouseDrag)
            {
                Vector2 normalized =
                    GuiToNormalized(
                        current.mousePosition,
                        tailRect,
                        flipped);

                WriteCenterPoint(
                    index,
                    normalized);

                current.Use();
            }
            else if (current.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                activeCenterHandle = -1;
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
        int index,
        Vector2[] points,
        Rect tailRect,
        bool flipped)
    {
        Vector2 center =
            NormalizedToGui(
                points[index],
                tailRect,
                flipped);

        Vector2 tangent =
            ResolveGuiTangent(
                points,
                index,
                tailRect,
                flipped);

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

        float halfWidth =
            ReadWidthValue(index);

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

            activeWidthHandle =
                index;

            activeCenterHandle =
                -1;

            Undo.RecordObject(
                Manager,
                $"Resize Speech Tail Width {index}");

            current.Use();
        }

        if (GUIUtility.hotControl == controlId &&
            activeWidthHandle == index)
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
                    index,
                    normalizedWidth);

                current.Use();
            }
            else if (current.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                activeWidthHandle = -1;
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

    private void DrawPassiveCenterMarkers(
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
                TailStylePropertyName);

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

        InvalidatePreview();
        Repaint();
        SceneView.RepaintAll();
    }

    private void WriteWidthValue(
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
                TailStylePropertyName);

        if (style == null)
            return;

        SerializedProperty width =
            style.FindPropertyRelative(
                WidthPropertyNames[index]);

        width.floatValue =
            value;

        serializedObject.ApplyModifiedProperties();

        EditorUtility.SetDirty(manager);

        InvalidatePreview();
        Repaint();
        SceneView.RepaintAll();
    }

    private float ReadWidthValue(int index)
    {
        BattleSpeechBubbleTailStyle style =
            Manager != null
                ? Manager.SpeechBubbleTailStyle
                : null;

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
        BattleSpeechBubbleTailStyle style)
    {
        int hash =
            style != null
                ? style.ComputeHash()
                : 0;

        if (previewTexture != null &&
            previewHash == hash)
        {
            return;
        }

        ReleasePreviewTexture();

        previewHash =
            hash;

        previewTexture =
            BattleSpeechBubbleTailTextureBuilder
                .BuildTexture(
                    style,
                    new Color(
                        0.97f,
                        0.97f,
                        0.94f,
                        1f),
                    "BattleSpeechTail_EditorPreview");
    }

    private void InvalidatePreview()
    {
        previewHash =
            int.MinValue;

        ReleasePreviewTexture();
    }

    private void ReleasePreviewTexture()
    {
        if (previewTexture == null)
            return;

        DestroyImmediate(previewTexture);
        previewTexture = null;
    }
}
#endif
