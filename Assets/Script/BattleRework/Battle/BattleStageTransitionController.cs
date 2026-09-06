using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using NavMeshPlus.Components;
using UnityEngine;

/// <summary>
/// Stage-to-stage presentation layer.
///
/// Spatial contract:
/// - 32px = 1 tile = 1 world unit.
/// - The persistent Start/Base is always 4x4 tiles.
/// - Every Combat/Elite Room is positioned FROM that persistent 4x4 anchor.
/// - The generated Room never disables/removes the central 4x4 gameplay cells.
///   The persistent base remains real walkable floor and is rendered above the generated floor,
///   so the stage looks like new pieces are added around the existing start point instead of
///   treating the middle as a hole/reserved void.
/// - On clear, the player's 4x4 neighborhood becomes the next persistent base and all other
///   Room pieces spin/fly off before physical reward selection.
/// </summary>
[DefaultExecutionOrder(-15000)]
public sealed class BattleStageTransitionController : MonoBehaviour
{
    [Header("Stage Clear / Exit")]
    [SerializeField, Min(0.1f)] private float exitGhostExtraDistance = 5f;
    [SerializeField, Min(0f)] private float exitGhostStagger = 0.035f;
    [SerializeField, Min(45f)] private float exitGhostSpinDegrees = 540f;
    [SerializeField, Range(0.75f, 1f)] private float exitGhostEndScale = 0.90f;

    [Header("Persistent Base Presentation")]
    [Tooltip("Generated floor uses sorting -20. Keep the preserved 4x4 immediately above it, but below walls/actors.")]
    [SerializeField] private int persistentBaseFloorSorting = -19;

    [Header("Reward Drop")]
    [SerializeField, Min(0.5f)] private float rewardDropHeight = 4.2f;
    [SerializeField, Min(0.1f)] private float rewardDropDuration = 0.72f;
    [SerializeField, Min(0.4f)] private float rewardSpacing = 1.15f;
    [SerializeField, Min(0.1f)] private float rewardWorldSize = 0.72f;
    [SerializeField, Min(0.1f)] private float rewardPickupRadius = 0.38f;
    [SerializeField] private Color fallbackRewardColor = new(1f, 0.82f, 0.24f, 1f);

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo ActiveBlocksField =
        typeof(BattleRoomManager).GetField("activeBlocks", PrivateInstance);

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private RoomBaseTemplate baseTemplate;
    private PlayerController player;

    private readonly List<GameObject> exitGhosts = new();
    private readonly List<GameObject> rewardPickups = new();

