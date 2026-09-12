using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

/// <summary>
/// BattleSystems 루트에 흩어져 있던 Sprite / Show / Lighting presentation 컴포넌트를
/// BattleSystems/SpriteManager 자식으로 정리하는 Editor-only organizer입니다.
///
/// 런타임에 hierarchy를 재구축하지 않습니다. 씬을 열거나 스크립트가 리컴파일될 때
/// Editor에서 한 번 정리하고 Scene에 저장되므로 첫 전투 프레임의 AddComponent 비용을 만들지 않습니다.
/// </summary>
public static class BattleSpriteManagerOrganizer
{
    public const string SpriteManagerObjectName = "SpriteManager";

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void InstallEditorHooks()
    {
        EditorApplication.delayCall -= OrganizeLoadedBattleScenes;
        EditorApplication.delayCall += OrganizeLoadedBattleScenes;

        EditorSceneManager.sceneOpened -= HandleSceneOpened;
        EditorSceneManager.sceneOpened += HandleSceneOpened;
    }

    private static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall -= OrganizeLoadedBattleScenes;
        EditorApplication.delayCall += OrganizeLoadedBattleScenes;
    }

    [MenuItem("Tools/Battle/Organize Sprite Manager")]
    private static void OrganizeLoadedBattleScenesFromMenu()
    {
        OrganizeLoadedBattleScenes();
    }

    private static void OrganizeLoadedBattleScenes()
    {
        if (Application.isPlaying)
            return;

        BattleSceneManager[] managers = Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null || !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded)
                continue;

            Organize(manager);
        }
    }

    private static void Organize(BattleSceneManager manager)
    {
        if (manager == null)
            return;

        bool changed = false;
        Transform spriteRoot = manager.transform.Find(SpriteManagerObjectName);
        if (spriteRoot == null)
        {
            GameObject rootObject = new(SpriteManagerObjectName);
            Undo.RegisterCreatedObjectUndo(rootObject, "Create Battle SpriteManager");
            rootObject.transform.SetParent(manager.transform, false);
            spriteRoot = rootObject.transform;
            changed = true;
        }

        if (spriteRoot.localPosition != Vector3.zero ||
            spriteRoot.localRotation != Quaternion.identity ||
            spriteRoot.localScale != Vector3.one)
        {
            Undo.RecordObject(spriteRoot, "Reset Battle SpriteManager Transform");
            spriteRoot.localPosition = Vector3.zero;
            spriteRoot.localRotation = Quaternion.identity;
            spriteRoot.localScale = Vector3.one;
            changed = true;
        }

        // World Sprite / Show Set
        changed |= EnsureOnSpriteManager<BattleShowPresentationManager>(manager, spriteRoot);
        changed |= EnsureOnSpriteManager<BattleShowWorldSetController>(manager, spriteRoot);
        changed |= EnsureOnSpriteManager<BattleShowSetDecorationController>(manager, spriteRoot);
        changed |= EnsureOnSpriteManager<BattleShowSharedTvContentController>(manager, spriteRoot);
        changed |= EnsureOnSpriteManager<BattleShowFocusController>(manager, spriteRoot);

        // Lighting / screen presentation
        changed |= EnsureOnSpriteManager<BattleCombatLightPolicyController>(manager, spriteRoot);
        changed |= EnsureOnSpriteManager<BattleCombatCornerVignetteController>(manager, spriteRoot);
        changed |= EnsureOnSpriteManager<BattlePlayerStageLightingController>(manager, spriteRoot);
        changed |= EnsureOnSpriteManager<BattleShowBroadcastNoiseController>(manager, spriteRoot);
        changed |= EnsureOnSpriteManager<BattleSpotlightBeamDirectionController>(manager, spriteRoot);

        // PACK의 Combat <-> Reward Choice 시각 전환도 visual presentation 설정이므로 함께 관리합니다.
        changed |= EnsureOnSpriteManager<BattleMiniPackContextTweenController>(manager, spriteRoot);

        if (!changed)
            return;

        EditorUtility.SetDirty(manager.gameObject);
        EditorUtility.SetDirty(spriteRoot.gameObject);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
    }

    /// <summary>
    /// 기존 BattleSystems 루트에 자동 설치돼 있던 컴포넌트는 Inspector 값을 보존해 SpriteManager로 이동합니다.
    /// 사용자가 다른 별도 GameObject에 명시적으로 배치한 컴포넌트는 임의로 이동하지 않습니다.
    /// </summary>
    private static bool EnsureOnSpriteManager<T>(BattleSceneManager manager, Transform spriteRoot)
        where T : Component
    {
        if (manager == null || spriteRoot == null)
            return false;

        T alreadyOrganized = spriteRoot.GetComponent<T>();
        if (alreadyOrganized != null)
            return false;

        T rootComponent = manager.GetComponent<T>();
        if (rootComponent != null)
        {
            T moved = Undo.AddComponent<T>(spriteRoot.gameObject);
            EditorUtility.CopySerialized(rootComponent, moved);
            Undo.DestroyObjectImmediate(rootComponent);
            EditorUtility.SetDirty(moved);
            return true;
        }

        // 다른 오브젝트에 사용자가 직접 배치해 둔 인스턴스가 있으면 그 배치를 존중합니다.
        T[] existing = Object.FindObjectsByType<T>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < existing.Length; i++)
        {
            T component = existing[i];
            if (component != null && component.gameObject.scene == manager.gameObject.scene)
                return false;
        }

        T created = Undo.AddComponent<T>(spriteRoot.gameObject);
        EditorUtility.SetDirty(created);
        return true;
    }
#endif
}
