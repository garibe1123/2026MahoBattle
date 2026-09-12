using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Procedural Room의 Entry Transport/Assembly는 진입 때만 하나의 이동 Root로 사용합니다.
/// 전투 종료 시에는 StageTransition이 이미 선택한 안전 Exit Rail 방향을 유지하면서,
/// 내부의 원래 MapBlock Piece(1x1 / L / 자유형)를 다시 분리해 개별적으로 화면 밖으로 보냅니다.
///
/// 이 컨트롤러는 Room lifecycle, Persistent 4x4 선택, Exit 방향 계획을 소유하지 않습니다.
/// BattleStageTransitionController의 기존 안전 Rail 판정과 ownership retirement는 그대로 두고,
/// 부모 Assembly가 실제 PlayExit를 시작한 순간에만 시각 이동 단위를 Piece로 분해합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(21000)]
public sealed class BattleRoomExitPiecePresentationController : MonoBehaviour
{
    private sealed class ExitCandidate
    {
        public MapBlock root;
        public Vector3 lastPosition;
        public bool observedMoving;
    }

    private BattleStageTransitionController stageFlow;
    private readonly List<ExitCandidate> candidates = new();
    private readonly HashSet<int> processedRoots = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallAfterSceneLoad()
    {
        EnsureInstalled();
    }

