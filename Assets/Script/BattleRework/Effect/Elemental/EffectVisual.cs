using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class EffectVisual : MonoBehaviour
{
    private const int MaxPieces = 5;
    private const int HistoryCapacity = 128;
    private static EffectVisual template;
    private readonly EffectPlayback playback = new();
    private readonly List<ElementDefinitionSO> elements = new();
    private readonly Vector3[] positions = new Vector3[HistoryCapacity];
    private readonly double[] times = new double[HistoryCapacity];
    private SpriteRenderer[] renderers;
    private Material defaultMaterial;
    private MaterialPropertyBlock properties;
    private MaterialPropertyBlock smokeProperties;
    private Transform visualRoot;
    private Light2D localLight;
    private EffectDefinitionSO definition;
    private Sprite[][] frames;
    private Sprite[][] smokeFrames;
    private int historyHead, historyCount, generation, playbackRun;
    private bool released = true;
    private bool airborne;
    private float flightElapsed, flightDuration, age;
    private float requestedDuration;
    private float variationSeed, sizeMultiplier = 1f;
    private Vector3 velocity;
    private Vector3 rootScale;
    private Camera renderCamera;
    private float frameRadius;
    private int emissionColorId, emissionStrengthId, responseId, rimId, centerId;
    private bool hasEmissionColor, hasEmissionStrength, hasResponse, hasRim, hasCenter;

    public EffectPhase Phase => playback.Phase;
    public bool IsAirborne => airborne;
    public bool IsPlaying => !released && (airborne || playback.IsPlaying);
    public double Remaining => released ? 0d : airborne
        ? requestedDuration == -1f ? double.PositiveInfinity : EffectNumbers.NonNegative(requestedDuration) + Math.Max(0d, flightDuration - flightElapsed)
        : playback.Remaining;
    public int ExecutionId => generation;
    public EffectFaction Faction { get; private set; }
    public IReadOnlyList<ElementDefinitionSO> Elements => elements;
    public event Action<EffectVisual, EffectPhase> PhaseStarted;
    public event Action<EffectVisual> Finished;

    public static EffectVisual Spawn(EffectDefinitionSO data, Vector3 position, Quaternion rotation,
        float? duration = null, Vector3 velocity = default, bool airborne = false,
        EffectFaction faction = EffectFaction.Neutral, IReadOnlyList<ElementDefinitionSO> extraElements = null,
        EffectVisual prefab = null, Action<EffectVisual, EffectPhase> phaseStarted = null,
        Action<EffectVisual> finished = null)
    {
        if (data == null) return null;
        if (data.mergeNearby && !EffectBlobWorld.CanSpawn) return null;
        if (prefab == null)
        {
            if (template == null)
            {
                GameObject host = new("ElementalVisualPoolTemplate");
                host.SetActive(false);
                host.hideFlags = HideFlags.HideInHierarchy;
                template = host.AddComponent<EffectVisual>();
            }
            prefab = template;
        }
        EffectVisual instance = PoolService.Spawn(prefab, position, rotation);
        instance.Play(data, duration ?? data.duration, velocity, airborne, faction, extraElements, phaseStarted, finished);
        return instance;
    }

    private void Awake()
    {
        rootScale = transform.localScale;
        renderCamera = Camera.main;
        GameObject root = new("Elemental Visual");
        visualRoot = root.transform;
        visualRoot.SetParent(transform, false);
        renderers = new SpriteRenderer[MaxPieces];
        for (int i = 0; i < MaxPieces; i++)
        {
            GameObject piece = new("Layer " + i);
            piece.transform.SetParent(visualRoot, false);
            renderers[i] = piece.AddComponent<SpriteRenderer>();
            renderers[i].enabled = false;
        }
        defaultMaterial = renderers[0].sharedMaterial;
        properties = new MaterialPropertyBlock();
        smokeProperties = new MaterialPropertyBlock();
        GameObject lightObject = new("Elemental Light");
        lightObject.transform.SetParent(visualRoot, false);
        localLight = lightObject.AddComponent<Light2D>();
        localLight.lightType = Light2D.LightType.Point;
        localLight.enabled = false;
        localLight.intensity = 0f;
    }

    public void Play(EffectDefinitionSO data, float duration, Vector3 motion = default,
        bool inFlight = false, EffectFaction faction = EffectFaction.Neutral,
        IReadOnlyList<ElementDefinitionSO> extraElements = null,
        Action<EffectVisual, EffectPhase> phaseStarted = null, Action<EffectVisual> finished = null)
    {
        ResetForPool();
        if (data == null) { PoolService.Release(gameObject); return; }
        definition = data;
        released = false;
        PhaseStarted = phaseStarted;
        Finished = finished;
        requestedDuration = duration;
        variationSeed = UnityEngine.Random.Range(0f, 1000f);
        float sizeRange = Mathf.Clamp(data.sizeVariation, 0f, 0.5f);
        sizeMultiplier = UnityEngine.Random.Range(1f - sizeRange, 1f + sizeRange);
        velocity = motion;
        Faction = faction;
        AddElement(data.element);
        if (data.additionalElements != null)
            foreach (ElementDefinitionSO element in data.additionalElements) AddElement(element);
        if (extraElements != null)
            for (int i = 0; i < extraElements.Count; i++) AddElement(extraElements[i]);
        frames = new[] { data.GetFrames(EffectPhase.Intro), data.GetFrames(EffectPhase.Idle), data.GetFrames(EffectPhase.End) };
        if (data.smokeTail != null)
        {
            smokeFrames = new[] { data.smokeTail.GetFrames(EffectPhase.Intro),
                data.smokeTail.GetFrames(EffectPhase.Idle), data.smokeTail.GetFrames(EffectPhase.End) };
            foreach (Sprite[] clip in smokeFrames)
                foreach (Sprite sprite in clip) frameRadius = Mathf.Max(frameRadius, sprite.bounds.extents.magnitude);
        }
        foreach (Sprite[] clip in frames)
            foreach (Sprite sprite in clip) frameRadius = Mathf.Max(frameRadius, sprite.bounds.extents.magnitude);
        Material material = data.customMaterial != null ? data.customMaterial : defaultMaterial;
        Material smokeMaterial = data.smokeTail != null && data.smokeTail.customMaterial != null
            ? data.smokeTail.customMaterial : material;
        int smokeStart = smokeFrames == null ? MaxPieces : Mathf.Max(1,
            Mathf.Clamp(data.pieceCount, 1, MaxPieces) - data.smokeTailPieces);
        if (smokeFrames != null) ConfigureSmokeProperties(smokeMaterial, data.smokeTail);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].sharedMaterial = i >= smokeStart ? smokeMaterial : material;
            renderers[i].sortingLayerID = data.sortingLayerId;
            renderers[i].sortingOrder = data.sortingOrder - i;
        }
        hasEmissionColor = ResolveProperty(material, data.emissionColorProperty, "_GlowColor", out emissionColorId);
        hasEmissionStrength = ResolveProperty(material, data.emissionStrengthProperty, null, out emissionStrengthId);
        hasResponse = ResolveProperty(material, data.lightResponseProperty, null, out responseId);
        hasRim = ResolveProperty(material, data.rimProperty, null, out rimId);
        hasCenter = ResolveProperty(material, data.centerProperty, null, out centerId);
        flightDuration = EffectNumbers.NonNegative(data.airborneDuration);
        airborne = inFlight && data.family == EffectFamily.Liquid && flightDuration > 0f;
        RecordPosition();
        int run = generation;
        if (!airborne) BeginPlayback();
        if (run == generation && !released)
        {
            Render();
            if (data.mergeNearby && !EffectBlobWorld.Register(this, data, run)) Complete(run);
        }
    }

    private void AddElement(ElementDefinitionSO element)
    {
        if (element != null && !elements.Contains(element)) elements.Add(element);
    }

    private static bool ResolveProperty(Material material, string name, string fallback, out int id)
    {
        id = 0;
        if (material == null) return false;
        if (!string.IsNullOrEmpty(name) && material.HasProperty(name))
        {
            id = Shader.PropertyToID(name);
            return true;
        }
        if (!string.IsNullOrEmpty(fallback) && material.HasProperty(fallback))
        {
            id = Shader.PropertyToID(fallback);
            return true;
        }
        return false;
    }

    private void ConfigureSmokeProperties(Material material, EffectDefinitionSO style)
    {
        smokeProperties.Clear();
        float emission = EffectNumbers.NonNegative(style.emissionStrength);
        bool strength = ResolveProperty(material, style.emissionStrengthProperty, null, out int strengthId);
        if (ResolveProperty(material, style.emissionColorProperty, "_GlowColor", out int colorId))
            smokeProperties.SetColor(colorId, style.emissionColor * (strength ? 1f : emission));
        if (strength) smokeProperties.SetFloat(strengthId, emission);
        if (ResolveProperty(material, style.lightResponseProperty, null, out int lightId))
            smokeProperties.SetFloat(lightId, Mathf.Clamp01(style.lightResponse));
        if (ResolveProperty(material, style.rimProperty, null, out int rimPropertyId))
            smokeProperties.SetFloat(rimPropertyId, style.rimStrength);
        if (ResolveProperty(material, style.centerProperty, null, out int centerPropertyId))
            smokeProperties.SetFloat(centerPropertyId, style.centerStrength);
    }

    private void BeginPlayback(bool startAtEnd = false)
    {
        int run = generation;
        playbackRun = unchecked(playback.RunId + 1);
        playback.Play(definition, requestedDuration,
            (id, phase) => { if (run == generation && id == playbackRun && !released) PhaseStarted?.Invoke(this, phase); },
            id => { if (run == generation && id == playbackRun) Complete(run); }, startAtEnd);
    }

    public void RequestStop()
    {
        if (released) return;
        if (airborne)
        {
            airborne = false;
            velocity = Vector3.zero;
            BeginPlayback(true);
            return;
        }
        playback.Stop();
    }

    private void Update()
    {
        if (released || definition == null) return;
        int run = generation;
        float delta = EffectNumbers.NonNegative(Time.deltaTime);
        age += delta;
        float flightStep = airborne ? Mathf.Min(delta, Mathf.Max(0f, flightDuration - flightElapsed)) : 0f;
        float motionStep = airborne ? flightStep : delta;
        float wanderAmount = definition.family == EffectFamily.Liquid && !airborne
            ? 0f : EffectNumbers.NonNegative(definition.motionWander);
        Vector3 wander = wanderAmount > 0f ? new Vector3(Variation(0f), Variation(19f), 0f) : Vector3.zero;
        transform.position += (velocity + wander * wanderAmount) * motionStep;
        RecordPosition();
        if (airborne)
        {
            flightElapsed += flightStep;
            if (flightElapsed >= flightDuration)
            {
                airborne = false;
                velocity = Vector3.zero;
                BeginPlayback();
                if (run != generation || released) return;
                playback.Tick(delta - flightStep);
            }
        }
        else playback.Tick(delta);
        if (run == generation && !released) Render();
    }

    private void RecordPosition()
    {
        positions[historyHead] = transform.position;
        times[historyHead] = age;
        historyHead = (historyHead + 1) % HistoryCapacity;
        historyCount = Mathf.Min(historyCount + 1, HistoryCapacity);
    }

    private float Variation(float channel)
    {
        float time = age * EffectNumbers.Positive(definition.variationFrequency, 1.5f);
        return Mathf.PerlinNoise(variationSeed + channel, time) * 2f - 1f;
    }

    private Vector3 PositionAt(double time)
    {
        if (historyCount == 0) return transform.position;
        int newer = (historyHead - 1 + HistoryCapacity) % HistoryCapacity;
        for (int i = 1; i < historyCount; i++)
        {
            int older = (newer - 1 + HistoryCapacity) % HistoryCapacity;
            if (times[older] <= time)
            {
                double span = times[newer] - times[older];
                float amount = span > 0d ? (float)((time - times[older]) / span) : 0f;
                return Vector3.Lerp(positions[older], positions[newer], amount);
            }
            newer = older;
        }
        return positions[newer];
    }

    private void Render()
    {
        if (!WithinCamera())
        {
            foreach (SpriteRenderer renderer in renderers) renderer.enabled = false;
            localLight.enabled = false;
            return;
        }
        int count = Mathf.Clamp(definition.pieceCount, 1, MaxPieces);
        int smokeStart = smokeFrames == null ? count : Mathf.Max(1, count - definition.smokeTailPieces);
        EffectPhase phase = airborne ? EffectPhase.Intro : playback.Phase;
        int phaseIndex = (int)phase - (int)EffectPhase.Intro;
        if (phaseIndex < 0 || phaseIndex >= frames.Length) return;
        Sprite[] clip = frames[phaseIndex];
        float progress = airborne ? flightElapsed / flightDuration : (float)playback.PhaseProgress;
        float envelope = airborne ? 1f : phase == EffectPhase.Intro ? progress : phase == EffectPhase.End ? 1f - progress : 1f;
        float brightness = phase == EffectPhase.Intro ? definition.introBrightness : phase == EffectPhase.End ? definition.endBrightness : definition.idleBrightness;
        float riseTime = Mathf.Min(age, 4f);
        visualRoot.localPosition = airborne ? Vector3.up * (Mathf.Sin(progress * Mathf.PI) * definition.airborneHeight)
            : definition.family == EffectFamily.Gas ? Vector3.up * (riseTime * definition.gasRise) : Vector3.zero;
        float size = EffectNumbers.NonNegative(definition.visualScale) * sizeMultiplier;
        float expansion = definition.family == EffectFamily.Gas ? Mathf.Max(0f, 1f + riseTime * definition.gasExpansion) : 1f;
        Vector2 shape = definition.family == EffectFamily.Liquid && !airborne ? definition.puddleScale : Vector2.one;
        visualRoot.localScale = new Vector3(shape.x, shape.y, 1f) * (size * expansion);
        float deformation = Mathf.Clamp(definition.shapeVariation, 0f, 0.3f);
        Vector3 variedScale = visualRoot.localScale;
        variedScale.x *= 1f + Variation(37f) * deformation;
        variedScale.y *= 1f + Variation(53f) * deformation;
        visualRoot.localScale = variedScale;
        if (clip.Length > 0)
        {
            Sprite source = clip[0];
            float ppu = source.pixelsPerUnit;
            Vector3 offset = visualRoot.localPosition;
            visualRoot.localPosition = new Vector3(Mathf.Round(offset.x * ppu) / ppu, Mathf.Round(offset.y * ppu) / ppu, offset.z);
            Vector3 scale = visualRoot.localScale;
            scale.x = Mathf.Round(scale.x * source.rect.width) / source.rect.width;
            scale.y = Mathf.Round(scale.y * source.rect.height) / source.rect.height;
            visualRoot.localScale = scale;
        }
        bool visible = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            bool smoke = i >= smokeStart && i < count;
            EffectDefinitionSO style = smoke ? definition.smokeTail : definition;
            Sprite[] pieceClip = smoke ? smokeFrames[phaseIndex] : clip;
            float delay = EffectNumbers.NonNegative(definition.pieceDelay) * i;
            renderer.enabled = i < count && age >= delay && pieceClip.Length > 0;
            if (!renderer.enabled) continue;
            int frame = airborne ? 0 : playback.FrameIndex(pieceClip.Length, delay);
            renderer.sprite = pieceClip[frame];
            float attenuation = Mathf.Pow(Mathf.Clamp01(definition.pieceAttenuation), i);
            if (smoke) attenuation = Mathf.Pow(Mathf.Clamp01(style.pieceAttenuation), i - smokeStart);
            float pieceBrightness = smoke ? phase == EffectPhase.Intro ? style.introBrightness :
                phase == EffectPhase.End ? style.endBrightness : style.idleBrightness : brightness;
            Color tint = style.tint;
            float tail = count > 1 ? (float)i / (count - 1) : 0f;
            tint *= Color.Lerp(Color.white, style.tailTint, tail);
            if (!airborne && phase == EffectPhase.End)
                tint *= Color.Lerp(Color.white, style.spentTint, progress);
            tint.r *= pieceBrightness * attenuation;
            tint.g *= pieceBrightness * attenuation;
            tint.b *= pieceBrightness * attenuation;
            tint.a *= attenuation;
            renderer.color = tint;
            Transform piece = renderer.transform;
            Vector3 trail = i == 0 ? Vector3.zero : transform.InverseTransformVector(PositionAt(age - delay) - transform.position);
            Vector2 spread = i == 0 ? Vector2.zero : definition.tailOffset * i +
                new Vector2(Variation(71f + i * 13f), Variation(139f + i * 17f)) * definition.edgeSpread * i;
            if (smoke) spread.y += Mathf.Min(Mathf.Max(0f, age - delay), 4f) * style.gasRise;
            piece.localPosition = trail / Mathf.Max(0.0001f, size * expansion) + (Vector3)spread;
            piece.localRotation = Quaternion.identity;
            float pieceScale = i == 0 ? 1f : Mathf.Lerp(0.8f, 0.45f, (float)i / MaxPieces);
            if (smoke) pieceScale = 0.8f + Mathf.Min(Mathf.Max(0f, age - delay), 4f) * EffectNumbers.NonNegative(style.gasExpansion);
            else pieceScale *= Mathf.Lerp(1f, EffectNumbers.NonNegative(style.tailExpansion), tail);
            piece.localScale = Vector3.one * (pieceScale * (1f + Variation(211f + i * 23f) * deformation));
            float ppu = renderer.sprite.pixelsPerUnit;
            Vector3 position = piece.localPosition;
            piece.localPosition = new Vector3(Mathf.Round(position.x * ppu) / ppu, Mathf.Round(position.y * ppu) / ppu, position.z);
            if (smoke) renderer.SetPropertyBlock(smokeProperties);
            else ApplyProperties(renderer, attenuation);
            visible |= renderer.isVisible;
        }
        localLight.enabled = definition.useLight && visible && envelope > 0f;
        localLight.color = definition.lightColor;
        float flicker = definition.lightMode == EffectLightMode.Flicker
            ? 1f - Mathf.Clamp01(definition.flickerAmount) * Mathf.PerlinNoise(variationSeed + 307f,
                age * EffectNumbers.NonNegative(definition.flickerFrequency)) : 1f;
        localLight.intensity = EffectNumbers.NonNegative(definition.lightIntensity) * envelope * flicker;
        localLight.pointLightInnerRadius = 0f;
        localLight.pointLightOuterRadius = EffectNumbers.Positive(definition.lightRadius, 0.01f);
    }

    internal bool CaptureBlob(EffectDefinitionSO expected, out EffectBlobSample sample)
    {
        sample = default;
        if (released || definition != expected || !renderers[0].enabled || renderers[0].sprite == null)
            return false;
        SpriteRenderer source = renderers[0];
        Bounds bounds = source.bounds;
        float growth = !airborne && Phase == EffectPhase.Intro
            ? Mathf.Lerp(0.3f, 1f, (float)playback.PhaseProgress) : 1f;
        sample.center = bounds.center;
        sample.radius = new Vector2(bounds.extents.x, bounds.extents.y) *
            (Mathf.Clamp(definition.mergeRadius, 0.2f, 1f) * growth);
        sample.weight = !airborne && Phase == EffectPhase.End ? 1f - (float)playback.PhaseProgress : 1f;
        sample.dissolve = !airborne && Phase == EffectPhase.End ? (float)playback.PhaseProgress : 0f;
        float contactAge = !airborne && flightDuration > 0f && flightElapsed >= flightDuration
            ? Mathf.Max(0f, age - flightDuration) : float.PositiveInfinity;
        sample.ripple = Mathf.Clamp01(contactAge / EffectNumbers.Positive(definition.impactFoamDuration, 0.65f));
        sample.impact = definition.family == EffectFamily.Liquid
            ? (1f - Mathf.SmoothStep(0f, 1f, sample.ripple)) * EffectNumbers.NonNegative(definition.impactFoamStrength) : 0f;
        sample.source = source;
        foreach (SpriteRenderer renderer in renderers) renderer.enabled = false;
        return sample.radius.x > 0.0001f && sample.radius.y > 0.0001f;
    }

    private void ApplyProperties(SpriteRenderer renderer, float attenuation)
    {
        if (!renderer.isVisible && !definition.mergeNearby) return;
        properties.Clear();
        float emission = EffectNumbers.NonNegative(definition.emissionStrength) * attenuation;
        if (hasEmissionColor) properties.SetColor(emissionColorId, definition.emissionColor * (hasEmissionStrength ? 1f : emission));
        if (hasEmissionStrength) properties.SetFloat(emissionStrengthId, emission);
        if (hasResponse) properties.SetFloat(responseId, Mathf.Clamp01(definition.lightResponse));
        if (hasRim) properties.SetFloat(rimId, definition.rimStrength);
        if (hasCenter) properties.SetFloat(centerId, definition.centerStrength);
        renderer.SetPropertyBlock(properties);
    }

    private bool WithinCamera()
    {
        if (renderCamera == null) return true;
        float shapeMargin = 1f + Mathf.Clamp(definition.shapeVariation, 0f, 0.3f);
        float scale = EffectNumbers.NonNegative(definition.visualScale) * sizeMultiplier * shapeMargin * shapeMargin *
            (definition.family == EffectFamily.Gas ? Mathf.Max(1f, 1f + 4f * definition.gasExpansion) :
                Mathf.Max(1f, Mathf.Max(Mathf.Abs(definition.puddleScale.x), Mathf.Abs(definition.puddleScale.y))));
        float radius = (frameRadius * Mathf.Max(1f, EffectNumbers.NonNegative(definition.tailExpansion)) + definition.tailOffset.magnitude * MaxPieces +
            Mathf.Abs(definition.edgeSpread) * MaxPieces) * scale +
            Mathf.Abs(definition.gasRise) * 4f + Mathf.Abs(definition.airborneHeight) +
            (velocity.magnitude + EffectNumbers.NonNegative(definition.motionWander) * 1.414214f) *
            EffectNumbers.NonNegative(definition.pieceDelay) * MaxPieces;
        if (definition.smokeTail != null)
            radius += (frameRadius * EffectNumbers.NonNegative(definition.smokeTail.gasExpansion) +
                Mathf.Abs(definition.smokeTail.gasRise)) * 4f * Mathf.Max(1f, scale);
        Vector3 root = transform.lossyScale;
        radius *= Mathf.Max(Mathf.Abs(root.x), Mathf.Abs(root.y));
        Vector3 viewport = renderCamera.WorldToViewportPoint(transform.position);
        if (viewport.z + radius < renderCamera.nearClipPlane || viewport.z - radius > renderCamera.farClipPlane) return false;
        float halfHeight = renderCamera.orthographic ? renderCamera.orthographicSize :
            Mathf.Max(0.01f, viewport.z) * Mathf.Tan(renderCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        float marginY = radius / Mathf.Max(0.01f, halfHeight * 2f);
        float marginX = marginY / Mathf.Max(0.01f, renderCamera.aspect);
        return viewport.x >= -marginX && viewport.x <= 1f + marginX && viewport.y >= -marginY && viewport.y <= 1f + marginY;
    }

    private void Complete(int run)
    {
        if (released || run != generation) return;
        released = true;
        Action<EffectVisual> callback = Finished;
        Finished = null;
        try { callback?.Invoke(this); }
        finally
        {
            if (run == generation && released)
            {
                ResetForPool();
                PoolService.Release(gameObject);
            }
        }
    }

    public void ResetForPool()
    {
        EffectBlobWorld.Unregister(this);
        unchecked { generation++; }
        released = true;
        playback.Reset();
        PhaseStarted = null;
        Finished = null;
        definition = null;
        frames = smokeFrames = null;
        elements.Clear();
        Faction = EffectFaction.Neutral;
        airborne = false;
        flightElapsed = flightDuration = age = requestedDuration = 0f;
        variationSeed = 0f;
        sizeMultiplier = 1f;
        velocity = Vector3.zero;
        frameRadius = 0f;
        historyHead = historyCount = 0;
        Array.Clear(positions, 0, positions.Length);
        Array.Clear(times, 0, times.Length);
        transform.localScale = rootScale;
        if (visualRoot == null) return;
        visualRoot.localPosition = Vector3.zero;
        visualRoot.localRotation = Quaternion.identity;
        visualRoot.localScale = Vector3.one;
        foreach (SpriteRenderer renderer in renderers)
        {
            renderer.enabled = false;
            renderer.sprite = null;
            renderer.color = Color.white;
            renderer.sharedMaterial = defaultMaterial;
            renderer.SetPropertyBlock(null);
            renderer.transform.localPosition = Vector3.zero;
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = Vector3.one;
        }
        properties.Clear();
        hasEmissionColor = hasEmissionStrength = hasResponse = hasRim = hasCenter = false;
        smokeProperties.Clear();
        localLight.enabled = false;
        localLight.intensity = 0f;
        localLight.color = Color.white;
        localLight.pointLightInnerRadius = 0f;
        localLight.pointLightOuterRadius = 1f;
        localLight.transform.localPosition = Vector3.zero;
        localLight.transform.localRotation = Quaternion.identity;
        localLight.transform.localScale = Vector3.one;
    }

    private void OnDisable()
    {
        ResetForPool();
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }
}
