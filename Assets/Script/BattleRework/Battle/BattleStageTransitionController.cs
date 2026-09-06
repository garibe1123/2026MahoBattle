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
/// Flow:
/// 1) The persistent Start/Base anchor is always 4x4 32px tiles.
/// 2) Selected Combat Rooms are assembled around that 4x4 anchor.
/// 3) The central 4x4 cells of the generated Room are reserved for the persistent base,
///    so incoming pieces only add floor around it.
/// 4) After reward collection and next-stage selection, the 4x4 area around the player
///    becomes the next base. Old room pieces visually fly off-screen while new pieces dock in.
/// 5) Reward choices physically drop from above and are collected by touching them.
///
/// This component is presentation-only. BattleRunManager still owns node/reward progression and
/// BattleRoomManager still owns real room lifecycle, spawning, NavMesh and combat.
/// </summary>
[DefaultExecutionOrder(-15000)]
public sealed class BattleStageTransitionController : MonoBehaviour
{
    [Header("Stage Exit")]
    [SerializeField, Min(0.1f)] private float exitGhostExtraDistance = 5f;
    [SerializeField, Min(0f)] private float exitGhostStagger = 0.035f;

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

        // BattleSpatialMapController has an earlier execution order (-20000).
        // Waiting one frame makes its NodeEntered subscription reliably precede ours, so its
        // temporary runtime Room prototypes exist before we reserve the central 4x4 cells.
        yield return null;
        Subscribe();

        if (baseTemplate.HasPersistentBase)
        {
            preservedBaseTileOrigin = baseTemplate.FixedTileOriginWorld;
            hasPreservedBaseOrigin = true;
        }
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

        // Reward is a physical pickup phase: walking is allowed, shooting/roll are not.
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
        if (baseTemplate == null || player == null)
            return;

        // The very first stage keeps the initial 4x4 Start Base exactly where it was built.
        if (roomManager == null || !roomManager.IsRoomActive)
        {
            preservedBaseTileOrigin = baseTemplate.FixedTileOriginWorld;
            hasPreservedBaseOrigin = true;
            return;
        }

        // After a cleared stage, the player's current neighborhood becomes the next 4x4 anchor.
        // Clone the old room visuals first because BattleRoomManager clears the real blocks immediately
        // when EnterRoom starts. The clones are presentation-only and fly out of the screen.
        SpawnExitGhostsFromCurrentRoom();
        preservedBaseTileOrigin = baseTemplate.ReanchorAroundPlayer(player.transform.position);
        hasPreservedBaseOrigin = true;
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

        if (!baseTemplate.HasPersistentBase)
            baseTemplate.BuildBase(node.room);

        if (!hasPreservedBaseOrigin)
        {
            preservedBaseTileOrigin = baseTemplate.FixedTileOriginWorld;
            hasPreservedBaseOrigin = true;
        }

        ReservePersistentBaseInsideRuntimeRoom(node.room);
    }

    /// <summary>
    /// BattleSpatialMapController temporarily stores its generated MapBlock prototypes in room.blocks
    /// during NodeEntered. We inspect those prototypes, find the generated Room bounds and reserve the
    /// central 4x4 cells for RoomBaseTemplate. Incoming pieces therefore physically connect around the
    /// already-existing base instead of covering/replacing it.
    /// </summary>
    private void ReservePersistentBaseInsideRuntimeRoom(RoomDefinitionSO room)
    {
        if (room == null || room.blocks == null || room.blocks.Count == 0)
            return;

        List<Transform> floorTiles = new();
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

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
                floorTiles.Add(child);
            }
        }

        if (floorTiles.Count == 0 || minX == int.MaxValue)
            return;

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        int baseStartX = minX + Mathf.Max(0, (width - RoomBaseTemplate.FixedBaseTiles) / 2);
        int baseStartY = minY + Mathf.Max(0, (height - RoomBaseTemplate.FixedBaseTiles) / 2);
        int baseEndX = baseStartX + RoomBaseTemplate.FixedBaseTiles - 1;
        int baseEndY = baseStartY + RoomBaseTemplate.FixedBaseTiles - 1;

        // Place RoomOrigin so generated central 4x4 cell centers exactly coincide with the persistent base.
        Vector3 roomOrigin = preservedBaseTileOrigin - new Vector3(baseStartX, baseStartY, 0f);
        roomOrigin.z = roomManager.RoomOrigin != null ? roomManager.RoomOrigin.position.z : 0f;
        if (roomManager.RoomOrigin != null)
            roomManager.RoomOrigin.position = roomOrigin;

        // Keep the player where they are. The base is the continuity anchor between stages.
        room.repositionPlayerOnEnter = false;

        for (int i = 0; i < floorTiles.Count; i++)
        {
            Transform tile = floorTiles[i];
            if (tile == null)
                continue;

            int x = Mathf.RoundToInt(tile.localPosition.x);
            int y = Mathf.RoundToInt(tile.localPosition.y);
            if (x < baseStartX || x > baseEndX || y < baseStartY || y > baseEndY)
                continue;

            ReserveTileForPersistentBase(tile);
        }
    }

    private static void ReserveTileForPersistentBase(Transform tile)
    {
        tile.name = "PersistentBaseReserved_" + tile.name;

        SpriteRenderer[] renderers = tile.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null)
                renderers[i].enabled = false;

        Collider2D[] colliders = tile.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null)
                colliders[i].enabled = false;

        NavMeshModifier[] modifiers = tile.GetComponentsInChildren<NavMeshModifier>(true);
        for (int i = 0; i < modifiers.Length; i++)
        {
            if (modifiers[i] == null)
                continue;
            modifiers[i].ignoreFromBuild = true;
        }

        BattleWalkableField[] fields = tile.GetComponentsInChildren<BattleWalkableField>(true);
        for (int i = 0; i < fields.Length; i++)
            if (fields[i] != null)
                fields[i].enabled = false;
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

            // PlayExit already uses the block's entry offset. Extend it slightly so large rooms clear the camera.
            Tween tween = ghostBlock.PlayExit(direction);
            if (tween != null && exitGhostExtraDistance > 0f)
            {
                tween.OnComplete(() =>
                {
                    if (cloneObject != null)
                        cloneObject.transform.position += (Vector3)(direction * exitGhostExtraDistance);
                });
            }

            float delay = Mathf.Max(0f, exitGhostStagger) * ghostIndex;
            if (delay > 0f && tween != null)
                tween.SetDelay(delay);

            exitGhosts.Add(cloneObject);
            Destroy(cloneObject, ghostBlock.ExitDuration + delay + 0.25f);
            ghostIndex++;
        }

        Destroy(root, 2.5f);
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

/// <summary>
/// Physical stage reward trigger. Lives in the same file to avoid another script file.
/// </summary>
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

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null)
            player = other.GetComponentInParent<PlayerController>();
        if (player == null)
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