    private static void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        EnsureInstalled();
    }

    private static void EnsureInstalled()
    {
        BattleStageTransitionController owner = FindFirstObjectByType<BattleStageTransitionController>(FindObjectsInactive.Include);
        if (owner == null || owner.GetComponent<BattleRoomExitPiecePresentationController>() != null)
            return;

        owner.gameObject.AddComponent<BattleRoomExitPiecePresentationController>();
    }

    private void Awake()
    {
        stageFlow = GetComponent<BattleStageTransitionController>();
        if (stageFlow == null)
            stageFlow = FindFirstObjectByType<BattleStageTransitionController>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        if (stageFlow == null)
            stageFlow = GetComponent<BattleStageTransitionController>();

        if (stageFlow != null)
        {
            stageFlow.FlowStateChanged -= HandleFlowStateChanged;
            stageFlow.FlowStateChanged += HandleFlowStateChanged;

            if (stageFlow.FlowState == BattleStageFlowState.RoomExiting)
                CaptureAssemblyCandidates();
        }
    }

    private void OnDisable()
    {
        if (stageFlow != null)
            stageFlow.FlowStateChanged -= HandleFlowStateChanged;
        ClearCandidates();
    }

    private void HandleFlowStateChanged(BattleStageFlowState state)
    {
        if (state == BattleStageFlowState.RoomExiting)
            CaptureAssemblyCandidates();
        else
            ClearCandidates();
    }

    private void CaptureAssemblyCandidates()
    {
        candidates.Clear();
        processedRoots.Clear();

        MapBlock[] blocks = FindObjectsByType<MapBlock>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            MapBlock root = blocks[i];
            if (root == null || !root.gameObject.activeInHierarchy)
                continue;
            if (HasMapBlockAncestor(root.transform))
                continue;
            if (!HasDirectNestedMapBlock(root))
                continue;

            candidates.Add(new ExitCandidate
            {
                root = root,
                lastPosition = root.transform.position,
                observedMoving = false
            });
        }
    }

    private void LateUpdate()
    {
        if (stageFlow == null || stageFlow.FlowState != BattleStageFlowState.RoomExiting)
            return;

        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            ExitCandidate candidate = candidates[i];
            MapBlock root = candidate.root;
            if (root == null)
            {
                candidates.RemoveAt(i);
                continue;
            }

            int id = root.GetInstanceID();
            if (processedRoots.Contains(id))
            {
                candidates.RemoveAt(i);
                continue;
            }

            // StageTransition이 이 Root의 PlayExit를 실제로 시작한 시점만 가로챕니다.
            // 아직 자기 차례가 아닌 Assembly는 그대로 둡니다.
            if (!DOTween.IsTweening(root.transform))
            {
                candidate.lastPosition = root.transform.position;
                candidate.observedMoving = false;
                continue;
            }

            Vector3 current = root.transform.position;
            Vector2 delta = (Vector2)(current - candidate.lastPosition);

            if (!candidate.observedMoving)
            {
                candidate.observedMoving = true;
                candidate.lastPosition = current;
                continue;
            }

            if (delta.sqrMagnitude <= 0.000001f)
            {
                candidate.lastPosition = current;
                continue;
            }

            Vector2 direction = Cardinalize(delta);
            SplitAndExitPieces(root, direction);
            processedRoots.Add(id);
            candidates.RemoveAt(i);
        }
    }

    private static void SplitAndExitPieces(MapBlock root, Vector2 direction)
    {
        if (root == null)
            return;

        List<MapBlock> pieces = CollectDirectNestedBlocks(root);
        if (pieces.Count == 0)
            return;

        // 부모가 통째로 빠지는 Tween을 중단하고, 현재 월드 위치에서 Piece들이 이어서 빠집니다.
        root.transform.DOKill();

        Vector2 dir = Cardinalize(direction);
        float travelDistance = Mathf.Max(2f, root.ExitTravelDistance);
        float totalWindow = Mathf.Max(0.12f, root.ExitDuration - 0.025f);
        float maxStagger = Mathf.Min(0.11f, totalWindow * 0.18f);
        float moveDuration = Mathf.Max(0.08f, totalWindow - maxStagger);

        // 같은 Rail에서는 바깥 Piece가 먼저 움직이게 해 안쪽 Piece가 앞 Piece를 따라잡지 않게 합니다.
        pieces.Sort((a, b) =>
        {
            float ap = a != null ? Vector2.Dot(a.transform.position, dir) : float.MinValue;
            float bp = b != null ? Vector2.Dot(b.transform.position, dir) : float.MinValue;
            return bp.CompareTo(ap);
        });

        Transform detachedParent = root.transform.parent;
        int visiblePieceCount = 0;

        for (int i = 0; i < pieces.Count; i++)
        {
            MapBlock piece = pieces[i];
            if (piece == null)
                continue;

            piece.transform.DOKill();
            piece.transform.SetParent(detachedParent, true);
            DisableWalkable(piece);

            if (!HasVisibleRenderer(piece))
            {
                piece.gameObject.SetActive(false);
                Destroy(piece.gameObject);
                continue;
            }

            float delay = pieces.Count <= 1
                ? 0f
                : maxStagger * i / Mathf.Max(1f, pieces.Count - 1f);

            Vector3 destination = piece.transform.position + (Vector3)(dir * travelDistance);
            Sequence sequence = DOTween.Sequence().SetUpdate(true);
            if (delay > 0f)
                sequence.AppendInterval(delay);
            sequence.Append(piece.transform.DOMove(destination, moveDuration).SetEase(Ease.InQuad));
            sequence.OnComplete(() =>
            {
                if (piece != null)
                {
                    piece.gameObject.SetActive(false);
                    Destroy(piece.gameObject);
                }
            });
            visiblePieceCount++;
        }

        Debug.Log(
            $"[BattleStageFlow] Split '{root.name}' into {visiblePieceCount} visible exit piece(s) on {dir} rail.",
            root);
    }

    private static List<MapBlock> CollectDirectNestedBlocks(MapBlock root)
    {
        List<MapBlock> result = new();
        if (root == null)
            return result;

        MapBlock[] nested = root.GetComponentsInChildren<MapBlock>(true);
        for (int i = 0; i < nested.Length; i++)
        {
            MapBlock piece = nested[i];
            if (piece == null || piece == root)
                continue;

            Transform current = piece.transform.parent;
            MapBlock nearestParentBlock = null;
            while (current != null && current != root.transform)
            {
                nearestParentBlock = current.GetComponent<MapBlock>();
                if (nearestParentBlock != null)
                    break;
                current = current.parent;
            }

            if (nearestParentBlock == null && current == root.transform)
                result.Add(piece);
        }

        return result;
    }

    private static bool HasDirectNestedMapBlock(MapBlock root)
    {
        if (root == null)
            return false;

        MapBlock[] nested = root.GetComponentsInChildren<MapBlock>(true);
        for (int i = 0; i < nested.Length; i++)
        {
            MapBlock piece = nested[i];
            if (piece == null || piece == root)
                continue;

            Transform current = piece.transform.parent;
            bool nestedUnderAnotherBlock = false;
            while (current != null && current != root.transform)
            {
                if (current.GetComponent<MapBlock>() != null)
                {
                    nestedUnderAnotherBlock = true;
                    break;
                }
                current = current.parent;
            }

            if (!nestedUnderAnotherBlock && current == root.transform)
                return true;
        }

        return false;
    }

    private static bool HasMapBlockAncestor(Transform transform)
    {
        if (transform == null)
            return false;

        Transform current = transform.parent;
        while (current != null)
        {
            if (current.GetComponent<MapBlock>() != null)
                return true;
            current = current.parent;
        }

        return false;
    }

    private static void DisableWalkable(MapBlock block)
    {
        if (block == null)
            return;

        BattleWalkableField[] fields = block.GetComponentsInChildren<BattleWalkableField>(true);
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i] != null)
                fields[i].enabled = false;
        }
    }

    private static bool HasVisibleRenderer(MapBlock block)
    {
        if (block == null)
            return false;

        SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer != null && renderer.enabled && renderer.sprite != null && renderer.gameObject.activeInHierarchy)
                return true;
        }

        return false;
    }

    private static Vector2 Cardinalize(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.000001f)
            return Vector2.right;

        return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? (direction.x >= 0f ? Vector2.right : Vector2.left)
            : (direction.y >= 0f ? Vector2.up : Vector2.down);
    }

    private void ClearCandidates()
    {
        candidates.Clear();
        processedRoots.Clear();
    }
}
