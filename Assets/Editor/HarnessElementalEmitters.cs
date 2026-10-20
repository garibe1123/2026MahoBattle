using UnityEditor;
using UnityEngine;

public static class HarnessElementalEmitters
{
    private const string Menu = "Tools/Harness/Create Elemental Emission Samples";

    [MenuItem(Menu, true)]
    private static bool CanCreate() => !Application.isPlaying && Selection.activeObject is EffectDefinitionSO;

    [MenuItem(Menu)]
    private static void Create()
    {
        EffectDefinitionSO definition = Selection.activeObject as EffectDefinitionSO;
        if (definition == null || Application.isPlaying) return;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create Elemental Emission Samples");
        GameObject root = new("Elemental Emission Samples");
        Undo.RegisterCreatedObjectUndo(root, "Create Elemental Emission Samples");
        EffectEmissionMode[] modes = { EffectEmissionMode.Stationary, EffectEmissionMode.Trail,
            EffectEmissionMode.Faucet, EffectEmissionMode.Line, EffectEmissionMode.Area, EffectEmissionMode.Explosion };
        for (int i = 0; i < modes.Length; i++)
        {
            GameObject host = new(modes[i].ToString());
            host.transform.SetParent(root.transform, false);
            host.transform.localPosition = new Vector3((i % 3 - 1) * 5f, i < 3 ? 2.5f : -2.5f, 0f);
            EffectEmitter emitter = host.AddComponent<EffectEmitter>();
            emitter.definition = definition;
            emitter.mode = modes[i];
            emitter.playOnStart = true;
            emitter.emissionDuration = -1f;
            emitter.spawnRadius = modes[i] == EffectEmissionMode.Area ? 1.5f : 0.12f;
            Undo.RegisterCreatedObjectUndo(host, "Create Elemental Emission Sample");
        }
        Selection.activeGameObject = root;
        Undo.CollapseUndoOperations(group);
    }
}
