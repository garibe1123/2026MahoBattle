using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(31999)]
[DisallowMultipleComponent]
public sealed class CharacterStatusEffectVisual : MonoBehaviour
{
    [SerializeField] private SpriteRenderer body;
    public EffectDefinitionSO defaultDefinition;
    private PlayerAnimator playerAnimator;
    private SortingGroup bodyGroup;
    private BattleWorldSortAnchor sortAnchor;
    private EffectDefinitionSO definition;
    private Color originalColor;
    private float elapsed, nextEmission;
    private bool tinted;
    private bool emitting;
    private bool preview;
    private Camera previewCamera;
    private EffectBlobWorld previewWorld;
    public bool IsPlaying => definition != null;
    private readonly List<Particle> particles = new();
    private struct Particle
    {
        public EffectVisual visual;
        public int execution;
        public EffectCharacterSettings settings;
        public Vector3 offset, lastBodyPosition, lag, detachedPosition;
        public float age, lifetime, fallDuration, fallDistance;
        public bool frost, detached;
    }

    private void Awake()
    {
        if (body == null) body = GetComponent<SpriteRenderer>();
        if (body == null) body = GetComponentInChildren<SpriteRenderer>(true);
        if (body == null) body = GetComponentInParent<SpriteRenderer>();
        if (body == null && transform.parent != null) body = transform.parent.GetComponentInChildren<SpriteRenderer>(true);
        if (body != null) bodyGroup = body.GetComponentInParent<SortingGroup>();
        playerAnimator = body != null ? body.GetComponentInParent<PlayerAnimator>() : GetComponent<PlayerAnimator>();
        if (body != null)
        {
            sortAnchor = body.GetComponentInParent<BattleWorldSortAnchor>();
            if (sortAnchor == null) sortAnchor = BattleWorldSortAnchor.Ensure(body.gameObject);
        }
    }

    private void OnEnable() { if (defaultDefinition != null) Play(defaultDefinition); }

    public void InitializePreview(SpriteRenderer target, Camera camera, EffectBlobWorld world)
    {
        preview = true;
        previewCamera = camera;
        previewWorld = world;
        body = target;
        bodyGroup = null;
        playerAnimator = null;
        sortAnchor = null;
    }

    public void Play(EffectDefinitionSO data)
    {
        Stop(true);
        if (!isActiveAndEnabled || body == null || data == null || data.character == null) return;
        definition = data;
        data.UpgradeCharacterSettings();
        emitting = true;
        elapsed = nextEmission = 0f;
        originalColor = body.color;
        tinted = true;
        if (playerAnimator != null) playerAnimator.SetStatusTint(data.character.tint, data.character.tintAmount);
        else ApplyTint();
        Emit();
    }

    private void LateUpdate()
    {
        if (!preview) Tick(Time.deltaTime);
    }

