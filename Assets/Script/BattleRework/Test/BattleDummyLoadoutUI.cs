using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Run 시작 전 Main Core / Sub Core Loadout을 검증하는 더미 IMGUI 패널입니다.
/// 최종 Loadout 화면을 만들기 전 기능 테스트 용도입니다.
/// </summary>
public class BattleDummyLoadoutUI : MonoBehaviour
{
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private PlayerLoadout loadout;
    [SerializeField] private List<CoreDefinitionSO> selectableCores = new();
    [SerializeField] private bool showDeveloperButtons = true;

    private Vector2 scroll;

    private void Awake()
    {
        if (runManager == null) runManager = FindFirstObjectByType<BattleRunManager>();
        if (loadout == null) loadout = FindFirstObjectByType<PlayerLoadout>();

        if (selectableCores == null)
            selectableCores = new List<CoreDefinitionSO>();

        if (selectableCores.Count == 0)
        {
            CoreDefinitionSO[] loaded = Resources.FindObjectsOfTypeAll<CoreDefinitionSO>();
            for (int i = 0; i < loaded.Length; i++)
            {
                if (loaded[i] != null && !selectableCores.Contains(loaded[i]))
                    selectableCores.Add(loaded[i]);
            }
        }
    }

    private void OnGUI()
    {
        if (runManager == null || loadout == null)
            return;

        // Loadout은 런 중 수정하지 않습니다.
        if (runManager.RunActive)
            return;

        float width = 420f;
        float height = Mathf.Min(600f, Screen.height - 24f);
        float x = Mathf.Max(12f, Screen.width - width - 12f);

        GUILayout.BeginArea(new Rect(x, 12f, width, height), GUI.skin.window);
        GUILayout.Label("CORE LOADOUT / DUMMY");
        GUILayout.Label("런 시작 전 장착. 최종 UI 제작 전 기능 검증용.");
        GUILayout.Space(8f);

        scroll = GUILayout.BeginScrollView(scroll);

        DrawMainCore();
        GUILayout.Space(12f);
        DrawSubCores();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawMainCore()
    {
        GUILayout.Label("MAIN CORE 1");
        string current = loadout.MainCore != null
            ? GetCoreName(loadout.MainCore)
            : "EMPTY";
        GUILayout.Label($"Current: {current}");

        if (loadout.MainCore != null)
        {
            GUILayout.Label(loadout.MainCore.synergyDescription ?? string.Empty);
            GUILayout.Label($"Damage x{loadout.MainCore.damageMultiplier:0.00} / Move x{loadout.MainCore.moveSpeedMultiplier:0.00}");
        }

        if (selectableCores.Count == 0)
        {
            GUILayout.Label("로드된 CoreDefinitionSO가 없습니다.");
            return;
        }

        for (int i = 0; i < selectableCores.Count; i++)
        {
            CoreDefinitionSO core = selectableCores[i];
            if (core == null) continue;

            if (GUILayout.Button($"Set Main: {GetCoreName(core)}", GUILayout.Height(28f)))
                loadout.SetMainCore(core);
        }

        if (showDeveloperButtons && GUILayout.Button("CLEAR MAIN"))
            loadout.SetMainCore(null);
    }

    private void DrawSubCores()
    {
        GUILayout.Label($"SUB CORE {loadout.SubCores.Count}/{loadout.UnlockedSubCoreSlots}");

        for (int i = 0; i < loadout.SubCores.Count; i++)
        {
            CoreDefinitionSO core = loadout.SubCores[i];
            if (core == null) continue;

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(GetCoreName(core));
            if (GUILayout.Button("REMOVE", GUILayout.Width(80f)))
            {
                loadout.RemoveSubCore(core);
                GUILayout.EndHorizontal();
                return;
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(6f);
        GUILayout.Label("Available");

        for (int i = 0; i < selectableCores.Count; i++)
        {
            CoreDefinitionSO core = selectableCores[i];
            if (core == null || core == loadout.MainCore) continue;
            if (ContainsSubCore(core)) continue;

            if (GUILayout.Button($"Add Sub: {GetCoreName(core)}", GUILayout.Height(26f)))
                loadout.TryAddSubCore(core);
        }

        if (showDeveloperButtons)
        {
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("[TEST] SUB SLOT +1"))
                loadout.UnlockSubCoreSlots(1);
            if (GUILayout.Button("CLEAR SUB"))
                loadout.ClearSubCores();
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(8f);
        GUILayout.Label($"Combined Damage x{loadout.GetDamageMultiplier():0.00}");
        GUILayout.Label($"Combined Move x{loadout.GetMoveSpeedMultiplier():0.00}");
    }

    private bool ContainsSubCore(CoreDefinitionSO core)
    {
        for (int i = 0; i < loadout.SubCores.Count; i++)
        {
            if (loadout.SubCores[i] == core)
                return true;
        }

        return false;
    }

    private static string GetCoreName(CoreDefinitionSO core)
    {
        if (core == null) return "EMPTY";
        return string.IsNullOrWhiteSpace(core.coreName) ? core.name : core.coreName;
    }
}
