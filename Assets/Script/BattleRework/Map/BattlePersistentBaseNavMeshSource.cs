using NavMeshPlus.Components;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Persistent 4x4 Base가 Procedural Room의 중앙 NavMesh에서 빠지지 않도록
/// 정확한 4x4 Simple Sprite NavMesh source를 별도로 유지합니다.
///
/// 확장 Room 타일은 1x1 Simple SpriteRenderer를 NavMesh source로 사용하지만,
/// RoomBaseTemplate의 기본 Base는 하나의 Tiled SpriteRenderer를 4x4로 늘려 사용합니다.
/// NavMeshPlus의 2D source 수집에서 Tiled Sprite의 렌더 크기가 안정적으로 반영되지 않는 경우
/// 확장 타일이 의도적으로 비워 둔 중앙 4x4가 NavMesh hole로 남을 수 있습니다.
///
/// 이 컴포넌트는 Base 비주얼/SO/Collider를 변경하지 않습니다.
/// Navigation 아래에 투명한 4x4 FullRect Simple Sprite 하나만 추가해 bake source로 사용합니다.
/// </summary>
[DefaultExecutionOrder(-9500)]
[DisallowMultipleComponent]
public sealed class BattlePersistentBaseNavMeshSource : MonoBehaviour
{
    private const string SourceObjectName = "PersistentBaseNavMeshSource_4x4";

    [Header("Persistent Base NavMesh")]
    [SerializeField, Range(0.02f, 0.5f)] private float refreshInterval = 0.08f;
    [SerializeField] private bool keepSourceVisibleInHierarchy = true;

    private RoomBaseTemplate baseTemplate;
    private NavMeshSurface navSurface;
    private GameObject sourceObject;
    private SpriteRenderer sourceRenderer;
    private NavMeshModifier sourceModifier;
    private GameObject lastActiveBase;
    private Vector3 lastCenter = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
    private float nextRefresh;

    private static Sprite navSourceSprite;

    private void Awake()
    {
        if (Application.isPlaying)
            EnsureSourceNow();
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
            EnsureSourceNow();
    }

