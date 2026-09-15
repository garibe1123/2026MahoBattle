using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BattleDecor가 참조해야 하는 실제 Floor Renderer의 경량 Runtime Registry입니다.
/// Scene 전체 FindObjectsByType 대신 각 Floor가 활성/비활성 시점에 스스로 등록합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleDecorFloorSource : MonoBehaviour
{
    private static readonly HashSet<BattleDecorFloorSource> ActiveSources = new();

    [SerializeField] private SpriteRenderer sourceRenderer;
    private MapBlock owner;

    public SpriteRenderer Renderer => sourceRenderer;
    public MapBlock Owner => owner;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ActiveSources.Add(this);
    }

    private void OnDisable()
    {
        ActiveSources.Remove(this);
    }

    private void OnDestroy()
    {
        ActiveSources.Remove(this);
    }

    private void OnTransformParentChanged()
    {
        ResolveOwner();
    }

    private void ResolveReferences()
    {
        if (sourceRenderer == null)
            sourceRenderer = GetComponent<SpriteRenderer>();
        ResolveOwner();
    }

    private void ResolveOwner()
    {
        owner = null;
        Transform cursor = transform;
        while (cursor != null)
        {
            MapBlock candidate = cursor.GetComponent<MapBlock>();
            if (candidate != null)
                owner = candidate;
            cursor = cursor.parent;
        }
    }

    public static BattleDecorFloorSource Ensure(SpriteRenderer renderer)
    {
        if (renderer == null)
            return null;

        BattleDecorFloorSource source = renderer.GetComponent<BattleDecorFloorSource>();
        if (source == null)
            source = renderer.gameObject.AddComponent<BattleDecorFloorSource>();

        source.sourceRenderer = renderer;
        source.ResolveOwner();
        if (source.isActiveAndEnabled)
            ActiveSources.Add(source);
        return source;
    }

    public static void CopyActive(List<BattleDecorFloorSource> destination)
    {
        if (destination == null)
            return;

        destination.Clear();
        foreach (BattleDecorFloorSource source in ActiveSources)
        {
            if (source == null || !source.isActiveAndEnabled)
                continue;
            if (source.sourceRenderer == null || !source.sourceRenderer.enabled || source.sourceRenderer.sprite == null)
                continue;
            destination.Add(source);
        }
    }
}

/// <summary>
/// Runtime에 뒤늦게 생성되는 non-walkable Show Carrier처럼 Visual 하위에 Floor가 추가되는 경우,
/// Hierarchy 변경을 한 프레임에 한 번만 모아 BattleDecorFloorSource를 붙입니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleDecorFloorHierarchyRegistrar : MonoBehaviour
{
    private bool dirty = true;

    private void OnEnable()
    {
        dirty = true;
    }

    private void OnTransformChildrenChanged()
    {
        dirty = true;
    }

    private void LateUpdate()
    {
        if (!dirty)
            return;
        dirty = false;
        RegisterNamedFloorRenderers();
    }

    private void RegisterNamedFloorRenderers()
    {
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sprite == null)
                continue;

            string n = renderer.name;
            if (!n.StartsWith("ShowTile_", System.StringComparison.Ordinal) &&
                !n.StartsWith("Floor_", System.StringComparison.Ordinal))
                continue;

            BattleDecorFloorSource.Ensure(renderer);
        }
    }

    public static BattleDecorFloorHierarchyRegistrar Ensure(Transform root)
    {
        if (root == null)
            return null;

        BattleDecorFloorHierarchyRegistrar registrar = root.GetComponent<BattleDecorFloorHierarchyRegistrar>();
        if (registrar == null)
            registrar = root.gameObject.AddComponent<BattleDecorFloorHierarchyRegistrar>();
        registrar.dirty = true;
        return registrar;
    }
}
