using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class EffectVisual : MonoBehaviour
{
    private const int MaxPieces = 5;
    private const int HistoryCapacity = 128;
    private static EffectVisual template;
    private readonly EffectPlayback playback = new();
    private EffectFireParticles fire;
    private EffectDetailParticles details;
    private SpriteRenderer[] detailRenderers;
    private Texture2D detailTexture;
    private Sprite detailSprite;
    private Sprite pixelDetailSprite;
    private EffectBlobWorld previewWorld;
    private bool preview;
    private readonly float[] pieceSizes = new float[MaxPieces];
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
    private bool anchored;
    private Vector3 anchorPosition;
    private float flightElapsed, flightDuration, age;
    private float requestedDuration;
    private float variationSeed, sizeMultiplier = 1f;
    private Vector3 velocity;
    private Vector3 rootScale;
    private Camera renderCamera;
    private float frameRadius;
    private bool withinCamera;
    private Vector3 sortingGroundPosition;
    private SpriteRenderer attachedRenderer;
    private SortingGroup attachedGroup;
    private BattleWorldSortAnchor attachedAnchor;
    private int AttachedOrder => attachedAnchor != null && attachedAnchor.isActiveAndEnabled
        ? BattleWorldSorting.WorldYToOrder(attachedAnchor.CurrentAnchorWorldY, attachedAnchor.LocalSortOffset) + 1
        : (attachedGroup != null && attachedGroup.isActiveAndEnabled ? attachedGroup.sortingOrder : attachedRenderer.sortingOrder) + 1;
    private EffectRenderSort appliedSorting;
    private FieldBeamEffectVisual beamVisual;
    internal EffectRenderSort RenderSorting => attachedGroup != null && attachedGroup.isActiveAndEnabled
        ? new EffectRenderSort(attachedGroup.sortingLayerID, AttachedOrder, EffectSortMode.Attached)
        : attachedRenderer != null
        ? new EffectRenderSort(attachedRenderer.sortingLayerID, AttachedOrder, EffectSortMode.Attached)
        : definition != null ? definition.ResolveSorting(sortingGroundPosition) : default;
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
        Action<EffectVisual> finished = null, bool stationary = false, float spawnScale = 1f)
    {
        if (data == null) return null;
        if ((data.mergeNearby || data.fireParticles) && !EffectBlobWorld.CanSpawn) return null;
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
        instance.Play(data, duration ?? data.duration, velocity, airborne, faction, extraElements, phaseStarted, finished, stationary, spawnScale);
        return instance;
    }

    private void Awake()
    {
        InitializeVisuals(Camera.main);
    }

    public void InitializePreview(Camera camera, EffectBlobWorld world)
    {
        preview = true;
        previewWorld = world;
        InitializeVisuals(camera);
        renderCamera = camera;
    }

    private void InitializeVisuals(Camera camera)
    {
        if (visualRoot != null) return;
        rootScale = transform.localScale;
        renderCamera = camera;
        GameObject root = new("Elemental Visual") { hideFlags = gameObject.hideFlags };
        visualRoot = root.transform;
        visualRoot.SetParent(transform, false);
        renderers = new SpriteRenderer[MaxPieces];
        for (int i = 0; i < MaxPieces; i++)
        {
            GameObject piece = new("Layer " + i) { hideFlags = gameObject.hideFlags };
            piece.transform.SetParent(visualRoot, false);
            renderers[i] = piece.AddComponent<SpriteRenderer>();
            renderers[i].enabled = false;
        }
        defaultMaterial = renderers[0].sharedMaterial;
        properties = new MaterialPropertyBlock();
        smokeProperties = new MaterialPropertyBlock();
        GameObject lightObject = new("Elemental Light") { hideFlags = gameObject.hideFlags };
        lightObject.transform.SetParent(visualRoot, false);
        localLight = lightObject.AddComponent<Light2D>();
        localLight.lightType = Light2D.LightType.Point;
        localLight.enabled = false;
        localLight.intensity = 0f;
    }

    public void Play(EffectDefinitionSO data, float duration, Vector3 motion = default,
        bool inFlight = false, EffectFaction faction = EffectFaction.Neutral,
        IReadOnlyList<ElementDefinitionSO> extraElements = null,
        Action<EffectVisual, EffectPhase> phaseStarted = null, Action<EffectVisual> finished = null, bool stationary = false, float spawnScale = 1f)
    {
        ResetForPool();
        if (data == null) { if (!preview) PoolService.Release(gameObject); return; }
        transform.localScale = rootScale * EffectNumbers.NonNegative(spawnScale);
        definition = data;
        released = false;
        PhaseStarted = phaseStarted;
        Finished = finished;
        requestedDuration = duration;
        anchored = duration == -1f || stationary;
        anchorPosition = transform.position;
        sortingGroundPosition = transform.position;
        variationSeed = UnityEngine.Random.Range(0f, 1000f);
        sizeMultiplier = (data.beam != null && data.beam.kind != EffectBeamKind.None) ||
            (data.ResolvedSorting == EffectSortMode.Attached && !data.fireParticles && data.character != null && data.character.alignToWorldPixels)
            ? 1f : EffectNumbers.SpawnScale(data);
        pieceSizes[0] = 1f;
        for (int i = 1; i < MaxPieces; i++) pieceSizes[i] = EffectNumbers.SpawnScale(data) / sizeMultiplier;
        velocity = anchored ? Vector3.zero : motion;
        if (data.fireParticles)
        {
            fire ??= new EffectFireParticles();
            Vector3 scale = transform.lossyScale;
            fire.Play(data, transform.position, velocity,
                Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)) *
                EffectNumbers.NonNegative(data.visualScale) * sizeMultiplier);
        }
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
        Vector2 frameExtent = Vector2.zero;
        foreach (Sprite[] clip in frames)
            foreach (Sprite sprite in clip)
            {
                frameRadius = Mathf.Max(frameRadius, sprite.bounds.extents.magnitude);
                frameExtent = Vector2.Max(frameExtent, new Vector2(sprite.bounds.extents.x, sprite.bounds.extents.y));
            }
        EffectDetailSettings detailSettings = data.fireParticles ? data.embers :
            data.family == EffectFamily.Liquid ? data.waterDetails : data.gasDetails;
        if (detailSettings != null && detailSettings.enabled)
        {
            EnsureDetailRenderers();
            details ??= new EffectDetailParticles();
            Vector3 scale = transform.lossyScale;
            float size = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)) *
                EffectNumbers.NonNegative(data.visualScale) * sizeMultiplier;
            Vector2 shape = data.family == EffectFamily.Liquid && !inFlight ? data.puddleScale : Vector2.one;
            Vector2 footprint = Vector2.Scale(frameExtent, new Vector2(Mathf.Abs(shape.x), Mathf.Abs(shape.y))) * size;
            float pixelSize = preview && previewWorld != null ? previewWorld.DetailPixelSize(data) : EffectBlobWorld.DefaultDetailPixelSize(data);
            details.Play(data, detailSettings, transform.position, size, footprint, pixelSize);
            foreach (SpriteRenderer renderer in detailRenderers)
            {
                renderer.sprite = details.IsCloud ? detailSprite : pixelDetailSprite;
                renderer.sharedMaterial = defaultMaterial;
                RenderSorting.Apply(renderer, 1);
            }
        }
        Material material = data.customMaterial != null ? data.customMaterial : defaultMaterial;
        Material smokeMaterial = data.smokeTail != null && data.smokeTail.customMaterial != null
            ? data.smokeTail.customMaterial : material;
        int smokeStart = smokeFrames == null ? MaxPieces : Mathf.Max(1,
            PieceCount(data) - data.smokeTailPieces);
        if (smokeFrames != null) ConfigureSmokeProperties(smokeMaterial, data.smokeTail);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].sharedMaterial = i >= smokeStart ? smokeMaterial : material;
            RenderSorting.Apply(renderers[i], -i);
        }
        hasEmissionColor = ResolveProperty(material, data.emissionColorProperty, "_GlowColor", out emissionColorId);
        hasEmissionStrength = ResolveProperty(material, data.emissionStrengthProperty, null, out emissionStrengthId);
        hasResponse = ResolveProperty(material, data.lightResponseProperty, null, out responseId);
        hasRim = ResolveProperty(material, data.rimProperty, null, out rimId);
        hasCenter = ResolveProperty(material, data.centerProperty, null, out centerId);
        flightDuration = EffectNumbers.NonNegative(data.airborneDuration);
        airborne = !anchored && inFlight && data.family == EffectFamily.Liquid && flightDuration > 0f;
        RecordPosition();
        int run = generation;
        if (!airborne) BeginPlayback();
        if (run == generation && !released)
        {
            Render();
            if (data.mergeNearby || data.fireParticles)
            {
                bool registered = preview ? previewWorld != null && previewWorld.RegisterVisual(this, data, run)
                    : EffectBlobWorld.Register(this, data, run);
                if (!registered) Complete(run);
            }
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
        if (!preview) Tick(Time.deltaTime);
    }

    public void Tick(float deltaTime)
    {
        if (released || definition == null) return;
        int run = generation;
        float delta = EffectNumbers.NonNegative(deltaTime);
        age += delta;
        float flightStep = airborne ? Mathf.Min(delta, Mathf.Max(0f, flightDuration - flightElapsed)) : 0f;
        if (anchored) transform.position = anchorPosition;
        else
        {
            float motionStep = airborne ? flightStep : delta;
            float wanderAmount = definition.family == EffectFamily.Liquid && !airborne
                ? 0f : EffectNumbers.NonNegative(definition.motionWander);
            Vector3 wander = wanderAmount > 0f ? new Vector3(Variation(0f), Variation(19f), 0f) : Vector3.zero;
            transform.position += (velocity + wander * wanderAmount) * motionStep;
            RecordPosition();
        }
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
        if (run == generation && !released)
        {
            if (definition.fireParticles)
                fire.Tick(delta, transform.position, velocity, Phase, (float)playback.PhaseProgress);
            details?.Tick(delta, definition.fireParticles ? transform.position : visualRoot.position,
                Phase, (float)playback.PhaseProgress);
            Render();
        }
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

    private float VisualCurveProgress => requestedDuration == -1f
        ? Mathf.Repeat(age / EffectNumbers.Positive(definition.duration, 2f), 1f)
        : Mathf.Clamp01((float)(playback.Elapsed / Math.Max(0.0001d, playback.Elapsed + playback.Remaining)));

    private void Render()
    {
        ApplySorting();
        withinCamera = WithinCamera();
        RenderDetails(withinCamera);
        if (definition.beam != null && definition.beam.kind != EffectBeamKind.None)
        {
            foreach (SpriteRenderer renderer in renderers) renderer.enabled = false;
            localLight.enabled = false;
            if (!withinCamera) { beamVisual?.Clear(); return; }
            beamVisual ??= new FieldBeamEffectVisual(transform, defaultMaterial);
            float pixelStep = preview && previewWorld != null ? previewWorld.DetailPixelSize(definition) :
                EffectBlobWorld.DefaultDetailPixelSize(definition);
            float beamEnvelope = Mathf.Clamp01(age / 0.025f) *
                (double.IsInfinity(Remaining) ? 1f : Mathf.Clamp01((float)Remaining / 0.075f));
            beamVisual.Render(definition.beam, RenderSorting, age,
                EffectNumbers.NonNegative(definition.visualScale) * sizeMultiplier, pixelStep, renderCamera, beamEnvelope,
                definition.customMaterial != null ? definition.customMaterial : defaultMaterial);
            localLight.transform.position = beamVisual.Center;
            localLight.color = definition.beam.edge;
            localLight.pointLightOuterRadius = EffectNumbers.Positive(definition.beam.lightRadius, 2.5f);
            localLight.intensity = EffectNumbers.NonNegative(definition.beam.lightIntensity) * beamVisual.Intensity;
            localLight.enabled = definition.beam.useLight && localLight.intensity > 0f;
            return;
        }
        if (!withinCamera)
        {
            foreach (SpriteRenderer renderer in renderers) renderer.enabled = false;
            localLight.enabled = false;
            return;
        }
        if (definition.fireParticles)
        {
            foreach (SpriteRenderer renderer in renderers) renderer.enabled = false;
            SpriteRenderer fireSource = renderers[0];
            fireSource.sprite = frames[0].Length > 0 ? frames[0][0] :
                frames[1].Length > 0 ? frames[1][0] : frames[2].Length > 0 ? frames[2][0] : null;
            fireSource.color = Color.white;
            ApplyProperties(fireSource, 0f);
            float fireEnvelope = Phase == EffectPhase.Intro ? (float)playback.PhaseProgress :
                Phase == EffectPhase.End ? 1f - (float)playback.PhaseProgress : 1f;
            float fireFlicker = definition.lightMode == EffectLightMode.Flicker
                ? 1f + (Mathf.PerlinNoise(variationSeed + 71f,
                    age * EffectNumbers.NonNegative(definition.flickerFrequency)) * 2f - 1f) *
                    Mathf.Clamp01(definition.flickerAmount) : 1f;
            localLight.color = definition.lightColor;
            localLight.pointLightOuterRadius = EffectNumbers.Positive(definition.lightRadius, 2f);
            localLight.intensity = EffectNumbers.NonNegative(definition.lightIntensity) * fireEnvelope * fireFlicker;
            localLight.enabled = definition.useLight && localLight.intensity > 0f;
            return;
        }
        int count = PieceCount(definition);
        int smokeStart = smokeFrames == null ? count : Mathf.Max(1, count - definition.smokeTailPieces);
        EffectPhase phase = airborne ? EffectPhase.Intro : playback.Phase;
        int phaseIndex = (int)phase - (int)EffectPhase.Intro;
        if (phaseIndex < 0 || phaseIndex >= frames.Length) return;
        Sprite[] clip = frames[phaseIndex];
        float progress = airborne ? flightElapsed / flightDuration : (float)playback.PhaseProgress;
        float envelope = airborne ? 1f : phase == EffectPhase.Intro ? progress : phase == EffectPhase.End ? 1f - progress : 1f;
        float brightness = phase == EffectPhase.Intro ? definition.introBrightness : phase == EffectPhase.End ? definition.endBrightness : definition.idleBrightness;
        float riseTime = anchored ? 0f : Mathf.Min(age, 4f);
        visualRoot.localPosition = airborne ? Vector3.up * (Mathf.Sin(progress * Mathf.PI) * definition.airborneHeight)
            : definition.family == EffectFamily.Gas ? Vector3.up * (riseTime * definition.gasRise) : Vector3.zero;
        float size = EffectNumbers.NonNegative(definition.visualScale) * sizeMultiplier;
        float curveProgress = VisualCurveProgress;
        visualRoot.localPosition += (Vector3)(definition.visualCurves?.Offset(curveProgress) ?? Vector2.zero) * size;
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
            if (style.visualCurves != null && style.visualCurves.enabled)
                tint *= Color.Lerp(Color.white, style.spentTint, style.visualCurves.ColorProgress(curveProgress));
            else if (!airborne && phase == EffectPhase.End)
                tint *= Color.Lerp(Color.white, style.spentTint, progress);
            tint.r *= pieceBrightness * attenuation;
            tint.g *= pieceBrightness * attenuation;
            tint.b *= pieceBrightness * attenuation;
            tint.a *= attenuation;
            tint.a *= style.visualCurves?.Opacity(curveProgress) ?? 1f;
            if (style.fadeOnEnd && !airborne && phase == EffectPhase.End) tint.a *= 1f - progress;
            renderer.color = tint;
            Transform piece = renderer.transform;
            Vector3 trail = i == 0 ? Vector3.zero : transform.InverseTransformVector(PositionAt(age - delay) - transform.position);
            Vector2 spread = i == 0 ? Vector2.zero : definition.tailOffset * i +
                new Vector2(Variation(71f + i * 13f), Variation(139f + i * 17f)) * definition.edgeSpread * i;
            if (i > 0 && definition.mixedPieceSizes && !smoke && !airborne)
            {
                Vector3 extent = renderer.sprite.bounds.extents;
                float side = i % 2 == 0 ? 1f : -1f;
                spread = new Vector2(side * extent.x * 0.55f, extent.y * 0.1f) * ((i + 1) / 2);
                spread += new Vector2(Variation(71f), Variation(139f)) * definition.edgeSpread * 0.15f;
            }
            if (smoke && !anchored) spread.y += Mathf.Min(Mathf.Max(0f, age - delay), 4f) * style.gasRise;
            piece.localPosition = trail / Mathf.Max(0.0001f, size * expansion) + (Vector3)spread;
            piece.localRotation = Quaternion.identity;
            float pieceScale = i == 0 ? 1f : Mathf.Lerp(0.8f, 0.45f, (float)i / MaxPieces);
            if (definition.mixedPieceSizes) pieceScale = EffectNumbers.PieceScale(definition, i);
            if (smoke) pieceScale = 0.8f + (anchored ? 0f : Mathf.Min(Mathf.Max(0f, age - delay), 4f)) * EffectNumbers.NonNegative(style.gasExpansion);
            else pieceScale *= Mathf.Lerp(1f, EffectNumbers.NonNegative(style.tailExpansion), tail);
            piece.localScale = Vector3.one * (pieceScale * pieceSizes[i] * (1f + Variation(211f + i * 23f) * deformation));
            float ppu = renderer.sprite.pixelsPerUnit;
            Vector3 position = piece.localPosition;
            piece.localPosition = new Vector3(Mathf.Round(position.x * ppu) / ppu, Mathf.Round(position.y * ppu) / ppu, position.z);
            if (definition.ResolvedSorting == EffectSortMode.Attached && definition.character != null && definition.character.alignToWorldPixels)
            {
                float step = preview && previewWorld != null ? previewWorld.DetailPixelSize(definition) :
                    EffectBlobWorld.DefaultDetailPixelSize(definition);
                Vector3 worldPosition = piece.position;
                float offsetX = renderer.sprite.rect.width % 2f * 0.5f;
                float offsetY = renderer.sprite.rect.height % 2f * 0.5f;
                piece.position = new Vector3((Mathf.Round(worldPosition.x / step - offsetX) + offsetX) * step,
                    (Mathf.Round(worldPosition.y / step - offsetY) + offsetY) * step, worldPosition.z);
            }
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

    private static int PieceCount(EffectDefinitionSO data) => Mathf.Clamp(
        data.mixedPieceSizes && !data.fireParticles ? Mathf.Max(3, data.pieceCount) : data.pieceCount, 1, MaxPieces);

    internal int BlobSampleCount => definition != null ? PieceCount(definition) +
        (details != null && details.IsCloud ? details.Capacity : 0) : 0;

    internal bool CaptureBlob(EffectDefinitionSO expected, out EffectBlobSample sample) => CaptureBlob(expected, 0, out sample);

    internal void HideBlobSources(EffectDefinitionSO expected)
    {
        if (released || definition != expected) return;
        int count = PieceCount(definition);
        for (int i = 0; i < count; i++)
            if (renderers[i].sprite != null) renderers[i].enabled = false;
        if (details == null || !details.IsCloud || detailRenderers == null) return;
        for (int i = 0; i < details.Capacity; i++) detailRenderers[i].enabled = false;
    }

    internal bool CaptureBlob(EffectDefinitionSO expected, int index, out EffectBlobSample sample)
    {
        sample = default;
        if (!released && definition == expected && index >= PieceCount(definition))
            return CaptureCloud(index - PieceCount(definition), out sample);
        if (released || definition != expected || index < 0 || index >= renderers.Length ||
            !renderers[index].enabled || renderers[index].sprite == null)
            return false;
        SpriteRenderer source = renderers[index];
        Bounds bounds = source.bounds;
        float growth = !airborne && Phase == EffectPhase.Intro
            ? Mathf.Lerp(0.3f, 1f, (float)playback.PhaseProgress) : 1f;
        sample.center = bounds.center;
        sample.radius = new Vector2(bounds.extents.x, bounds.extents.y) *
            (Mathf.Clamp(definition.mergeRadius, 0.2f, 1f) * growth);
        sample.weight = !airborne && Phase == EffectPhase.End ? 1f - (float)playback.PhaseProgress : 1f;
        sample.dissolve = !definition.fadeOnEnd && !airborne && Phase == EffectPhase.End ? (float)playback.PhaseProgress : 0f;
        if (definition.fadeOnEnd && !airborne && Phase == EffectPhase.End)
            sample.radius *= 1f + EffectNumbers.NonNegative(definition.gasExpansion) * (float)playback.PhaseProgress;
        float contactAge = !airborne && flightDuration > 0f && flightElapsed >= flightDuration
            ? Mathf.Max(0f, age - flightDuration) : float.PositiveInfinity;
        sample.ripple = Mathf.Clamp01(contactAge / EffectNumbers.Positive(definition.impactFoamDuration, 0.65f));
        sample.impact = definition.family == EffectFamily.Liquid
            ? (1f - Mathf.SmoothStep(0f, 1f, sample.ripple)) * EffectNumbers.NonNegative(definition.impactFoamStrength) : 0f;
        sample.source = source;
        sample.sorting = RenderSorting;
        source.enabled = false;
        return sample.radius.x > 0.0001f && sample.radius.y > 0.0001f;
    }

    private bool CaptureCloud(int index, out EffectBlobSample sample)
    {
        sample = default;
        if (details == null || !details.IsCloud || index < 0 || index >= details.Capacity ||
            !detailRenderers[index].enabled || !details.Capture(index, out Vector3 position, out Vector2 size, out Color color))
            return false;
        sample.center = position;
        sample.radius = size * (0.5f * Mathf.Clamp(definition.mergeRadius, 0.2f, 1f));
        sample.weight = Mathf.Clamp01(color.a / Mathf.Max(0.0001f, definition.tint.a));
        sample.source = detailRenderers[index];
        sample.sorting = RenderSorting;
        detailRenderers[index].enabled = false;
        return sample.radius.x > 0.0001f && sample.radius.y > 0.0001f;
    }

    internal int FireSampleCount => withinCamera && definition != null && definition.fireParticles && fire != null ? fire.Capacity : 0;

    internal bool CaptureFire(int index, out EffectBlobSample sample)
    {
        sample = default;
        if (!withinCamera || !IsPlaying || definition == null || !definition.fireParticles || fire == null || renderers[0].sprite == null)
            return false;
        if (!fire.Capture(index, out sample)) return false;
        sample.source = renderers[0];
        sample.sorting = RenderSorting;
        return true;
    }

    private void ApplyProperties(SpriteRenderer renderer, float attenuation)
    {
        if (!renderer.isVisible && !definition.mergeNearby && !definition.fireParticles) return;
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
        if (definition.beam != null && definition.beam.kind != EffectBeamKind.None)
            radius = Mathf.Max(radius, (definition.beam.length + definition.beam.width + definition.beam.zigzag +
                definition.beam.glowRadius + definition.beam.wobble) *
                EffectNumbers.NonNegative(definition.visualScale) * sizeMultiplier * Mathf.Max(Mathf.Abs(root.x), Mathf.Abs(root.y)));
        radius += (definition.visualCurves?.Offset(VisualCurveProgress) ?? Vector2.zero).magnitude *
            EffectNumbers.NonNegative(definition.visualScale) * sizeMultiplier * Mathf.Max(Mathf.Abs(root.x), Mathf.Abs(root.y));
        if (details != null) radius += details.VisibilityRadius;
        if (definition.fireParticles && fire != null)
            radius = Mathf.Max(radius, fire.GetVisibilityRadius(transform.position));
        if (definition.useLight)
            radius += EffectNumbers.Positive(definition.lightRadius, definition.fireParticles ? 2f : 0.01f);
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
                if (!preview) PoolService.Release(gameObject);
            }
        }
    }

    public void ResetForPool()
    {
        if (released && definition == null) return;
        if (preview) { if (previewWorld != null) previewWorld.UnregisterVisual(this); }
        else EffectBlobWorld.Unregister(this);
        unchecked { generation++; }
        released = true;
        playback.Reset();
        fire?.Reset();
        details?.Reset();
        beamVisual?.Clear();
        attachedRenderer = null;
        attachedGroup = null;
        attachedAnchor = null;
        appliedSorting = default;
        sortingGroundPosition = Vector3.zero;
        if (detailRenderers != null)
            foreach (SpriteRenderer renderer in detailRenderers) renderer.enabled = false;
        PhaseStarted = null;
        Finished = null;
        definition = null;
        frames = smokeFrames = null;
        elements.Clear();
        Faction = EffectFaction.Neutral;
        airborne = false;
        anchored = false;
        anchorPosition = Vector3.zero;
        flightElapsed = flightDuration = age = requestedDuration = 0f;
        variationSeed = 0f;
        sizeMultiplier = 1f;
        Array.Clear(pieceSizes, 0, pieceSizes.Length);
        velocity = Vector3.zero;
        frameRadius = 0f;
        withinCamera = false;
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

    private void EnsureDetailRenderers()
    {
        if (detailRenderers != null) return;
        const int resolution = 16;
        detailTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            name = "Elemental Detail", hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
        };
        Color32[] pixels = new Color32[resolution * resolution];
        for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                Vector2 point = new((x + 0.5f) / resolution * 2f - 1f, (y + 0.5f) / resolution * 2f - 1f);
                pixels[y * resolution + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - point.sqrMagnitude) * 2f));
            }
        detailTexture.SetPixels32(pixels);
        detailTexture.Apply(false, true);
        detailSprite = Sprite.Create(detailTexture, new Rect(0, 0, resolution, resolution),
            new Vector2(0.5f, 0.5f), resolution, 0, SpriteMeshType.FullRect);
        detailSprite.hideFlags = HideFlags.HideAndDontSave;
        pixelDetailSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1),
            new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
        pixelDetailSprite.hideFlags = HideFlags.HideAndDontSave;
        detailRenderers = new SpriteRenderer[EffectDetailParticles.MaxParticles];
        for (int i = 0; i < detailRenderers.Length; i++)
        {
            GameObject host = new("Detail " + i) { hideFlags = gameObject.hideFlags };
            host.transform.SetParent(transform, false);
            SpriteRenderer renderer = host.AddComponent<SpriteRenderer>();
            renderer.sprite = detailSprite;
            renderer.enabled = false;
            detailRenderers[i] = renderer;
        }
    }

    private void RenderDetails(bool visible)
    {
        if (detailRenderers == null || details == null) return;
        if (!visible)
        {
            foreach (SpriteRenderer renderer in detailRenderers) renderer.enabled = false;
            return;
        }
        Vector3 scale = transform.lossyScale;
        for (int i = 0; i < detailRenderers.Length; i++)
        {
            SpriteRenderer renderer = detailRenderers[i];
            bool active = details.Capture(i, out Vector3 position, out Vector2 size, out Color color);
            renderer.enabled = active;
            if (!renderer.enabled) continue;
            renderer.transform.position = position;
            renderer.transform.rotation = Quaternion.identity;
            renderer.transform.localScale = new Vector3(size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)), 1f);
            renderer.color = color;
        }
    }

    private void OnDestroy()
    {
        beamVisual?.Dispose();
        ReleaseDetailResources();
    }

    public void SetAttachedSorting(SpriteRenderer target, SortingGroup group = null, BattleWorldSortAnchor anchor = null)
    {
        attachedRenderer = target;
        attachedGroup = group;
        attachedAnchor = anchor;
        ApplySorting();
    }

    private void ApplySorting()
    {
        if (definition == null || renderers == null) return;
        EffectRenderSort sorting = RenderSorting;
        if (appliedSorting.Equals(sorting)) return;
        if (definition.mergeNearby || definition.fireParticles)
        {
            if (preview) { if (previewWorld != null) previewWorld.RequestRefresh(); }
            else EffectBlobWorld.RequestSortingRefresh();
        }
        appliedSorting = sorting;
        for (int i = 0; i < renderers.Length; i++) sorting.Apply(renderers[i], -i);
        if (detailRenderers != null)
            foreach (SpriteRenderer renderer in detailRenderers) sorting.Apply(renderer, 1);
    }

    public void DisposePreview()
    {
        if (!preview) return;
        ResetForPool();
        ReleaseDetailResources();
    }

    private void ReleaseDetailResources()
    {
        if (Application.isPlaying)
        {
            if (detailSprite != null) Destroy(detailSprite);
            if (pixelDetailSprite != null) Destroy(pixelDetailSprite);
            if (detailTexture != null) Destroy(detailTexture);
        }
        else
        {
            if (detailSprite != null) DestroyImmediate(detailSprite);
            if (pixelDetailSprite != null) DestroyImmediate(pixelDetailSprite);
            if (detailTexture != null) DestroyImmediate(detailTexture);
        }
        detailSprite = null;
        pixelDetailSprite = null;
        detailTexture = null;
    }
}
