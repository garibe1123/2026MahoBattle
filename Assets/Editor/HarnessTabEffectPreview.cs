using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BattleShowBroadcastNoiseController))]
public sealed class HarnessTabEffectPreview : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        BattleShowBroadcastNoiseController controller = (BattleShowBroadcastNoiseController)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("TAB Effect Preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Play mode: enable preview during combat to view TAB effects without holding TAB. " +
            "Enable Use Tab Effect Tuning to apply this component's sliders, including camera grain, " +
            "camera noise and static. Scrolling texture effects remain disabled. " +
            "Play mode changes are temporary; copy component values before stopping and paste afterward to keep them.",
            MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying || !controller.isActiveAndEnabled))
        {
            bool preview = EditorGUILayout.Toggle("Preview TAB effects", controller.TabPreview);
            if (preview != controller.TabPreview) controller.SetTabPreview(preview);
            if (GUILayout.Button("Stop preview")) controller.SetTabPreview(false);
        }
    }

    private void OnDisable()
    {
        if (Application.isPlaying && target is BattleShowBroadcastNoiseController controller && controller.TabPreview)
            controller.SetTabPreview(false);
    }

    public override bool RequiresConstantRepaint() => Application.isPlaying;
}
