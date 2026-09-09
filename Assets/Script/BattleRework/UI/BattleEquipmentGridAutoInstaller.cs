using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public static class BattleEquipmentGridAutoInstaller
{
    private const int MinimumBackpackSlots = 3;

#if UNITY_EDITOR
    private static bool installQueued;

    [InitializeOnLoadMethod]
    private static void InitializeEditorInstaller()
    {
        EditorApplication.hierarchyChanged -= QueueInstall;
        EditorApplication.hierarchyChanged += QueueInstall;
        QueueInstall();
    }

    private static void QueueInstall()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || installQueued) return;
        installQueued = true;
        EditorApplication.delayCall += EnsureEditorComponents;
    }

    private static void EnsureEditorComponents()
    {
        installQueued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        BattleSceneManager[] managers = Resources.FindObjectsOfTypeAll<BattleSceneManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null || EditorUtility.IsPersistent(manager) || !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded) continue;

            bool changed = false;
            BattleEquipmentSystem equipment = manager.GetComponent<BattleEquipmentSystem>();
            if (equipment != null)
            {
                if (!equipment.LegacyNumberKeyEquipEnabled)
                {
                    Undo.RecordObject(equipment, "Enable Number Key Equipment Input");
                    equipment.LegacyNumberKeyEquipEnabled = true;
                    changed = true;
                }

                if (equipment.UnlockedSlotCount < MinimumBackpackSlots)
                {
                    Undo.RecordObject(equipment, "Set Backpack Starting Slots");
                    equipment.SetUnlockedSlotCount(MinimumBackpackSlots);
                    changed = true;
                }

                if (changed) EditorUtility.SetDirty(equipment);
            }

            if (manager.GetComponent<BattleGridSynergyController>() == null)
            {
                Undo.AddComponent<BattleGridSynergyController>(manager.gameObject);
                changed = true;
            }

            if (manager.GetComponent<BattleKineticLoadoutUI>() == null)
            {
                Undo.AddComponent<BattleKineticLoadoutUI>(manager.gameObject);
                changed = true;
            }

            if (!changed) continue;
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponents()
    {
        BattleSceneManager[] managers = Object.FindObjectsByType<BattleSceneManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null) continue;

            BattleEquipmentSystem equipment = manager.GetComponent<BattleEquipmentSystem>();
            if (equipment != null)
            {
                equipment.LegacyNumberKeyEquipEnabled = true;
                if (equipment.UnlockedSlotCount < MinimumBackpackSlots) equipment.SetUnlockedSlotCount(MinimumBackpackSlots);
            }

            if (manager.GetComponent<BattleGridSynergyController>() == null) manager.gameObject.AddComponent<BattleGridSynergyController>();
            if (manager.GetComponent<BattleKineticLoadoutUI>() == null) manager.gameObject.AddComponent<BattleKineticLoadoutUI>();
        }
    }
}
