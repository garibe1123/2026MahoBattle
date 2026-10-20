using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(31999)]
[DisallowMultipleComponent]
public sealed class CharacterStatusEffectVisual : MonoBehaviour
{
    [SerializeField] private SpriteRenderer body;
    private PlayerAnimator playerAnimator;
    private SortingGroup bodyGroup;
    private BattleWorldSortAnchor sortAnchor;
    private EffectDefinitionSO definition;
    private Color originalColor;
    private float elapsed, nextEmission;
    private bool tinted;
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
        if (body != null) bodyGroup = body.GetComponentInParent<SortingGroup>();
        playerAnimator = GetComponent<PlayerAnimator>();
        sortAnchor = BattleWorldSortAnchor.Ensure(gameObject);
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
        if (definition == null) return;
        if (body == null || !body.gameObject.activeInHierarchy) { Stop(); return; }
        elapsed += Time.deltaTime;
        EffectCharacterSettings settings = definition.character;
        if (settings.duration != -1f && elapsed >= Mathf.Max(0f, settings.duration)) { Stop(); return; }
        if (elapsed >= nextEmission) Emit();
        if (playerAnimator != null) playerAnimator.SetStatusTint(settings.tint, settings.tintAmount);
        else ApplyTint();
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            Particle particle = particles[i];
            if (particle.visual == null || !particle.visual.IsPlaying || particle.visual.ExecutionId != particle.execution)
                particles.RemoveAt(i);
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
        EffectVisual visual = EffectVisual.Spawn(definition, position, Quaternion.identity,
            duration: Sample(settings.particleLifetime, 0.01f),
            spawnScale: width / Mathf.Max(0.001f, sourceWidth * definition.visualScale));
        if (visual == null || !visual.IsPlaying) return;
        visual.SetAttachedSorting(body, bodyGroup, sortAnchor);
        particles.Add(new Particle { visual = visual, execution = visual.ExecutionId });
        if (settings.sparkle != null && Random.value < Mathf.Clamp01(settings.sparkleChance))
        {
            Sprite[] sparkFrames = settings.sparkle.GetFrames(EffectPhase.Intro);
            float sparkWidth = sparkFrames.Length > 0 ? sparkFrames[0].bounds.size.x : 1f;
            EffectVisual sparkle = EffectVisual.Spawn(settings.sparkle, position + Vector3.right * width,
                Quaternion.identity, duration: Sample(settings.particleLifetime, 0.01f),
                spawnScale: width * Sample(settings.sparkleSize, 0.01f) /
                    Mathf.Max(0.001f, sparkWidth * settings.sparkle.visualScale));
            if (sparkle == null || !sparkle.IsPlaying) return;
            sparkle.SetAttachedSorting(body, bodyGroup, sortAnchor);
            particles.Add(new Particle { visual = sparkle, execution = sparkle.ExecutionId });
        }
    }

    public void Stop()
    {
        foreach (Particle particle in particles)
            if (particle.visual != null && particle.visual.ExecutionId == particle.execution && particle.visual.IsPlaying)
            {
                particle.visual.ResetForPool();
                PoolService.Release(particle.visual.gameObject);
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
