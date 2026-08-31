using UnityEngine;

/// <summary>
/// Synergy Resolver와 VFX 교체 경로만 독립적으로 확인하는 개발용 UI입니다.
/// F3로 표시/숨김. 실제 Sprite가 연결돼 있으면 Sprite VFX,
/// 없으면 SynergyManager의 코드 기반 Dummy VFX가 재생됩니다.
/// </summary>
public class SynergyDummyUI : MonoBehaviour
{
    [SerializeField] private SynergyManager synergyManager;
    [SerializeField] private bool visible = true;

    private Vector2 scroll;

    private void Awake()
    {
        if (synergyManager == null)
            synergyManager = FindFirstObjectByType<SynergyManager>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F3))
            visible = !visible;
    }

    private void OnGUI()
    {
        if (!visible)
            return;

        float width = 410f;
        float height = Mathf.Min(560f, Screen.height - 24f);
        float x = Mathf.Max(12f, Screen.width - width - 12f);
        float y = Mathf.Max(12f, Screen.height - height - 12f);

        GUILayout.BeginArea(new Rect(x, y, width, height), GUI.skin.box);
        GUILayout.Label("SYNERGY RESOLVER / VFX DUMMY");
        GUILayout.Label("F3: Toggle   Sprite null = Code Dummy VFX");
        GUILayout.Space(6f);

        if (synergyManager == null)
        {
            GUILayout.Label("SynergyManager: NULL");
            if (GUILayout.Button("AUTO FIND"))
                synergyManager = FindFirstObjectByType<SynergyManager>();

            GUILayout.EndArea();
            return;
        }

        GUILayout.Label($"Active Synergy: {synergyManager.ActiveSynergies.Count}");
        GUILayout.Label($"Damage x{synergyManager.ActiveDamageMultiplier:0.00}");
        GUILayout.Label($"Move x{synergyManager.ActiveMoveSpeedMultiplier:0.00}");
        GUILayout.Label($"Break x{synergyManager.ActiveBreakPowerMultiplier:0.00}");
        GUILayout.Label($"Explosion Radius x{synergyManager.ActiveExplosionRadiusMultiplier:0.00}");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("RESOLVE NOW"))
            synergyManager.ResolveSynergies();
        if (GUILayout.Button("PREVIEW ACTIVE"))
            synergyManager.PreviewAllActive();
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
        scroll = GUILayout.BeginScrollView(scroll);

        for (int i = 0; i < synergyManager.Rules.Count; i++)
        {
            SynergyRule rule = synergyManager.Rules[i];
            if (rule == null) continue;

            bool active = synergyManager.IsActive(rule.synergyId);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"{(active ? "[ACTIVE]" : "[LOCKED]")} {rule.displayName}");
            GUILayout.Label($"ID: {rule.synergyId}");

            if (!string.IsNullOrWhiteSpace(rule.description))
                GUILayout.Label(rule.description);

            GUILayout.Label($"Need: {FormatRequirements(rule)}");
            GUILayout.Label($"VFX fallback: {rule.dummyVisual}");

            if (GUILayout.Button("PREVIEW VFX"))
                synergyManager.PreviewSynergy(rule.synergyId);

            GUILayout.EndVertical();
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private static string FormatRequirements(SynergyRule rule)
    {
        if (rule == null || rule.requirements == null || rule.requirements.Count == 0)
            return "None";

        string result = string.Empty;
        for (int i = 0; i < rule.requirements.Count; i++)
        {
            SynergyTagRequirement requirement = rule.requirements[i];
            if (requirement == null) continue;

            if (!string.IsNullOrEmpty(result))
                result += " + ";

            result += $"{requirement.tag} x{Mathf.Max(1, requirement.count)}";
        }

        return result;
    }
}
