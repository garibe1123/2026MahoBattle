#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// BattleUniversalStageLightRigSO의 Base ↔ Head Anchor 결합을 Inspector에서 바로 확인하는 Editor입니다.
/// 런타임 배치 로직과 동일한 normalized anchor 계산을 사용합니다.
/// </summary>
[CustomEditor(typeof(BattleUniversalStageLightRigSO))]
public sealed class BattleUniversalStageLightRigSOEditor : Editor
{
    private const float PreviewHeight = 260f;
    private const float PreviewPadding = 18f;

    private SerializedProperty baseSprite;
    private SerializedProperty baseScale;
    private SerializedProperty baseLocalOffset;
    private SerializedProperty baseMountAnchor;
    private SerializedProperty lightHeadVariants;
    private SerializedProperty headScale;
    private SerializedProperty headConnectorAnchor;
    private SerializedProperty headFineOffset;
    private SerializedProperty overallScale;

    private void OnEnable()
    {
        baseSprite = serializedObject.FindProperty("baseSprite");
        baseScale = serializedObject.FindProperty("baseScale");
        baseLocalOffset = serializedObject.FindProperty("baseLocalOffset");
        baseMountAnchor = serializedObject.FindProperty("baseMountAnchor");
        lightHeadVariants = serializedObject.FindProperty("lightHeadVariants");
        headScale = serializedObject.FindProperty("headScale");
        headConnectorAnchor = serializedObject.FindProperty("headConnectorAnchor");
        headFineOffset = serializedObject.FindProperty("headFineOffset");
        overallScale = serializedObject.FindProperty("overallScale");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("LIGHT RIG CONNECTION PREVIEW", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Base Mount Anchor와 Head Connector Anchor가 같은 점에 맞춰진 결과를 미리 봅니다. " +
            "카메라/조명 Rig는 상하 반전이나 180도 회전을 사용하지 않습니다.",
            MessageType.Info);

        Sprite baseValue = baseSprite != null ? baseSprite.objectReferenceValue as Sprite : null;
        Sprite headValue = ResolveFirstHeadSprite();

        if (baseValue == null)
        {
            EditorGUILayout.HelpBox("Base Sprite가 필요합니다.", MessageType.Warning);
            return;
        }

        if (headValue == null)
        {
            EditorGUILayout.HelpBox("Light Head Variant를 하나 이상 지정하세요.", MessageType.Warning);
            return;
        }

        Rect preview = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
        DrawPreview(preview, baseValue, headValue);
        Repaint();
    }

    private Sprite ResolveFirstHeadSprite()
    {
        if (lightHeadVariants == null || !lightHeadVariants.isArray)
            return null;

        for (int i = 0; i < lightHeadVariants.arraySize; i++)
        {
            Sprite sprite = lightHeadVariants.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
            if (sprite != null)
                return sprite;
        }
        return null;
    }

