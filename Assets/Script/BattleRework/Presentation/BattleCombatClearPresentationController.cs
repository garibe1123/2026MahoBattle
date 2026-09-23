using System.Collections;
using UnityEngine;

/// <summary>
/// Owns the short Combat -> Finish Shot -> Reward hand-off.
/// BattleRoomManager remains authoritative for alive-count / clear state; this component only presents
/// the confirmed final kill and calls CompleteCombatClearPresentation when the shot has finished.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleCombatClearPresentationController : MonoBehaviour, IInputModal
{
    [Header("Timing (unscaled seconds)")]
    [SerializeField, Min(0.01f)] private float totalDuration = 1.05f;
    [SerializeField, Min(0f)] private float playerProjectileCleanupDelay = 0.08f;
    [SerializeField, Min(0f)] private float returnToPlayerAt = 0.65f;

    [Header("Final Kill Camera")]
    [SerializeField, Range(0.35f, 0.85f)] private float normalOccupancy = 0.58f;
    [SerializeField, Range(0.35f, 0.85f)] private float eliteOccupancy = 0.63f;
    [SerializeField, Range(0.35f, 0.85f)] private float bossOccupancy = 0.69f;
    [SerializeField, Range(-0.2f, 0.4f)] private float focusYBiasFraction = 0.12f;

    [Header("Finish Look")]
    [SerializeField, Range(0f, 1f)] private float gradingEmphasis = 0.70f;

    private Coroutine routine;
    private BattleRoomManager activeRoom;
    private BattleCameraController battleCamera;
    private BattleTimeScaleController timeScaleController;
    private BattleInputRouter inputRouter;
    private BattleColorGradingController colorGrading;
    private int finalTargetFocusId;
    private int playerReturnFocusId;
    private bool modalPushed;

    public static BattleCombatClearPresentationController ResolveOrCreate(Component requester = null)
    {
        BattleCombatClearPresentationController existing =
            FindFirstObjectByType<BattleCombatClearPresentationController>(FindObjectsInactive.Include);
        if (existing != null)
            return existing;

        GameObject host = requester != null && requester.gameObject != null
            ? requester.gameObject
            : new GameObject("BattleCombatClearPresentation");
        return host.AddComponent<BattleCombatClearPresentationController>();
    }

    public bool Play(BattleRoomManager room, MonsterController finalTarget)
    {
        if (room == null || finalTarget == null)
            return false;

        Cancel();
        ResolveReferences();

        activeRoom = room;
        routine = StartCoroutine(PlayRoutine(room, finalTarget));
        return true;
    }

    public void Cancel()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        ReleasePresentationState();
        activeRoom = null;
    }

    public void RequestClose()
    {
        // Finish Shot is intentionally non-dismissible.
    }

    private void OnDisable()
    {
        Cancel();
    }

    private void ResolveReferences()
    {
        if (battleCamera == null)
            battleCamera = FindFirstObjectByType<BattleCameraController>(FindObjectsInactive.Include);
        timeScaleController = BattleTimeScaleController.ResolveOrCreate(this);
        if (inputRouter == null)
            inputRouter = BattleInputRouter.ResolveOrCreate(this);
        if (colorGrading == null)
            colorGrading = FindFirstObjectByType<BattleColorGradingController>(FindObjectsInactive.Include);
    }

    private IEnumerator PlayRoutine(BattleRoomManager room, MonsterController finalTarget)
    {
        if (inputRouter != null)
        {
            inputRouter.PushModal(this);
            modalPushed = true;
        }

        room.EnemyProjectilePool?.ReturnAllActive();

        Bounds targetBounds = ResolveSpriteBounds(finalTarget);
        float occupancy = ResolveOccupancy(finalTarget);
        if (battleCamera != null)
        {
            finalTargetFocusId = battleCamera.FocusBoundsOccupancy(
                targetBounds,
                0f,
                occupancy,
                focusYBiasFraction,
                BattleCameraFocusPriority.LastKill);
        }

        colorGrading?.SetLastKillEmphasis(gradingEmphasis);

        float elapsed = 0f;
        bool playerProjectilesCleaned = false;
        bool returnFocusStarted = false;

        while (elapsed < Mathf.Max(0.05f, totalDuration))
        {
            elapsed += Time.unscaledDeltaTime;

            if (!playerProjectilesCleaned && elapsed >= playerProjectileCleanupDelay)
            {
                playerProjectilesCleaned = true;
                PlayerShootingSystem shooting =
                    FindFirstObjectByType<PlayerShootingSystem>(FindObjectsInactive.Include);
                ProjectilePooler playerPool = shooting != null ? shooting.playerProjectilePool : null;
                if (playerPool != null && !ReferenceEquals(playerPool, room.EnemyProjectilePool))
                    playerPool.ReturnAllActive();
            }

            ApplyTimeScale(elapsed);

            if (!returnFocusStarted && elapsed >= returnToPlayerAt)
            {
                returnFocusStarted = true;

                if (battleCamera != null)
                {
                    battleCamera.ReleaseFocus(finalTargetFocusId);
                    finalTargetFocusId = 0;

                    if (room.PlayerTarget != null)
                    {
                        playerReturnFocusId = battleCamera.FocusTarget(
                            room.PlayerTarget,
                            0.30f,
                            0f,
                            BattleCameraFocusPriority.LastKill);
                    }
                }
            }

            yield return null;
        }

        BattleRoomManager completedRoom = activeRoom;
        routine = null;
        ReleasePresentationState();
        activeRoom = null;

        if (completedRoom != null)
            completedRoom.CompleteCombatClearPresentation();
    }

    private void ApplyTimeScale(float elapsed)
    {
        if (timeScaleController == null)
            return;

        float scale;
        if (elapsed < 0.08f)
            scale = Mathf.Lerp(1f, 0.55f, elapsed / 0.08f);
        else if (elapsed < 0.16f)
            scale = Mathf.Lerp(0.55f, 0.22f, (elapsed - 0.08f) / 0.08f);
        else if (elapsed < 0.40f)
            scale = 0.22f;
        else if (elapsed < 0.55f)
            scale = Mathf.Lerp(0.22f, 0.35f, (elapsed - 0.40f) / 0.15f);
        else if (elapsed < 0.85f)
            scale = Mathf.Lerp(0.35f, 1f, (elapsed - 0.55f) / 0.30f);
        else
            scale = 1f;

        if (scale >= 0.999f)
            timeScaleController.Release(BattleTimeScaleController.Owner.LastKill);
        else
            timeScaleController.Request(BattleTimeScaleController.Owner.LastKill, scale);
    }

    private void ReleasePresentationState()
    {
        if (battleCamera != null)
        {
            battleCamera.ReleaseFocus(finalTargetFocusId);
            battleCamera.ReleaseFocus(playerReturnFocusId);
        }

        finalTargetFocusId = 0;
        playerReturnFocusId = 0;

        timeScaleController?.Release(BattleTimeScaleController.Owner.LastKill);
        colorGrading?.SetLastKillEmphasis(0f);

        if (modalPushed && inputRouter != null)
            inputRouter.PopModal(this);
        modalPushed = false;
    }

    private float ResolveOccupancy(MonsterController monster)
    {
        if (monster == null || monster.Definition == null)
            return normalOccupancy;

        string category = monster.Definition.category.ToString();
        if (category.IndexOf("Boss", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return bossOccupancy;
        if (category.IndexOf("Elite", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return eliteOccupancy;
        return normalOccupancy;
    }

    private static Bounds ResolveSpriteBounds(MonsterController monster)
    {
        SpriteRenderer[] renderers = monster.GetComponentsInChildren<SpriteRenderer>(true);
        bool hasBounds = false;
        Bounds result = new(monster.transform.position, Vector3.one * 0.5f);

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sprite == null)
                continue;

            if (!hasBounds)
            {
                result = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                result.Encapsulate(renderer.bounds);
            }
        }

        return result;
    }
}