    private void Update()
    {
        if (!Application.isPlaying || Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + Mathf.Max(0.02f, refreshInterval);
        EnsureSourceNow();
    }

    /// <summary>
    /// 현재 Persistent Base 위치에 정확한 4x4 NavMesh source가 존재하도록 보장합니다.
    /// Base가 다음 스테이지 위치로 승격되거나 새 인스턴스로 교체되어도 동일 source를 이동해 재사용합니다.
    /// </summary>
    public void EnsureSourceNow()
    {
        ResolveReferences();

        GameObject activeBase = baseTemplate != null ? baseTemplate.ActiveBase : null;
        if (baseTemplate == null || navSurface == null || activeBase == null)
        {
            if (sourceObject != null && sourceObject.activeSelf)
                sourceObject.SetActive(false);
            return;
        }

        EnsureSourceObject();
        if (sourceObject == null)
            return;

        Vector3 center = baseTemplate.FixedCenterWorld;
        bool baseChanged = lastActiveBase != activeBase;
        bool centerChanged = (lastCenter - center).sqrMagnitude > 0.000001f;

        if (baseChanged || centerChanged || !sourceObject.activeSelf)
        {
            sourceObject.SetActive(true);
            sourceObject.layer = activeBase.layer;
            sourceObject.transform.position = center;
            sourceObject.transform.rotation = Quaternion.identity;
            ApplyWorldUnitScale(sourceObject.transform, navSurface.transform);

            lastActiveBase = activeBase;
            lastCenter = center;
        }

        if (sourceRenderer != null)
        {
            sourceRenderer.enabled = true;
            sourceRenderer.sprite = GetNavSourceSprite();
            sourceRenderer.drawMode = SpriteDrawMode.Simple;
            sourceRenderer.color = new Color(1f, 1f, 1f, 0f);
            sourceRenderer.sortingOrder = -32760;
        }

        if (sourceModifier != null)
        {
            sourceModifier.ignoreFromBuild = false;
            sourceModifier.overrideArea = false;
        }
    }

    private void ResolveReferences()
    {
        if (baseTemplate == null)
            baseTemplate = GetComponent<RoomBaseTemplate>();
        if (baseTemplate == null)
            baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();

        if (navSurface == null)
            navSurface = FindFirstObjectByType<NavMeshSurface>();
    }

    private void EnsureSourceObject()
    {
        if (navSurface == null)
            return;

        if (sourceObject == null)
        {
            Transform existing = navSurface.transform.Find(SourceObjectName);
            if (existing != null)
                sourceObject = existing.gameObject;
        }

        if (sourceObject == null)
        {
            sourceObject = new GameObject(SourceObjectName);
            sourceObject.transform.SetParent(navSurface.transform, false);
        }
        else if (sourceObject.transform.parent != navSurface.transform)
        {
            sourceObject.transform.SetParent(navSurface.transform, true);
        }

        if (!keepSourceVisibleInHierarchy)
            sourceObject.hideFlags = HideFlags.HideInHierarchy;
        else if (sourceObject.hideFlags != HideFlags.None)
            sourceObject.hideFlags = HideFlags.None;

        sourceRenderer = sourceObject.GetComponent<SpriteRenderer>();
        if (sourceRenderer == null)
            sourceRenderer = sourceObject.AddComponent<SpriteRenderer>();

        sourceModifier = sourceObject.GetComponent<NavMeshModifier>();
        if (sourceModifier == null)
            sourceModifier = sourceObject.AddComponent<NavMeshModifier>();
    }

    private static void ApplyWorldUnitScale(Transform target, Transform parent)
    {
        if (target == null)
            return;

        Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
        target.localScale = new Vector3(
            SafeInverse(parentScale.x),
            SafeInverse(parentScale.y),
            SafeInverse(parentScale.z));
    }

    private static float SafeInverse(float value)
    {
        return Mathf.Abs(value) <= 0.0001f ? 1f : 1f / value;
    }

    private static Sprite GetNavSourceSprite()
    {
        if (navSourceSprite != null)
            return navSourceSprite;

        // 4 pixels / 1 PPU = 정확히 4x4 world unit.
        // FullRect Simple Sprite이므로 Tiled/Sliced Sprite와 달리 bake geometry가 모호하지 않습니다.
        const int pixels = RoomBaseTemplate.FixedBaseTiles;
        Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
        {
            name = "PersistentBaseNavMeshSource_4x4_Texture",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color solid = Color.white;
        for (int y = 0; y < pixels; y++)
            for (int x = 0; x < pixels; x++)
                texture.SetPixel(x, y, solid);
        texture.Apply(false, true);

        navSourceSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, pixels, pixels),
            new Vector2(0.5f, 0.5f),
            1f,
            0,
            SpriteMeshType.FullRect);
        navSourceSprite.name = "PersistentBaseNavMeshSource_4x4_Sprite";
        navSourceSprite.hideFlags = HideFlags.HideAndDontSave;
        return navSourceSprite;
    }
}

/// <summary>
/// BattlePersistentBaseNavMeshSource가 BattleSystems에서 누락되지 않도록 하는 설치 보조기입니다.
/// 사용자 Floor SO / Sprite / Scene asset 참조는 수정하지 않습니다.
/// </summary>
public static class BattlePersistentBaseNavMeshSourceInstaller
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
        EditorApplication.delayCall += EnsureEditorComponent;
    }

    private static void EnsureEditorComponent()
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
            {
                continue;
            }

            if (manager.GetComponent<BattlePersistentBaseNavMeshSource>() != null)
                continue;

            Undo.AddComponent<BattlePersistentBaseNavMeshSource>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponent()
    {
        BattleSceneManager manager = Object.FindFirstObjectByType<BattleSceneManager>();
        if (manager != null)
        {
            if (manager.GetComponent<BattlePersistentBaseNavMeshSource>() == null)
                manager.gameObject.AddComponent<BattlePersistentBaseNavMeshSource>();
            return;
        }

        // 구형/테스트 씬에서 BattleSceneManager가 없어도 RoomBaseTemplate이 있으면 fallback으로 붙입니다.
        RoomBaseTemplate baseTemplate = Object.FindFirstObjectByType<RoomBaseTemplate>();
        if (baseTemplate != null && baseTemplate.GetComponent<BattlePersistentBaseNavMeshSource>() == null)
            baseTemplate.gameObject.AddComponent<BattlePersistentBaseNavMeshSource>();
    }
}