    private void DrawPreview(Rect area, Sprite baseValue, Sprite headValue)
    {
        EditorGUI.DrawRect(area, new Color(0.08f, 0.085f, 0.095f, 1f));

        Vector2 baseS = SafeScale(baseScale != null ? baseScale.vector2Value : Vector2.one);
        Vector2 headS = SafeScale(headScale != null ? headScale.vector2Value : Vector2.one);
        Vector2 overall = SafeScale(overallScale != null ? overallScale.vector2Value : Vector2.one);
        baseS = Vector2.Scale(baseS, overall);
        headS = Vector2.Scale(headS, overall);

        Vector2 baseOffset = baseLocalOffset != null ? baseLocalOffset.vector2Value : Vector2.zero;
        Vector2 baseAnchor = Clamp01(baseMountAnchor != null ? baseMountAnchor.vector2Value : new Vector2(0.5f, 0.78f));
        Vector2 headAnchor = Clamp01(headConnectorAnchor != null ? headConnectorAnchor.vector2Value : new Vector2(0.5f, 0.10f));
        Vector2 fine = headFineOffset != null ? headFineOffset.vector2Value : Vector2.zero;

        Rect baseLocal = ResolveSpriteLocalRect(baseValue, baseOffset, baseS);
        Vector2 mount = ResolveAnchorLocal(baseValue, baseAnchor, baseS) + baseOffset;
        Vector2 headConnector = ResolveAnchorLocal(headValue, headAnchor, headS);
        Vector2 headPosition = mount + fine - headConnector;
        Rect headLocal = ResolveSpriteLocalRect(headValue, headPosition, headS);

        Rect union = Union(baseLocal, headLocal);
        if (union.width <= 0.001f || union.height <= 0.001f)
            return;

        Rect content = new(
            area.x + PreviewPadding,
            area.y + PreviewPadding,
            Mathf.Max(1f, area.width - PreviewPadding * 2f),
            Mathf.Max(1f, area.height - PreviewPadding * 2f));

        float scale = Mathf.Min(content.width / union.width, content.height / union.height);
        Vector2 renderedSize = new(union.width * scale, union.height * scale);
        Vector2 origin = new(
            content.center.x - renderedSize.x * 0.5f - union.xMin * scale,
            content.center.y + renderedSize.y * 0.5f + union.yMin * scale);

        DrawSpriteLocal(baseValue, baseLocal, origin, scale);
        DrawSpriteLocal(headValue, headLocal, origin, scale);

        Vector2 mountGui = LocalToGui(mount, origin, scale);
        Vector2 connectorGui = LocalToGui(headPosition + headConnector, origin, scale);

        Handles.BeginGUI();
        Handles.color = new Color(0.15f, 0.95f, 0.95f, 1f);
        Handles.DrawSolidDisc(mountGui, Vector3.forward, 4f);
        Handles.color = new Color(1f, 0.78f, 0.12f, 1f);
        Handles.DrawWireDisc(connectorGui, Vector3.forward, 7f);
        Handles.color = Color.white;
        Handles.DrawLine(new Vector3(area.x, area.center.y), new Vector3(area.xMax, area.center.y));
        Handles.DrawLine(new Vector3(area.center.x, area.y), new Vector3(area.center.x, area.yMax));
        Handles.EndGUI();

        GUIStyle label = new(EditorStyles.miniLabel)
        {
            normal = { textColor = Color.white }
        };
        GUI.Label(new Rect(area.x + 8f, area.y + 6f, 240f, 18f), "Cyan = Base Mount / Yellow = Head Connector", label);
    }

    private static void DrawSpriteLocal(Sprite sprite, Rect localRect, Vector2 origin, float scale)
    {
        if (sprite == null || sprite.texture == null)
            return;

        Rect guiRect = new(
            origin.x + localRect.xMin * scale,
            origin.y - localRect.yMax * scale,
            localRect.width * scale,
            localRect.height * scale);

        Rect textureRect = sprite.textureRect;
        Texture2D texture = sprite.texture;
        Rect uv = new(
            textureRect.x / texture.width,
            textureRect.y / texture.height,
            textureRect.width / texture.width,
            textureRect.height / texture.height);

        GUI.DrawTextureWithTexCoords(guiRect, texture, uv, true);
    }

    private static Rect ResolveSpriteLocalRect(Sprite sprite, Vector2 position, Vector2 scale)
    {
        Bounds b = sprite.bounds;
        float x0 = position.x + b.min.x * scale.x;
        float x1 = position.x + b.max.x * scale.x;
        float y0 = position.y + b.min.y * scale.y;
        float y1 = position.y + b.max.y * scale.y;
        return Rect.MinMaxRect(
            Mathf.Min(x0, x1),
            Mathf.Min(y0, y1),
            Mathf.Max(x0, x1),
            Mathf.Max(y0, y1));
    }

    private static Vector2 ResolveAnchorLocal(Sprite sprite, Vector2 normalized, Vector2 scale)
    {
        Bounds b = sprite.bounds;
        Vector2 p = new(
            Mathf.Lerp(b.min.x, b.max.x, normalized.x),
            Mathf.Lerp(b.min.y, b.max.y, normalized.y));
        return Vector2.Scale(p, scale);
    }

    private static Vector2 LocalToGui(Vector2 local, Vector2 origin, float scale)
    {
        return new Vector2(origin.x + local.x * scale, origin.y - local.y * scale);
    }

    private static Rect Union(Rect a, Rect b)
    {
        return Rect.MinMaxRect(
            Mathf.Min(a.xMin, b.xMin),
            Mathf.Min(a.yMin, b.yMin),
            Mathf.Max(a.xMax, b.xMax),
            Mathf.Max(a.yMax, b.yMax));
    }

    private static Vector2 SafeScale(Vector2 value)
    {
        return new Vector2(
            Mathf.Approximately(value.x, 0f) ? 1f : value.x,
            Mathf.Approximately(value.y, 0f) ? 1f : value.y);
    }

    private static Vector2 Clamp01(Vector2 value)
    {
        return new Vector2(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y));
    }
}
#endif
