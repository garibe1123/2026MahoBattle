#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BattleShowPresentationManager))]
public sealed class BattleShowPresentationManagerEditor : Editor
{
    private const float PreviewHeight = 250f;

    private Texture2D previewTexture;
    private int previewHash = int.MinValue;
    private bool previewLeft;

    private BattleShowPresentationManager Manager =>
        target as BattleShowPresentationManager;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10f);
        DrawTailPreviewSection();
    }

    public override bool HasPreviewGUI()
    {
        return true;
    }

    public override void OnPreviewGUI(
        Rect rect,
        GUIStyle background)
    {
        DrawTailPreview(rect, compact: true);
    }

    private void OnDisable()
    {
        ReleasePreviewTexture();
    }

    private void DrawTailPreviewSection()
    {
        BattleShowPresentationManager manager =
            Manager;

        if (manager == null)
            return;

        BattleSpeechBubbleTailStyle style =
            manager.SpeechBubbleTailStyle;

        EditorGUILayout.LabelField(
            "말풍선 꼬리 실시간 Preview",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "위의 '말풍선 꼬리 스타일' 값을 수정하면 이 Preview와 Play Mode 꼬리가 동일한 Builder로 다시 생성됩니다. " +
            "Pivot1/2/3의 Y를 서로 반대로 꺾으면 번개형, Stroke Root→Tip 값을 다르게 주면 좌→우 두께 변화가 생깁니다.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("기본 삼각형"))
            {
                Undo.RecordObject(
                    manager,
                    "Apply Speech Tail Triangle Preset");

                style.ApplyTrianglePreset();
                EditorUtility.SetDirty(manager);
                InvalidatePreview();
            }

            if (GUILayout.Button("번개형"))
            {
                Undo.RecordObject(
                    manager,
                    "Apply Speech Tail Lightning Preset");

                style.ApplyLightningPreset();
                EditorUtility.SetDirty(manager);
                InvalidatePreview();
            }

            previewLeft =
                GUILayout.Toggle(
                    previewLeft,
                    "왼쪽 Flip",
                    "Button",
                    GUILayout.Width(90f));
        }

        EditorGUILayout.Space(4f);

        Rect previewRect =
            GUILayoutUtility.GetRect(
                100f,
                PreviewHeight,
                GUILayout.ExpandWidth(true));

        DrawTailPreview(
            previewRect,
            compact: false);

        EditorGUILayout.Space(4f);

        EditorGUILayout.LabelField(
            $"Stroke Root → Tip : " +
            $"{style.strokeRoot:0.#} / " +
            $"{style.strokePivot1:0.#} / " +
            $"{style.strokePivot2:0.#} / " +
            $"{style.strokePivot3:0.#} / " +
            $"{style.strokeTip:0.#}",
            EditorStyles.miniLabel);

        if (GUI.changed)
        {
            EditorUtility.SetDirty(manager);
            InvalidatePreview();
            Repaint();
            SceneView.RepaintAll();
        }
    }

    private void DrawTailPreview(
        Rect rect,
        bool compact)
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
            compact ? 8f : 16f;

        float bubbleHeight =
            Mathf.Min(
                compact ? 72f : 104f,
                rect.height - margin * 2f);

        float bubbleWidth =
            Mathf.Max(
                80f,
                rect.width * 0.52f);

        Rect bubbleOuter =
            new Rect(
                previewLeft
                    ? rect.xMax - margin - bubbleWidth
                    : rect.x + margin,
                rect.center.y - bubbleHeight * 0.5f,
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

        float sourceAspect =
            Mathf.Max(
                0.1f,
                style.uiSize.x /
                Mathf.Max(
                    1f,
                    style.uiSize.y));

        float tailHeight =
            Mathf.Min(
                bubbleHeight * 0.82f,
                compact ? 66f : 92f);

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
                tailWidth * 0.80f);

        Rect tailRect;

        if (!previewLeft)
        {
            tailRect =
                new Rect(
                    bubbleOuter.xMax -
                    previewOverlap,
                    bubbleOuter.center.y -
                    tailHeight * 0.5f,
                    tailWidth,
                    tailHeight);
        }
        else
        {
            tailRect =
                new Rect(
                    bubbleOuter.xMin -
                    tailWidth +
                    previewOverlap,
                    bubbleOuter.center.y -
                    tailHeight * 0.5f,
                    tailWidth,
                    tailHeight);
        }

        Matrix4x4 oldMatrix =
            GUI.matrix;

        if (previewLeft)
        {
            Vector2 pivot =
                tailRect.center;

            GUIUtility.ScaleAroundPivot(
                new Vector2(-1f, 1f),
                pivot);
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
            DrawPivotMarkers(
                tailRect,
                style,
                previewLeft);
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

    private void DrawPivotMarkers(
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
            new Color(0.40f, 0.92f, 1f, 1f),
            new Color(1f, 0.78f, 0.20f, 1f),
            new Color(1f, 0.48f, 0.25f, 1f),
            new Color(0.90f, 0.28f, 0.80f, 1f),
            new Color(0.35f, 1f, 0.48f, 1f)
        };

        Vector2 previous =
            Vector2.zero;

        for (int i = 0; i < points.Length; i++)
        {
            float normalizedX =
                flipped
                    ? 1f - points[i].x
                    : points[i].x;

            Vector2 position =
                new Vector2(
                    tailRect.x +
                    normalizedX *
                    tailRect.width,
                    tailRect.yMax -
                    points[i].y *
                    tailRect.height);

            if (i > 0)
            {
                Handles.BeginGUI();
                Handles.color =
                    new Color(
                        1f,
                        1f,
                        1f,
                        0.34f);

                Handles.DrawLine(
                    previous,
                    position);

                Handles.EndGUI();
            }

            Rect marker =
                new Rect(
                    position.x - 4f,
                    position.y - 4f,
                    8f,
                    8f);

            EditorGUI.DrawRect(
                marker,
                colors[i]);

            GUI.Label(
                new Rect(
                    position.x + 6f,
                    position.y - 8f,
                    42f,
                    16f),
                labels[i],
                EditorStyles.miniLabel);

            previous =
                position;
        }
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
