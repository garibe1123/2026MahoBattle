using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns the short Combat -> Finish Shot -> Reward hand-off.
/// BattleRoomManager remains authoritative for alive-count / clear state; this component only presents
/// the confirmed final kill and calls CompleteCombatClearPresentation when the shot has finished.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleCombatClearPresentationController : MonoBehaviour, IInputModal
{
    [Header("Timing (unscaled seconds)")]
    [SerializeField, Range(0.80f, 1.15f)] private float normalDuration = 1.05f;
    [SerializeField, Range(1.00f, 1.45f)] private float eliteDuration = 1.20f;
    [SerializeField, Range(1.40f, 2.20f)] private float bossDuration = 1.75f;
    [SerializeField, Min(0f)] private float playerProjectileCleanupDelay = 0.08f;
    [SerializeField, Range(0.45f, 0.82f)] private float returnToPlayerFraction = 0.64f;
    [SerializeField, Range(2f, 6f)] private float deathAnimationSafetyTimeout = 4f;

    [Header("Finish Time Scale")]
    [SerializeField, Range(0.16f, 0.28f)] private float normalMinTimeScale = 0.22f;
    [SerializeField, Range(0.12f, 0.22f)] private float eliteMinTimeScale = 0.17f;
    [SerializeField, Range(0.08f, 0.14f)] private float bossMinTimeScale = 0.10f;
    [SerializeField, Range(0.015f, 0.08f)] private float hitStopDuration = 0.045f;
    [SerializeField, Range(0.01f, 0.12f)] private float hitStopScale = 0.035f;

    [Header("Final Kill Camera")]
    [SerializeField, Range(0.35f, 0.85f)] private float normalOccupancy = 0.58f;
    [SerializeField, Range(0.35f, 0.85f)] private float eliteOccupancy = 0.63f;
    [SerializeField, Range(0.35f, 0.85f)] private float bossOccupancy = 0.69f;
    [SerializeField, Range(-0.2f, 0.4f)] private float focusYBiasFraction = 0.12f;

    [Header("Finish Look")]
    [SerializeField, Range(0f, 1f)] private float gradingEmphasis = 0.70f;
    [SerializeField, Range(0f, 1f)] private float bossGradingEmphasis = 0.84f;

    [Header("Final Kill Impact")]
    [SerializeField, Range(0f, 0.20f)] private float normalCameraImpulse = 0.055f;
    [SerializeField, Range(0f, 0.25f)] private float eliteCameraImpulse = 0.075f;
    [SerializeField, Range(0f, 0.30f)] private float bossCameraImpulse = 0.11f;
    [SerializeField, Range(0.05f, 0.22f)] private float impulseDuration = 0.12f;

    private Coroutine routine;
    private BattleRoomManager activeRoom;
    private BattleCameraController battleCamera;
    private BattleTimeScaleController timeScaleController;
    private BattleInputRouter inputRouter;
    private BattleColorGradingController colorGrading;
    private int finalTargetFocusId;
    private int playerReturnFocusId;
    private bool modalPushed;

    private Canvas targetColorIsolationCanvas;
    private readonly List<TargetColorClone> targetColorClones = new();

    private sealed class TargetColorClone
    {
        public SpriteRenderer source;
        public RectTransform rect;
        public Image image;
    }

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

        PlayerShootingSystem shooting =
            FindFirstObjectByType<PlayerShootingSystem>(FindObjectsInactive.Include);
        ProjectilePooler playerPool = shooting != null ? shooting.playerProjectilePool : null;

        // Enemy + neutral/environmental pools disappear immediately. Player bullets get a tiny
        // grace window so the final fired shot can visually finish before the field is cleared.
        CleanupNonPlayerProjectilePools(playerPool);

        Bounds targetBounds = ResolveSpriteBounds(finalTarget);
        float occupancy = ResolveOccupancy(finalTarget);
        float targetDuration = ResolveDuration(finalTarget);
        float minTimeScale = ResolveMinTimeScale(finalTarget);
        float returnToPlayerAt = targetDuration * Mathf.Clamp01(returnToPlayerFraction);

        if (battleCamera != null)
        {
            finalTargetFocusId = battleCamera.FocusBoundsOccupancy(
                targetBounds,
                0f,
                occupancy,
                focusYBiasFraction,
                BattleCameraFocusPriority.LastKill);

            Vector2 impulseDirection = Vector2.right;
            if (room.PlayerTarget != null)
            {
                impulseDirection =
                    (Vector2)targetBounds.center -
                    (Vector2)room.PlayerTarget.position;

                if (impulseDirection.sqrMagnitude < 0.0001f)
                    impulseDirection = Vector2.right;
            }

            battleCamera.PushCameraImpulse(
                impulseDirection.normalized,
                ResolveCameraImpulse(finalTarget),
                impulseDuration);
        }

        BeginTargetColorIsolation(finalTarget);

        colorGrading?.SetLastKillEmphasis(
            finalTarget != null && finalTarget.IsBoss
                ? bossGradingEmphasis
                : gradingEmphasis);

        float elapsed = 0f;
        bool playerProjectilesCleaned = false;
        bool returnFocusStarted = false;

        while (true)
        {
            if (!room.IsPlayerAlive)
            {
                BattleRoomManager abortedRoom = activeRoom;
                routine = null;
                ReleasePresentationState();
                activeRoom = null;
                abortedRoom?.AbortCombatClearPresentation();
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            UpdateTargetColorIsolation();

            if (!playerProjectilesCleaned && elapsed >= playerProjectileCleanupDelay)
            {
                playerProjectilesCleaned = true;
                playerPool?.ReturnAllActive();
            }

            ApplyTimeScale(elapsed, targetDuration, minTimeScale);

            bool deathAnimationFinished =
                finalTarget == null ||
                finalTarget.DeathAnimationCompleted;

            if (!deathAnimationFinished &&
                elapsed >= Mathf.Max(targetDuration, deathAnimationSafetyTimeout))
            {
                Debug.LogWarning(
                    $"[BattleFinish] Final death animation exceeded {deathAnimationSafetyTimeout:0.00}s. " +
                    "Forcing completion so Reward cannot deadlock.",
                    finalTarget);

                finalTarget?.ForceCompleteDeathAnimationForPresentation();
                deathAnimationFinished =
                    finalTarget == null ||
                    finalTarget.DeathAnimationCompleted;
            }

            if (!returnFocusStarted &&
                elapsed >= returnToPlayerAt &&
                deathAnimationFinished)
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

            if (elapsed >= Mathf.Max(0.05f, targetDuration) &&
                deathAnimationFinished)
            {
                break;
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

    private void ApplyTimeScale(float elapsed, float duration, float minScale)
    {
        if (timeScaleController == null)
            return;

        float safeDuration = Mathf.Max(0.05f, duration);
        float safeMin = Mathf.Clamp(minScale, 0.01f, 1f);

        float scale;
        if (elapsed < hitStopDuration)
        {
            scale = Mathf.Clamp(hitStopScale, 0.01f, safeMin);
        }
        else
        {
            float normalized = Mathf.Clamp01(
                (elapsed - hitStopDuration) /
                Mathf.Max(0.01f, safeDuration - hitStopDuration));

            if (normalized < 0.10f)
                scale = Mathf.Lerp(0.55f, safeMin, normalized / 0.10f);
            else if (normalized < 0.42f)
                scale = safeMin;
            else if (normalized < 0.58f)
                scale = Mathf.Lerp(safeMin, Mathf.Max(safeMin, 0.35f), (normalized - 0.42f) / 0.16f);
            else if (normalized < 0.86f)
                scale = Mathf.Lerp(Mathf.Max(safeMin, 0.35f), 1f, (normalized - 0.58f) / 0.28f);
            else
                scale = 1f;
        }

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
        EndTargetColorIsolation();

        if (modalPushed && inputRouter != null)
            inputRouter.PopModal(this);
        modalPushed = false;
    }

    private void BeginTargetColorIsolation(MonsterController monster)
    {
        EndTargetColorIsolation();
        if (monster == null)
            return;

        GameObject canvasObject = new("LastKillTargetColorIsolation");
        canvasObject.transform.SetParent(transform, false);

        targetColorIsolationCanvas = canvasObject.AddComponent<Canvas>();
        targetColorIsolationCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        targetColorIsolationCanvas.overrideSorting = true;
        // Combat vignette is 420 and analog broadcast overlay is 430.
        // The target sits between them: exempt from desaturation/vignette, still inside broadcast noise.
        targetColorIsolationCanvas.sortingOrder = 425;

        SpriteRenderer[] renderers =
            monster.GetComponentsInChildren<SpriteRenderer>(true);

        System.Array.Sort(
            renderers,
            (a, b) =>
            {
                int aOrder = a != null ? a.sortingOrder : int.MinValue;
                int bOrder = b != null ? b.sortingOrder : int.MinValue;
                return aOrder.CompareTo(bOrder);
            });

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer source = renderers[i];
            if (!IsFinalTargetColorSource(source))
                continue;

            GameObject imageObject = new($"TargetColor_{source.name}", typeof(RectTransform));
            imageObject.transform.SetParent(canvasObject.transform, false);

            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            Image image = imageObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = false;

            targetColorClones.Add(new TargetColorClone
            {
                source = source,
                rect = rect,
                image = image
            });
        }

        UpdateTargetColorIsolation();
    }

    private void UpdateTargetColorIsolation()
    {
        if (targetColorIsolationCanvas == null)
            return;

        Camera camera = Camera.main;
        if (camera == null)
            return;

        for (int i = 0; i < targetColorClones.Count; i++)
        {
            TargetColorClone clone = targetColorClones[i];
            SpriteRenderer source = clone.source;
            if (source == null || clone.rect == null || clone.image == null)
                continue;

            bool visible =
                source.enabled &&
                source.gameObject.activeInHierarchy &&
                source.sprite != null;

            clone.image.enabled = visible;
            if (!visible)
                continue;

            clone.image.sprite = source.sprite;
            clone.image.color = source.color;

            Bounds bounds = source.bounds;
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, bounds.min);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, bounds.max);

            clone.rect.position = (min + max) * 0.5f;
            clone.rect.sizeDelta = new Vector2(
                Mathf.Max(1f, Mathf.Abs(max.x - min.x)),
                Mathf.Max(1f, Mathf.Abs(max.y - min.y)));

            clone.rect.localScale = new Vector3(
                source.flipX ? -1f : 1f,
                source.flipY ? -1f : 1f,
                1f);
        }
    }

    private void EndTargetColorIsolation()
    {
        targetColorClones.Clear();

        if (targetColorIsolationCanvas != null)
            Destroy(targetColorIsolationCanvas.gameObject);

        targetColorIsolationCanvas = null;
    }

    private static bool IsFinalTargetColorSource(SpriteRenderer renderer)
    {
        if (renderer == null || renderer.sprite == null)
            return false;

        string rendererName = renderer.name;
        return rendererName != BattleCharacterLightVisual.KeyRendererName &&
               rendererName != BattleCharacterLightVisual.PoolRendererName &&
               rendererName != BattleCharacterLightVisual.GlowRendererName &&
               rendererName != "FacingIndicator";
    }

    private float ResolveOccupancy(MonsterController monster)
    {
        if (monster == null)
            return normalOccupancy;
        if (monster.IsBoss)
            return bossOccupancy;
        if (monster.IsElite)
            return eliteOccupancy;
        return normalOccupancy;
    }

    private float ResolveDuration(MonsterController monster)
    {
        if (monster != null && monster.IsBoss)
            return bossDuration;
        if (monster != null && monster.IsElite)
            return eliteDuration;
        return normalDuration;
    }

    private float ResolveMinTimeScale(MonsterController monster)
    {
        if (monster != null && monster.IsBoss)
            return bossMinTimeScale;
        if (monster != null && monster.IsElite)
            return eliteMinTimeScale;
        return normalMinTimeScale;
    }

    private float ResolveCameraImpulse(MonsterController monster)
    {
        if (monster != null && monster.IsBoss)
            return bossCameraImpulse;
        if (monster != null && monster.IsElite)
            return eliteCameraImpulse;
        return normalCameraImpulse;
    }

    private static void CleanupNonPlayerProjectilePools(ProjectilePooler playerPool)
    {
        ProjectilePooler[] pools = FindObjectsByType<ProjectilePooler>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < pools.Length; i++)
        {
            ProjectilePooler pool = pools[i];
            if (pool == null || ReferenceEquals(pool, playerPool))
                continue;

            pool.ReturnAllActive();
        }
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