    public void Tick(float deltaTime)
    {
        if (definition == null) return;
        if (body == null || !body.gameObject.activeInHierarchy) { Stop(true); return; }
        float delta = EffectNumbers.NonNegative(deltaTime);
        elapsed += delta;
        EffectCharacterSettings settings = definition.character;
        if (emitting && settings.duration != -1f && elapsed >= Mathf.Max(0f, settings.duration)) Stop();
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            Particle particle = particles[i];
            if (preview && particle.visual != null && particle.visual.ExecutionId == particle.execution)
                particle.visual.Tick(delta);
            if (particle.visual == null || !particle.visual.IsPlaying || particle.visual.ExecutionId != particle.execution)
            {
                if (preview && particle.visual != null && !particle.visual.IsPlaying) ReleasePreview(particle.visual);
                particles.RemoveAt(i);
            }
            else
            {
                Follow(ref particle, delta);
                particle.visual.SetAttachedSorting(body, bodyGroup, sortAnchor);
                particles[i] = particle;
            }
        }
        if (!emitting) { if (particles.Count == 0) definition = null; return; }
        if (elapsed >= nextEmission) Emit();
        if (playerAnimator != null) playerAnimator.SetStatusTint(settings.tint, settings.tintAmount);
        else ApplyTint();
    }

    private void Follow(ref Particle particle, float delta)
    {
        float previousAge = particle.age;
        particle.age += delta;
        float fullFollow = Mathf.Clamp(particle.settings.fullFollowFraction, 0f, 0.99f);
        float releaseAge = particle.lifetime * fullFollow;
        Vector3 bodyPosition = body.transform.position;
        Vector3 movement = bodyPosition - particle.lastBodyPosition;
        Vector3 position = bodyPosition + particle.offset;
        float dissolve = Mathf.InverseLerp(particle.settings.fadeStart, 1f, particle.age / particle.lifetime);
        if (particle.frost && particle.age >= releaseAge)
        {
            if (!particle.detached)
            {
                float attachedStep = delta > 0f ? Mathf.Clamp01((releaseAge - previousAge) / delta) : 1f;
                particle.detachedPosition = position - movement * (1f - attachedStep);
                particle.detached = true;
            }
            float fall = Mathf.Clamp01((particle.age - releaseAge) / particle.fallDuration);
            position = particle.detachedPosition + Vector3.down * (particle.fallDistance * fall * fall);
            dissolve = fall;
        }
        else if (!particle.frost)
        {
            float release = Mathf.InverseLerp(fullFollow, 1f, particle.age / particle.lifetime);
            particle.lag = Vector3.ClampMagnitude(particle.lag - movement * release,
                body.bounds.size.x * Mathf.Max(0f, particle.settings.maxLagInBodyWidths));
            position += particle.lag;
        }
        particle.lastBodyPosition = bodyPosition;
        particle.visual.MoveAttached(position, dissolve);
    }

    private void ApplyTint()
    {
        Color color = Color.Lerp(originalColor, definition.character.tint, Mathf.Clamp01(definition.character.tintAmount));
        color.a = originalColor.a;
        body.color = color;
    }

    private static float Sample(Vector2 range, float minimum)
    {
        float low = Mathf.Max(minimum, Mathf.Min(range.x, range.y));
        return Random.Range(low, Mathf.Max(low, Mathf.Max(range.x, range.y)));
    }

    private void Emit()
    {
        EffectCharacterSettings settings = definition.character;
        float count = EffectNumbers.NonNegative(definition.countValue);
        nextEmission = elapsed + Sample(settings.emissionInterval, 0.01f) / Mathf.Max(0.01f, count);
        // 기존 풀의 동일 인스턴스 재사용을 추적하므로 다른 호출자의 효과를 회수하지 않습니다.
        if (count <= 0f || particles.Count >= 32 || !body.enabled) return;
        float radius = body.bounds.size.x * Mathf.Max(0f, settings.radiusInBodyWidths);
        Vector2 offset = Random.insideUnitCircle * radius;
        Vector3 position = body.bounds.center + new Vector3(offset.x, offset.y, 0f);
        float width = Sample(settings.particleSize, 0.01f);
        Sprite[] frames = definition.GetFrames(EffectPhase.Intro);
        float sourceWidth = !definition.HasCharacterShape && frames.Length > 0 ? frames[0].bounds.size.x : 1f;
        if (definition.fireParticles)
            sourceWidth = 2f * (Mathf.Max(0f, definition.fireSourceSpread) +
                Mathf.Max(definition.fireParticleRadius.x, definition.fireParticleRadius.y) *
                Mathf.Max(definition.fireSourceScale.x, definition.fireSourceScale.y) *
                Mathf.Max(0.01f, definition.fireShapeScale.x));
        float scale = ParticleScale(definition, width, sourceWidth);
        width = sourceWidth * definition.visualScale * scale;
        EffectVisual visual = SpawnParticle(definition, position, Sample(settings.particleLifetime, 0.01f), scale);
        if (visual == null || !visual.IsPlaying) return;
        if (settings.sparkle != null && Random.value < Mathf.Clamp01(settings.sparkleChance))
        {
            Sprite[] sparkFrames = settings.sparkle.GetFrames(EffectPhase.Intro);
            float sparkWidth = !settings.sparkle.HasCharacterShape && sparkFrames.Length > 0 ? sparkFrames[0].bounds.size.x : 1f;
            Vector2 sparkOffset = Random.insideUnitCircle * width * 0.7f;
            EffectVisual sparkle = SpawnParticle(settings.sparkle, position + (Vector3)sparkOffset,
                Sample(settings.particleLifetime, 0.01f), ParticleScale(settings.sparkle,
                    width * Sample(settings.sparkleSize, 0.01f), sparkWidth));
            if (sparkle == null || !sparkle.IsPlaying) return;
        }
    }

    private EffectVisual SpawnParticle(EffectDefinitionSO data, Vector3 position, float lifetime, float scale)
    {
        EffectCharacterSettings settings = data.character;
        bool frost = settings.shape == EffectCharacterShape.FrostShard;
        float fallDuration = frost ? Sample(settings.frostFallDuration, 0.01f) : 0f;
        if (frost) lifetime = Mathf.Max(lifetime, fallDuration / (1f - Mathf.Clamp(settings.fullFollowFraction, 0f, 0.99f)));
        EffectVisual visual;
        if (!preview)
            visual = EffectVisual.Spawn(data, position, Quaternion.identity, duration: lifetime, spawnScale: scale, attachedParticle: true);
        else
        {
            GameObject host = new("Preview Status Particle") { hideFlags = HideFlags.HideAndDontSave };
            host.transform.SetParent(transform, false);
            host.transform.position = position;
            visual = host.AddComponent<EffectVisual>();
            visual.InitializePreview(previewCamera, previewWorld);
            visual.Play(data, lifetime, spawnScale: scale, attachedParticle: true);
            if (!visual.IsPlaying) { ReleasePreview(visual); return null; }
        }
        if (visual == null || !visual.IsPlaying) return null;
        visual.SetAttachedSorting(body, bodyGroup, sortAnchor);
        particles.Add(new Particle { visual = visual, execution = visual.ExecutionId, settings = settings,
            offset = position - body.transform.position, lastBodyPosition = body.transform.position,
            lifetime = lifetime, frost = frost, fallDuration = fallDuration,
            fallDistance = frost ? Sample(settings.frostFallInBodyHeights, 0f) * body.bounds.size.y : 0f });
        return visual;
    }

    private static void ReleasePreview(EffectVisual visual)
    {
        visual.DisposePreview();
        DestroyImmediate(visual.gameObject);
    }

    private static float ParticleScale(EffectDefinitionSO data, float width, float sourceWidth)
    {
        return width / Mathf.Max(0.001f, sourceWidth * data.visualScale);
    }

    public void Stop() { Stop(false); }

    public void Stop(bool immediate)
    {
        emitting = false;
        if (immediate)
        {
            foreach (Particle particle in particles)
                if (particle.visual != null && (preview || particle.visual.ExecutionId == particle.execution))
                {
                    if (preview) ReleasePreview(particle.visual);
                    else if (particle.visual.IsPlaying)
                    {
                        particle.visual.ResetForPool();
                        PoolService.Release(particle.visual.gameObject);
                    }
                }
            particles.Clear();
        }
        if (particles.Count == 0) definition = null;
        if (tinted)
        {
            if (playerAnimator != null) playerAnimator.ClearStatusTint();
            else if (body != null) body.color = originalColor;
        }
        tinted = false;
    }

    private void OnDisable() { Stop(true); }
}
