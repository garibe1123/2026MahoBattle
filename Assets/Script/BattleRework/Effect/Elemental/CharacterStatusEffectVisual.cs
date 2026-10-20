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
    private bool preview;
    private Camera previewCamera;
    private EffectBlobWorld previewWorld;
    public bool IsPlaying => definition != null;
    private readonly List<Particle> particles = new();
    private struct Particle
    {
        public EffectVisual visual;
        public int execution;
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
        Stop();
        if (!isActiveAndEnabled || body == null || data == null || data.character == null) return;
        definition = data;
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
        if (body == null || !body.gameObject.activeInHierarchy) { Stop(); return; }
        if (preview)
            for (int i = 0; i < particles.Count; i++)
                if (particles[i].visual != null) particles[i].visual.Tick(deltaTime);
        elapsed += deltaTime;
        EffectCharacterSettings settings = definition.character;
        if (settings.duration != -1f && elapsed >= Mathf.Max(0f, settings.duration)) { Stop(); return; }
        if (elapsed >= nextEmission) Emit();
        if (playerAnimator != null) playerAnimator.SetStatusTint(settings.tint, settings.tintAmount);
        else ApplyTint();
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            Particle particle = particles[i];
            if (particle.visual == null || !particle.visual.IsPlaying || particle.visual.ExecutionId != particle.execution)
            {
                if (preview && particle.visual != null) ReleasePreview(particle.visual);
                particles.RemoveAt(i);
            }
            else particle.visual.SetAttachedSorting(body, bodyGroup, sortAnchor);
        }
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
        nextEmission = elapsed + Sample(settings.emissionInterval, 0.01f);
        // 기존 풀의 동일 인스턴스 재사용을 추적하므로 다른 호출자의 효과를 회수하지 않습니다.
        if (particles.Count >= 32 || !body.enabled) return;
        float radius = body.bounds.size.x * Mathf.Max(0f, settings.radiusInBodyWidths);
        Vector2 offset = Random.insideUnitCircle * radius;
        Vector3 position = body.bounds.center + new Vector3(offset.x, offset.y, 0f);
        float width = Sample(settings.particleSize, 0.01f);
        Sprite[] frames = definition.GetFrames(EffectPhase.Intro);
        float sourceWidth = frames.Length > 0 ? frames[0].bounds.size.x : 1f;
        if (definition.fireParticles)
            sourceWidth = 2f * (Mathf.Max(0f, definition.fireSourceSpread) +
                Mathf.Max(definition.fireParticleRadius.x, definition.fireParticleRadius.y) *
                Mathf.Max(definition.fireSourceScale.x, definition.fireSourceScale.y) *
                Mathf.Max(0.01f, definition.fireShapeScale.x));
        float scale = ParticleScale(definition, frames, width, sourceWidth);
        width = sourceWidth * definition.visualScale * scale;
        EffectVisual visual = SpawnParticle(definition, position, Sample(settings.particleLifetime, 0.01f), scale);
        if (visual == null || !visual.IsPlaying) return;
        visual.SetAttachedSorting(body, bodyGroup, sortAnchor);
        particles.Add(new Particle { visual = visual, execution = visual.ExecutionId });
        if (settings.sparkle != null && Random.value < Mathf.Clamp01(settings.sparkleChance))
        {
            Sprite[] sparkFrames = settings.sparkle.GetFrames(EffectPhase.Intro);
            float sparkWidth = sparkFrames.Length > 0 ? sparkFrames[0].bounds.size.x : 1f;
            EffectVisual sparkle = SpawnParticle(settings.sparkle, position + Vector3.right * width,
                Sample(settings.particleLifetime, 0.01f), ParticleScale(settings.sparkle, sparkFrames,
                    width * Sample(settings.sparkleSize, 0.01f), sparkWidth));
            if (sparkle == null || !sparkle.IsPlaying) return;
            sparkle.SetAttachedSorting(body, bodyGroup, sortAnchor);
            particles.Add(new Particle { visual = sparkle, execution = sparkle.ExecutionId });
        }
    }

    private EffectVisual SpawnParticle(EffectDefinitionSO data, Vector3 position, float lifetime, float scale)
    {
        if (!preview) return EffectVisual.Spawn(data, position, Quaternion.identity, duration: lifetime, spawnScale: scale);
        GameObject host = new("Preview Status Particle") { hideFlags = HideFlags.HideAndDontSave };
        host.transform.SetParent(transform, false);
        host.transform.position = position;
        EffectVisual visual = host.AddComponent<EffectVisual>();
        visual.InitializePreview(previewCamera, previewWorld);
        visual.Play(data, lifetime, spawnScale: scale);
        if (!visual.IsPlaying) { ReleasePreview(visual); return null; }
        return visual;
    }

    private static void ReleasePreview(EffectVisual visual)
    {
        visual.DisposePreview();
        DestroyImmediate(visual.gameObject);
    }

    private float ParticleScale(EffectDefinitionSO data, Sprite[] frames, float width, float sourceWidth)
    {
        float scale = width / Mathf.Max(0.001f, sourceWidth * data.visualScale);
        if (data.fireParticles || data.character == null || !data.character.alignToWorldPixels || frames.Length == 0) return scale;
        float pixelSize = preview && previewWorld != null ? previewWorld.DetailPixelSize(data) : EffectBlobWorld.DefaultDetailPixelSize(data);
        float pixelScale = pixelSize * frames[0].pixelsPerUnit /
            Mathf.Max(0.001f, data.visualScale);
        return pixelScale * Mathf.Max(1f, Mathf.Round(scale / pixelScale));
    }

    public void Stop()
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
        definition = null;
        if (tinted)
        {
            if (playerAnimator != null) playerAnimator.ClearStatusTint();
            else if (body != null) body.color = originalColor;
        }
        tinted = false;
    }

    private void OnDisable() { Stop(); }
}
