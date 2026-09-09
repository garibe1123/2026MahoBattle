using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 1~9 숫자키를 전투 중 장비 직접 선택에만 사용합니다.
/// BattleEquipmentSystem의 Legacy 입력은 Reward/Map에서도 반응할 수 있으므로 끄고,
/// 이 컨트롤러가 Combat 상태에서만 같은 EquipSlot API를 호출합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30100)]
public sealed class BattleCombatNumberKeyEquipController : MonoBehaviour
{
    private static readonly KeyCode[] SlotKeys =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3,
        KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
        KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9
    };

    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;

    private void Update()
    {
        ResolveReferences();
        if (runManager == null || equipmentSystem == null || BattlePauseController.IsPaused)
            return;
        if (!runManager.RunActive || runManager.State != BattleRunState.Combat)
            return;

        int count = Mathf.Min(equipmentSystem.UnlockedSlotCount, SlotKeys.Length);
        for (int i = 0; i < count; i++)
        {
            if (!Input.GetKeyDown(SlotKeys[i]))
                continue;

            equipmentSystem.EquipSlot(i);
            break;
        }
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
    }
}

public static class BattleCombatNumberKeyEquipAutoInstaller
{
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
        if (EditorApplication.isPlayingOrWillChangePlaymode || installQueued)
            return;
        installQueued = true;
        EditorApplication.delayCall += EnsureEditorComponents;
    }

    private static void EnsureEditorComponents()
    {
        installQueued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        BattleSceneManager[] managers = Resources.FindObjectsOfTypeAll<BattleSceneManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null || EditorUtility.IsPersistent(manager) ||
                !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded)
                continue;

            if (manager.GetComponent<BattleCombatNumberKeyEquipController>() != null)
                continue;

            Undo.AddComponent<BattleCombatNumberKeyEquipController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponents()
    {
        BattleSceneManager[] managers = Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.GetComponent<BattleCombatNumberKeyEquipController>() == null)
                manager.gameObject.AddComponent<BattleCombatNumberKeyEquipController>();
        }
    }
}
