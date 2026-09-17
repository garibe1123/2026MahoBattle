using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps authored RoomDefinitionSO data immutable across a play session.
///
/// Some legacy stage-presentation code temporarily changes RoomDefinitionSO fields so the current
/// room can hand off from the persistent 4x4 without repositioning the player. ScriptableObjects are
/// shared assets, so leaving those values changed can leak state into a later node/run. This guard
/// snapshots the authored reposition flag before combat starts and restores it after NodeEntered has
/// finished its synchronous hand-off work.
/// </summary>
[DefaultExecutionOrder(-16000)]
[DisallowMultipleComponent]
public sealed class BattleRoomDefinitionRuntimeGuard : MonoBehaviour
{
    private static readonly BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Dictionary<RoomDefinitionSO, bool> authoredRepositionFlags = new();

    private BattleRunManager runManager;
    private Coroutine bindRoutine;
    private bool subscribed;

    private void OnEnable()
    {
        if (bindRoutine == null)
            bindRoutine = StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        if (bindRoutine != null)
            StopCoroutine(bindRoutine);
        bindRoutine = null;
        Unsubscribe();
        RestoreAllAuthoredFlags();
    }

    private IEnumerator BindWhenReady()
    {
        while (enabled && runManager == null)
        {
            runManager = FindFirstObjectByType<BattleRunManager>();
            if (runManager == null)
                yield return null;
        }

        if (!enabled || runManager == null)
        {
            bindRoutine = null;
            yield break;
        }

        CaptureAuthoredGraphFlags();
        Subscribe();
        bindRoutine = null;
    }

    private void CaptureAuthoredGraphFlags()
    {
        if (runManager == null)
            return;

        FieldInfo graphField = typeof(BattleRunManager).GetField("nodeGraph", InstanceFields);
        NodeGraphSO graph = graphField != null ? graphField.GetValue(runManager) as NodeGraphSO : null;
        if (graph == null || graph.nodes == null)
            return;

        for (int i = 0; i < graph.nodes.Count; i++)
        {
            BattleNodeData node = graph.nodes[i];
            RoomDefinitionSO room = node != null ? node.room : null;
            if (room != null && !authoredRepositionFlags.ContainsKey(room))
                authoredRepositionFlags.Add(room, room.repositionPlayerOnEnter);
        }
    }

    private void Subscribe()
    {
        if (subscribed || runManager == null)
            return;

        runManager.NodeEntered += HandleNodeEntered;
        runManager.RunEnded += HandleRunEnded;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || runManager == null)
            return;

        runManager.NodeEntered -= HandleNodeEntered;
        runManager.RunEnded -= HandleRunEnded;
        subscribed = false;
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        RoomDefinitionSO room = node != null ? node.room : null;
        if (room == null)
            return;

        if (!authoredRepositionFlags.TryGetValue(room, out bool authoredValue))
        {
            // Non-graph/runtime supplied rooms are uncommon. Preserve their value at first sight;
            // graph rooms were already captured before any NodeEntered subscriber can mutate them.
            authoredValue = room.repositionPlayerOnEnter;
            authoredRepositionFlags[room] = authoredValue;
        }

        StartCoroutine(RestoreAfterSynchronousNodeEntry(room, authoredValue));
    }

    private static IEnumerator RestoreAfterSynchronousNodeEntry(RoomDefinitionSO room, bool authoredValue)
    {
        // BattleRunManager calls NodeEntered and then starts EnterRoom in the same synchronous stack.
        // Waiting one frame preserves the intended current-room hand-off while preventing the SO asset
        // from carrying the temporary value into later rooms or a restarted run.
        yield return null;
        if (room != null)
            room.repositionPlayerOnEnter = authoredValue;
    }

    private void HandleRunEnded(RunEndReason _)
    {
        RestoreAllAuthoredFlags();
    }

    private void RestoreAllAuthoredFlags()
    {
        foreach (KeyValuePair<RoomDefinitionSO, bool> pair in authoredRepositionFlags)
        {
            if (pair.Key != null)
                pair.Key.repositionPlayerOnEnter = pair.Value;
        }
    }
}

/// <summary>
/// Installs the guard only in battle scenes and leaves it scene-owned (not DontDestroyOnLoad).
/// </summary>
internal static class BattleRoomDefinitionRuntimeGuardInstaller
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode _)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        bool battleScene = scene.name == BattleSceneEntry.DefaultBattleSceneName;
        if (!battleScene)
        {
            BattleSceneManager manager = Object.FindFirstObjectByType<BattleSceneManager>();
            battleScene = manager != null && manager.gameObject.scene == scene;
        }

        if (!battleScene || Object.FindFirstObjectByType<BattleRoomDefinitionRuntimeGuard>() != null)
            return;

        GameObject host = new("BattleRoomDefinitionRuntimeGuard");
        SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<BattleRoomDefinitionRuntimeGuard>();
    }
}