    private Vector3 preservedBaseTileOrigin;
    private bool hasPreservedBaseOrigin;
    private bool clearedStageCollapsed;
    private bool subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleStageTransitionController>() != null)
            return;

        GameObject host = new("BattleStageTransitionRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleStageTransitionController>();
    }

    private void OnEnable()
    {
        StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        Unsubscribe();
        ClearRewardPickups();
        ClearExitGhosts();
    }

    private IEnumerator BindWhenReady()
    {
        while (runManager == null || roomManager == null || baseTemplate == null || player == null)
        {
            ResolveSystems();
            yield return null;
        }

        // SpatialMapController (-20000) must prepare temporary room prototypes before this listener.
        yield return null;
        Subscribe();

        baseTemplate.EnsurePersistentBase();
        CaptureCurrentBaseAnchor();
        EnsureBasePresentation();
    }

    private void ResolveSystems()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (baseTemplate == null)
            baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();
    }

    private void Subscribe()
    {
        if (subscribed || runManager == null)
            return;

        runManager.StateChanged += HandleStateChanged;
        runManager.NodeEntered += HandleNodeEntered;
        runManager.RewardSelectionRequested += HandleRewardSelectionRequested;
        runManager.RewardSelected += HandleRewardSelected;
        runManager.RunEnded += HandleRunEnded;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || runManager == null)
            return;

        runManager.StateChanged -= HandleStateChanged;
        runManager.NodeEntered -= HandleNodeEntered;
        runManager.RewardSelectionRequested -= HandleRewardSelectionRequested;
        runManager.RewardSelected -= HandleRewardSelected;
        runManager.RunEnded -= HandleRunEnded;
        subscribed = false;
    }

    private void Update()
    {
        ResolveSystems();

        if (runManager != null && runManager.State == BattleRunState.Reward && rewardPickups.Count > 0 && player != null)
            player.SetInputPermissions(true, false, false);

        CleanupNullEntries(exitGhosts);
        CleanupNullEntries(rewardPickups);
    }

    private void HandleStateChanged(BattleRunState next)
    {
        if (next != BattleRunState.EnteringNode)
            return;

        ResolveSystems();
        if (baseTemplate == null)
            return;

        baseTemplate.EnsurePersistentBase();
        CaptureCurrentBaseAnchor();
        EnsureBasePresentation();
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        if (node == null || node.room == null)
            return;
        if (node.type != BattleNodeType.Combat && node.type != BattleNodeType.Elite)
            return;

        ResolveSystems();
        if (baseTemplate == null || roomManager == null)
            return;

        baseTemplate.EnsurePersistentBase();
        CaptureCurrentBaseAnchor();
        EnsureBasePresentation();

        // IMPORTANT: do not disable/rename/suppress the middle 4x4 cells anymore.
        // We only align the generated room so its central 4x4 lies exactly on the persistent base.
        AlignGeneratedRoomToPersistentBase(node.room);
        node.room.repositionPlayerOnEnter = false;
        clearedStageCollapsed = false;
    }

    private void CaptureCurrentBaseAnchor()
    {
        if (baseTemplate == null || !baseTemplate.HasPersistentBase)
            return;

        preservedBaseTileOrigin = baseTemplate.FixedTileOriginWorld;
        hasPreservedBaseOrigin = true;
    }

    /// <summary>
    /// Makes the persistent 4x4 the absolute anchor for every new room.
    /// Generated room cells remain fully functional; no central hole/reserved gameplay area exists.
    /// </summary>
    private void AlignGeneratedRoomToPersistentBase(RoomDefinitionSO room)
    {
        if (room == null || roomManager == null || baseTemplate == null)
            return;

        if (!hasPreservedBaseOrigin)
            CaptureCurrentBaseAnchor();
        if (!hasPreservedBaseOrigin)
            return;

        if (!baseTemplate.EnsureVisibleAtTileOrigin(preservedBaseTileOrigin, room))
        {
            Debug.LogError("[BattleStageTransition] Failed to ensure mandatory 4x4 persistent base.", this);
            return;
        }

        if (!TryGetRuntimeRoomTileBounds(room, out int minX, out int minY, out int maxX, out int maxY))
            return;

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        int baseStartX = minX + Mathf.Max(0, (width - RoomBaseTemplate.FixedBaseTiles) / 2);
        int baseStartY = minY + Mathf.Max(0, (height - RoomBaseTemplate.FixedBaseTiles) / 2);

        // The lower-left CENTER of the generated room's central 4x4 is placed on the
        // lower-left CENTER of the persistent 4x4. Every additional tile therefore derives
        // from the start/base anchor instead of an unrelated world origin.
        Vector3 roomOrigin = preservedBaseTileOrigin - new Vector3(baseStartX, baseStartY, 0f);
        if (roomManager.RoomOrigin != null)
        {
            roomOrigin.z = roomManager.RoomOrigin.position.z;
            roomManager.RoomOrigin.position = roomOrigin;
        }

        EnsureBasePresentation();
    }

    private static bool TryGetRuntimeRoomTileBounds(
        RoomDefinitionSO room,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY)
    {
        minX = int.MaxValue;
        minY = int.MaxValue;
        maxX = int.MinValue;
        maxY = int.MinValue;

        if (room == null || room.blocks == null)
            return false;

        bool found = false;
        for (int i = 0; i < room.blocks.Count; i++)
        {
            MapBlockPlacement placement = room.blocks[i];
            if (placement == null || placement.prefab == null)
                continue;

            Transform prototype = placement.prefab.transform;
            if (!prototype.name.StartsWith("__RuntimeRoomPiecePrototype_", StringComparison.Ordinal))
                continue;

            Transform[] children = prototype.GetComponentsInChildren<Transform>(true);
            for (int c = 0; c < children.Length; c++)
            {
                Transform child = children[c];
                if (child == null || !child.name.StartsWith("Tile_", StringComparison.Ordinal))
                    continue;

                int x = Mathf.RoundToInt(child.localPosition.x);
                int y = Mathf.RoundToInt(child.localPosition.y);
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
                found = true;
            }
        }

        return found;
    }

    private void EnsureBasePresentation()
    {
        if (baseTemplate == null)
            return;

        baseTemplate.EnsurePersistentBase();
        GameObject baseObject = baseTemplate.ActiveBase;
        if (baseObject == null)
            return;

        SpriteRenderer[] renderers = baseObject.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.enabled = true;
            if (renderer.sortingOrder < persistentBaseFloorSorting)
                renderer.sortingOrder = persistentBaseFloorSorting;
        }

        Collider2D[] colliders = baseObject.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider != null && collider.isTrigger)
                collider.enabled = true;
        }

        NavMeshModifier[] modifiers = baseObject.GetComponentsInChildren<NavMeshModifier>(true);
        for (int i = 0; i < modifiers.Length; i++)
        {
            if (modifiers[i] == null)
                continue;
            modifiers[i].ignoreFromBuild = false;
        }

        BattleWalkableField[] fields = baseObject.GetComponentsInChildren<BattleWalkableField>(true);
        for (int i = 0; i < fields.Length; i++)
            if (fields[i] != null)
                fields[i].enabled = true;
    }

    private void CollapseClearedStageAroundPlayer()
    {
        if (clearedStageCollapsed || baseTemplate == null || player == null || roomManager == null)
            return;
        if (!roomManager.IsRoomActive)
            return;

        preservedBaseTileOrigin = baseTemplate.ReanchorAroundPlayer(player.transform.position);
        hasPreservedBaseOrigin = true;
        EnsureBasePresentation();

        SpawnExitGhostsFromCurrentRoom();
        HideCurrentRoomBlocks();
        clearedStageCollapsed = true;
    }

    private void SpawnExitGhostsFromCurrentRoom()
    {
        if (roomManager == null || ActiveBlocksField == null)
            return;
        if (ActiveBlocksField.GetValue(roomManager) is not List<MapBlock> blocks || blocks.Count == 0)
            return;

        GameObject root = new("OutgoingStageVisuals");
        DontDestroyOnLoad(root);

        Vector2 center = player != null ? player.transform.position : roomManager.RoomOrigin.position;
        int ghostIndex = 0;

        for (int i = 0; i < blocks.Count; i++)
        {
            MapBlock source = blocks[i];
            if (source == null)
                continue;

            GameObject cloneObject = Instantiate(source.gameObject, source.transform.position, source.transform.rotation, root.transform);
            cloneObject.name = "Outgoing_" + source.name;
            cloneObject.transform.localScale = source.transform.lossyScale;
            DisableGhostGameplay(cloneObject);

            MapBlock ghostBlock = cloneObject.GetComponent<MapBlock>();
            if (ghostBlock == null)
            {
                Destroy(cloneObject);
                continue;
            }

            Vector2 direction = (Vector2)cloneObject.transform.position - center;
            if (direction.sqrMagnitude < 0.01f)
            {
                Vector2[] fallback = { Vector2.left, Vector2.right, Vector2.up, Vector2.down };
                direction = fallback[ghostIndex % fallback.Length];
            }
            direction.Normalize();

            float delay = Mathf.Max(0f, exitGhostStagger) * ghostIndex;
            float duration = Mathf.Max(0.05f, ghostBlock.ExitDuration);
            Tween moveTween = ghostBlock.PlayExit(direction);
            if (moveTween != null)
            {
                if (delay > 0f)
                    moveTween.SetDelay(delay);

                if (exitGhostExtraDistance > 0f)
                {
                    moveTween.OnComplete(() =>
                    {
                        if (cloneObject != null)
                            cloneObject.transform.position += (Vector3)(direction * exitGhostExtraDistance);
                    });
                }
            }

            float spin = (ghostIndex % 2 == 0 ? 1f : -1f) *
                         (exitGhostSpinDegrees + ghostIndex * 37f);
            cloneObject.transform
                .DORotate(new Vector3(0f, 0f, spin), duration, RotateMode.FastBeyond360)
                .SetRelative()
                .SetEase(Ease.InQuad)
                .SetDelay(delay);

            cloneObject.transform
                .DOScale(cloneObject.transform.localScale * exitGhostEndScale, duration)
                .SetEase(Ease.InQuad)
                .SetDelay(delay);

            exitGhosts.Add(cloneObject);
            Destroy(cloneObject, duration + delay + 0.35f);
            ghostIndex++;
        }

        Destroy(root, 3f);
    }

    private void HideCurrentRoomBlocks()
    {
        if (roomManager == null || ActiveBlocksField == null)
            return;
        if (ActiveBlocksField.GetValue(roomManager) is not List<MapBlock> blocks)
            return;

        for (int i = 0; i < blocks.Count; i++)
        {
            MapBlock block = blocks[i];
            if (block == null)
                continue;

            Renderer[] renderers = block.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
                if (renderers[r] != null)
                    renderers[r].enabled = false;

            Collider2D[] colliders = block.GetComponentsInChildren<Collider2D>(true);
            for (int c = 0; c < colliders.Length; c++)
                if (colliders[c] != null)
                    colliders[c].enabled = false;

            NavMeshModifier[] modifiers = block.GetComponentsInChildren<NavMeshModifier>(true);
            for (int m = 0; m < modifiers.Length; m++)
                if (modifiers[m] != null)
                    modifiers[m].ignoreFromBuild = true;

            BattleWalkableField[] fields = block.GetComponentsInChildren<BattleWalkableField>(true);
            for (int f = 0; f < fields.Length; f++)
                if (fields[f] != null)
                    fields[f].enabled = false;
        }

        // The room is gone, but the persistent 4x4 must remain visible/walkable.
        EnsureBasePresentation();
    }

    private static void DisableGhostGameplay(GameObject root)
    {
        Collider2D[] colliders = root.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null)
                colliders[i].enabled = false;

        NavMeshModifier[] modifiers = root.GetComponentsInChildren<NavMeshModifier>(true);
        for (int i = 0; i < modifiers.Length; i++)
            if (modifiers[i] != null)
                modifiers[i].ignoreFromBuild = true;

        BattleWalkableField[] fields = root.GetComponentsInChildren<BattleWalkableField>(true);
        for (int i = 0; i < fields.Length; i++)
            if (fields[i] != null)
                fields[i].enabled = false;
    }

    private void HandleRewardSelectionRequested(IReadOnlyList<BattleEquipmentSO> choices)
    {
        ClearRewardPickups();
        ResolveSystems();

        CollapseClearedStageAroundPlayer();

        if (choices == null || choices.Count == 0 || player == null)
            return;

        float totalWidth = (choices.Count - 1) * rewardSpacing;
        for (int i = 0; i < choices.Count; i++)
        {
            BattleEquipmentSO reward = choices[i];
            if (reward == null)
                continue;

            float x = player.transform.position.x - totalWidth * 0.5f + i * rewardSpacing;
            Vector3 target = new(x, player.transform.position.y + 1.05f, player.transform.position.z);
            Vector3 start = target + Vector3.up * rewardDropHeight;

            GameObject go = new($"StageReward_{i}_{reward.GetDisplayName()}");
            go.transform.position = start;

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = reward.icon != null ? reward.icon : StageTransitionRuntimeSpriteCache.RewardFallback;
            renderer.color = reward.icon != null ? Color.white : fallbackRewardColor;
            renderer.sortingOrder = 120;
            NormalizeSpriteToWorldSize(renderer, rewardWorldSize);

            CircleCollider2D trigger = go.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = rewardPickupRadius / Mathf.Max(0.01f, Mathf.Abs(go.transform.lossyScale.x));

            BattleStageRewardPickup pickup = go.AddComponent<BattleStageRewardPickup>();
            pickup.Configure(this, i);

            go.transform.DOMove(target, rewardDropDuration)
                .SetEase(Ease.OutBounce);
            go.transform.DORotate(new Vector3(0f, 0f, i % 2 == 0 ? 360f : -360f), rewardDropDuration, RotateMode.FastBeyond360)
                .SetEase(Ease.OutCubic);

            rewardPickups.Add(go);
        }
    }

    internal bool TryCollectReward(int index, GameObject pickupObject)
    {
        if (runManager == null || runManager.State != BattleRunState.Reward)
            return false;

        bool acquired = runManager.SelectReward(index);
        if (!acquired && pickupObject != null)
        {
            pickupObject.transform.DOPunchScale(Vector3.one * 0.12f, 0.18f, 5, 0.5f);
            return false;
        }

        if (acquired)
            ClearRewardPickups();
        return acquired;
    }

    private void HandleRewardSelected(BattleEquipmentSO _)
    {
        ClearRewardPickups();
    }

    private void HandleRunEnded(RunEndReason _)
    {
        ClearRewardPickups();
        ClearExitGhosts();
        hasPreservedBaseOrigin = false;
        clearedStageCollapsed = false;
    }

    private void ClearRewardPickups()
    {
        for (int i = 0; i < rewardPickups.Count; i++)
        {
            GameObject go = rewardPickups[i];
            if (go == null)
                continue;
            go.transform.DOKill();
            Destroy(go);
        }
        rewardPickups.Clear();
    }

    private void ClearExitGhosts()
    {
        for (int i = 0; i < exitGhosts.Count; i++)
        {
            GameObject go = exitGhosts[i];
            if (go == null)
                continue;
            go.transform.DOKill();
            Destroy(go);
        }
        exitGhosts.Clear();
    }

    private static void CleanupNullEntries(List<GameObject> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i] == null)
                list.RemoveAt(i);
    }

    private static void NormalizeSpriteToWorldSize(SpriteRenderer renderer, float maxWorldSize)
    {
        if (renderer == null || renderer.sprite == null)
            return;

        Vector2 size = renderer.sprite.bounds.size;
        float max = Mathf.Max(0.001f, Mathf.Max(size.x, size.y));
        float scale = Mathf.Max(0.01f, maxWorldSize / max);
        renderer.transform.localScale = new Vector3(scale, scale, 1f);
    }
}

internal sealed class BattleStageRewardPickup : MonoBehaviour
{
    private BattleStageTransitionController owner;
    private int rewardIndex;
    private bool collected;

    public void Configure(BattleStageTransitionController controller, int index)
    {
        owner = controller;
        rewardIndex = index;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (collected || owner == null || other == null)
            return;

        PlayerController hitPlayer = other.GetComponent<PlayerController>();
        if (hitPlayer == null)
            hitPlayer = other.GetComponentInParent<PlayerController>();
        if (hitPlayer == null)
            return;

        collected = owner.TryCollectReward(rewardIndex, gameObject);
    }
}

internal static class StageTransitionRuntimeSpriteCache
{
    private static Sprite rewardFallback;
    public static Sprite RewardFallback => rewardFallback != null ? rewardFallback : rewardFallback = CreateRewardFallback();

    private static Sprite CreateRewardFallback()
    {
        const int pixels = 16;
        Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Vector2 center = new((pixels - 1) * 0.5f, (pixels - 1) * 0.5f);
        for (int y = 0; y < pixels; y++)
        {
            for (int x = 0; x < pixels; x++)
            {
                float dx = Mathf.Abs(x - center.x);
                float dy = Mathf.Abs(y - center.y);
                bool inside = dx + dy <= 7.0f;
                bool core = dx + dy <= 4.5f;
                texture.SetPixel(x, y, !inside ? Color.clear : (core ? Color.white : new Color(0.82f, 0.82f, 0.82f, 1f)));
            }
        }

        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, pixels, pixels),
            new Vector2(0.5f, 0.5f),
            pixels,
            0,
            SpriteMeshType.FullRect);
        sprite.name = "RuntimeStageRewardFallback";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
