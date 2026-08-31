using System;
using System.Collections.Generic;
using UnityEngine;

public enum SynergyDummyVisualType
{
    RingBurst,
    Flash,
    Slash,
    Pulse
}

[Serializable]
public class SynergyTagRequirement
{
    public EquipmentTag tag;
    [Min(1)] public int count = 1;
}

[Serializable]
public class SynergyRule
{
    [Header("Identity")]
    public string synergyId;
    public string displayName;
    [TextArea] public string description;

    [Header("Requirements")]
    public List<SynergyTagRequirement> requirements = new();

    [Header("Resolved Modifiers - consumer systems read these")]
    [Min(0f)] public float damageMultiplier = 1f;
    [Min(0f)] public float moveSpeedMultiplier = 1f;
    [Min(0f)] public float breakPowerMultiplier = 1f;
    [Min(0f)] public float explosionRadiusMultiplier = 1f;

    [Header("Dummy Visual")]
    public SynergyDummyVisualType dummyVisual = SynergyDummyVisualType.RingBurst;
    public Color dummyColor = Color.white;
    [Min(0.05f)] public float dummyDuration = 0.55f;
    [Min(0.05f)] public float dummyScale = 1.25f;
}

/// <summary>
/// Synergy별 실제 Sprite 이펙트를 Inspector에서 연결하는 교체 지점입니다.
/// frames가 null/empty이면 SynergyRule의 코드 기반 Dummy Visual이 자동 사용됩니다.
/// </summary>
[Serializable]
public class SynergyVisualOverride
{
    public string synergyId;
    public Sprite[] frames;
    [Min(1f)] public float fps = 12f;
    [Min(0.05f)] public float worldScale = 1f;
    public Material material;
    public int sortingOrder = 80;
    public bool rotateToDirection = true;
}

/// <summary>
/// BattleEquipment의 Tag 조합을 읽어 활성 Synergy를 판정하는 메인 Resolver입니다.
///
/// - InventoryChanged 시 자동 재계산
/// - 새 Synergy 활성화 시 activation VFX 자동 재생
/// - visualOverrides에 Sprite[]를 넣으면 실제 Sprite Animation 사용
/// - Sprite가 비어 있으면 코드 생성 Pixel Dummy VFX 사용
/// - 전투 로직은 TriggerSynergyEffect(id, position, direction)를 호출하면 같은 VFX 경로를 사용
///
/// 실제 밸런스 효과는 ActiveDamageMultiplier 등의 집계값을 소비자 시스템이 읽는 방식으로 분리합니다.
/// </summary>
public class SynergyManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private Transform effectRoot;
    [SerializeField] private Transform defaultEffectOrigin;

    [Header("Rules")]
    [Tooltip("비어 있으면 테스트용 기본 Rule이 런타임에 자동 설치됩니다.")]
    [SerializeField] private bool installDefaultRulesWhenEmpty = true;
    [SerializeField] private List<SynergyRule> rules = new();

    [Header("Sprite Visual Overrides")]
    [Tooltip("여기에 직접 만든 이펙트 Sprite 프레임을 연결하세요. frames가 비어 있으면 Dummy VFX가 나옵니다.")]
    [SerializeField] private List<SynergyVisualOverride> visualOverrides = new();

    [Header("Activation Presentation")]
    [SerializeField] private bool playEffectWhenActivated = true;

    private readonly List<SynergyRule> activeSynergies = new();
    private readonly HashSet<string> activeIds = new(StringComparer.Ordinal);

    public IReadOnlyList<SynergyRule> Rules => rules;
    public IReadOnlyList<SynergyRule> ActiveSynergies => activeSynergies;

    public float ActiveDamageMultiplier { get; private set; } = 1f;
    public float ActiveMoveSpeedMultiplier { get; private set; } = 1f;
    public float ActiveBreakPowerMultiplier { get; private set; } = 1f;
    public float ActiveExplosionRadiusMultiplier { get; private set; } = 1f;

    public event Action SynergiesChanged;
    public event Action<SynergyRule> SynergyActivated;
    public event Action<SynergyRule> SynergyDeactivated;
    public event Action<SynergyRule, Vector3> SynergyEffectPlayed;

    private void Awake()
    {
        AutoFindReferences();
        EnsureDefaultRules();
    }

    private void OnEnable()
    {
        if (equipmentSystem != null)
            equipmentSystem.InventoryChanged += ResolveSynergies;

        ResolveSynergies();
    }

    private void OnDisable()
    {
        if (equipmentSystem != null)
            equipmentSystem.InventoryChanged -= ResolveSynergies;
    }

    [ContextMenu("Auto Find Synergy References")]
    public void AutoFindReferences()
    {
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();

        if (defaultEffectOrigin == null)
        {
            PlayerController player = FindFirstObjectByType<PlayerController>();
            if (player != null)
                defaultEffectOrigin = player.transform;
        }

        if (effectRoot == null)
            effectRoot = transform;
    }

    public bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();

        if (equipmentSystem == null)
            errors.Add("equipmentSystem is null");

        if (rules == null || rules.Count == 0)
            errors.Add("No SynergyRule is available.");

        HashSet<string> ids = new(StringComparer.Ordinal);
        if (rules != null)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                SynergyRule rule = rules[i];
                if (rule == null)
                {
                    errors.Add($"rules[{i}] is null");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rule.synergyId))
                {
                    errors.Add($"rules[{i}] has empty synergyId");
                    continue;
                }

                if (!ids.Add(rule.synergyId))
                    errors.Add($"Duplicate synergyId: {rule.synergyId}");

                if (rule.requirements == null || rule.requirements.Count == 0)
                    errors.Add($"Synergy '{rule.synergyId}' has no requirements.");
            }
        }

        report = string.Join("\n", errors);
        return errors.Count == 0;
    }

    [ContextMenu("Resolve Synergies")]
    public void ResolveSynergies()
    {
        EnsureDefaultRules();

        HashSet<string> previous = new(activeIds, StringComparer.Ordinal);
        activeIds.Clear();
        activeSynergies.Clear();

        ActiveDamageMultiplier = 1f;
        ActiveMoveSpeedMultiplier = 1f;
        ActiveBreakPowerMultiplier = 1f;
        ActiveExplosionRadiusMultiplier = 1f;

        if (equipmentSystem != null && rules != null)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                SynergyRule rule = rules[i];
                if (rule == null || string.IsNullOrWhiteSpace(rule.synergyId))
                    continue;

                if (!MeetsRequirements(rule))
                    continue;

                activeIds.Add(rule.synergyId);
                activeSynergies.Add(rule);

                ActiveDamageMultiplier *= Mathf.Max(0f, rule.damageMultiplier);
                ActiveMoveSpeedMultiplier *= Mathf.Max(0f, rule.moveSpeedMultiplier);
                ActiveBreakPowerMultiplier *= Mathf.Max(0f, rule.breakPowerMultiplier);
                ActiveExplosionRadiusMultiplier *= Mathf.Max(0f, rule.explosionRadiusMultiplier);
            }
        }

        for (int i = 0; i < activeSynergies.Count; i++)
        {
            SynergyRule rule = activeSynergies[i];
            if (previous.Remove(rule.synergyId))
                continue;

            SynergyActivated?.Invoke(rule);

            if (playEffectWhenActivated)
            {
                Vector3 position = defaultEffectOrigin != null
                    ? defaultEffectOrigin.position
                    : transform.position;
                TriggerSynergyEffect(rule.synergyId, position, Vector2.up);
            }
        }

        if (previous.Count > 0 && rules != null)
        {
            foreach (string removedId in previous)
            {
                SynergyRule removed = FindRule(removedId);
                if (removed != null)
                    SynergyDeactivated?.Invoke(removed);
            }
        }

        SynergiesChanged?.Invoke();
    }

    public bool IsActive(string synergyId)
    {
        return !string.IsNullOrWhiteSpace(synergyId) && activeIds.Contains(synergyId);
    }

    public SynergyRule FindRule(string synergyId)
    {
        if (string.IsNullOrWhiteSpace(synergyId) || rules == null)
            return null;

        for (int i = 0; i < rules.Count; i++)
        {
            if (rules[i] != null && string.Equals(rules[i].synergyId, synergyId, StringComparison.Ordinal))
                return rules[i];
        }

        return null;
    }

    /// <summary>
    /// 향후 Break/Explosion/Finisher 등 실제 전투 이벤트에서 호출하는 공통 VFX 진입점입니다.
    /// 현재는 활성 여부와 무관하게 명시적으로 Preview/Trigger할 수 있습니다.
    /// </summary>
    public void TriggerSynergyEffect(string synergyId, Vector3 position, Vector2 direction)
    {
        SynergyRule rule = FindRule(synergyId);
        if (rule == null)
        {
            Debug.LogWarning($"[Synergy] Unknown synergy id: {synergyId}");
            return;
        }

        SynergyVisualOverride visual = FindVisualOverride(synergyId);
        bool hasRealSprites = visual != null && visual.frames != null && visual.frames.Length > 0;

        GameObject go = new($"SynergyVFX_{synergyId}");
        if (effectRoot != null)
            go.transform.SetParent(effectRoot, true);
        go.transform.position = position;

        SynergyEffectInstance instance = go.AddComponent<SynergyEffectInstance>();

        if (hasRealSprites)
        {
            instance.PlaySpriteAnimation(
                visual.frames,
                visual.fps,
                visual.worldScale,
                visual.material,
                visual.sortingOrder,
                visual.rotateToDirection,
                direction);
        }
        else
        {
            instance.PlayDummy(
                rule.dummyVisual,
                rule.dummyColor,
                rule.dummyDuration,
                rule.dummyScale,
                direction);
        }

        SynergyEffectPlayed?.Invoke(rule, position);
    }

    public void PreviewSynergy(string synergyId)
    {
        Vector3 position = defaultEffectOrigin != null
            ? defaultEffectOrigin.position
            : transform.position;

        TriggerSynergyEffect(synergyId, position, Vector2.up);
    }

    public void PreviewAllActive()
    {
        if (activeSynergies.Count == 0)
            return;

        Vector3 origin = defaultEffectOrigin != null
            ? defaultEffectOrigin.position
            : transform.position;

        for (int i = 0; i < activeSynergies.Count; i++)
        {
            float angle = activeSynergies.Count <= 1
                ? 90f
                : (360f / activeSynergies.Count) * i;
            float rad = angle * Mathf.Deg2Rad;
            Vector2 direction = new(Mathf.Cos(rad), Mathf.Sin(rad));
            Vector3 position = origin + (Vector3)(direction * 0.8f);
            TriggerSynergyEffect(activeSynergies[i].synergyId, position, direction);
        }
    }

    private bool MeetsRequirements(SynergyRule rule)
    {
        if (rule.requirements == null || rule.requirements.Count == 0)
            return false;

        for (int i = 0; i < rule.requirements.Count; i++)
        {
            SynergyTagRequirement requirement = rule.requirements[i];
            if (requirement == null)
                return false;

            int current = equipmentSystem.CountTag(requirement.tag);
            if (current < Mathf.Max(1, requirement.count))
                return false;
        }

        return true;
    }

    private SynergyVisualOverride FindVisualOverride(string synergyId)
    {
        if (visualOverrides == null)
            return null;

        for (int i = 0; i < visualOverrides.Count; i++)
        {
            SynergyVisualOverride visual = visualOverrides[i];
            if (visual != null && string.Equals(visual.synergyId, synergyId, StringComparison.Ordinal))
                return visual;
        }

        return null;
    }

    private void EnsureDefaultRules()
    {
        if (!installDefaultRulesWhenEmpty)
            return;

        rules ??= new List<SynergyRule>();
        if (rules.Count > 0)
            return;

        rules.Add(CreateRule(
            "BREAK_TWO",
            "BREAK x2",
            "Break 계열 장비 2개. 향후 Break Gauge 위력 보너스용 기본 시너지.",
            SynergyDummyVisualType.RingBurst,
            new Color(0.35f, 0.85f, 1f, 1f),
            1f, 1f, 1.20f, 1f,
            (EquipmentTag.Break, 2)));

        rules.Add(CreateRule(
            "EXPLOSION_TWO",
            "EXPLOSION x2",
            "Explosion 계열 장비 2개. 향후 폭발 범위 확장용 기본 시너지.",
            SynergyDummyVisualType.Flash,
            new Color(1f, 0.55f, 0.15f, 1f),
            1f, 1f, 1f, 1.15f,
            (EquipmentTag.Explosion, 2)));

        rules.Add(CreateRule(
            "BREAKING_NEWS",
            "BREAKING NEWS",
            "Break + Explosion 조합. 테스트용 복합 시너지.",
            SynergyDummyVisualType.Flash,
            new Color(1f, 0.9f, 0.25f, 1f),
            1.10f, 1f, 1.10f, 1.10f,
            (EquipmentTag.Break, 1),
            (EquipmentTag.Explosion, 1)));

        rules.Add(CreateRule(
            "BULLET_SCRIPT",
            "BULLET SCRIPT",
            "Projectile 2 + Precision 1 조합. 정밀 사격 계열 테스트 시너지.",
            SynergyDummyVisualType.Slash,
            new Color(0.8f, 0.9f, 1f, 1f),
            1.08f, 1f, 1f, 1f,
            (EquipmentTag.Projectile, 2),
            (EquipmentTag.Precision, 1)));

        rules.Add(CreateRule(
            "RUSH_CUT",
            "RUSH CUT",
            "Melee + Dash 조합. 돌진 근접 계열 테스트 시너지.",
            SynergyDummyVisualType.Slash,
            new Color(1f, 0.35f, 0.55f, 1f),
            1.05f, 1.10f, 1f, 1f,
            (EquipmentTag.Melee, 1),
            (EquipmentTag.Dash, 1)));
    }

    private static SynergyRule CreateRule(
        string id,
        string displayName,
        string description,
        SynergyDummyVisualType dummyVisual,
        Color dummyColor,
        float damage,
        float move,
        float breakPower,
        float explosionRadius,
        params (EquipmentTag tag, int count)[] requirements)
    {
        SynergyRule rule = new()
        {
            synergyId = id,
            displayName = displayName,
            description = description,
            dummyVisual = dummyVisual,
            dummyColor = dummyColor,
            damageMultiplier = damage,
            moveSpeedMultiplier = move,
            breakPowerMultiplier = breakPower,
            explosionRadiusMultiplier = explosionRadius
        };

        for (int i = 0; i < requirements.Length; i++)
        {
            rule.requirements.Add(new SynergyTagRequirement
            {
                tag = requirements[i].tag,
                count = Mathf.Max(1, requirements[i].count)
            });
        }

        return rule;
    }
}

/// <summary>
/// SynergyManager가 런타임에 생성하는 일회성 VFX Player.
/// 실제 Sprite 프레임이 있으면 Sprite Animation, 없으면 코드 생성 Dummy Sprite를 재생합니다.
/// 별도 Prefab이 필요 없습니다.
/// </summary>
internal sealed class SynergyEffectInstance : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Sprite[] frames;
    private float fps;
    private float elapsed;
    private float duration;
    private bool spriteMode;
    private bool rotateDummy;
    private SynergyDummyVisualType dummyType;
    private Vector3 startScale;
    private Vector2 direction;

    public void PlaySpriteAnimation(
        Sprite[] sprites,
        float animationFps,
        float worldScale,
        Material material,
        int sortingOrder,
        bool rotateToDirection,
        Vector2 effectDirection)
    {
        spriteMode = true;
        frames = sprites;
        fps = Mathf.Max(1f, animationFps);
        duration = Mathf.Max(0.05f, frames.Length / fps);
        direction = effectDirection.sqrMagnitude > 0.001f ? effectDirection.normalized : Vector2.right;

        CreateRenderer();
        spriteRenderer.sprite = frames[0];
        spriteRenderer.sortingOrder = sortingOrder;

        if (material != null)
            spriteRenderer.sharedMaterial = material;

        transform.localScale = Vector3.one * Mathf.Max(0.05f, worldScale);

        if (rotateToDirection)
            transform.rotation = DirectionToRotation(direction);
    }

    public void PlayDummy(
        SynergyDummyVisualType visualType,
        Color color,
        float effectDuration,
        float effectScale,
        Vector2 effectDirection)
    {
        spriteMode = false;
        dummyType = visualType;
        duration = Mathf.Max(0.05f, effectDuration);
        direction = effectDirection.sqrMagnitude > 0.001f ? effectDirection.normalized : Vector2.right;
        rotateDummy = visualType == SynergyDummyVisualType.Slash;

        CreateRenderer();
        spriteRenderer.sprite = SynergyDummySpriteCache.Get(visualType);
        spriteRenderer.color = color;
        spriteRenderer.sortingOrder = 80;

        startScale = Vector3.one * Mathf.Max(0.05f, effectScale);
        transform.localScale = startScale * 0.25f;

        if (rotateDummy)
            transform.rotation = DirectionToRotation(direction);
    }

    private void CreateRenderer()
    {
        spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
    }

    private void Update()
    {
        elapsed += Time.unscaledDeltaTime;

        if (spriteMode)
            UpdateSpriteAnimation();
        else
            UpdateDummyAnimation();

        if (elapsed >= duration)
            Destroy(gameObject);
    }

    private void UpdateSpriteAnimation()
    {
        if (frames == null || frames.Length == 0 || spriteRenderer == null)
            return;

        int index = Mathf.Min(frames.Length - 1, Mathf.FloorToInt(elapsed * fps));
        spriteRenderer.sprite = frames[index];
    }

    private void UpdateDummyAnimation()
    {
        if (spriteRenderer == null)
            return;

        float t = Mathf.Clamp01(elapsed / duration);
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        Color color = spriteRenderer.color;
        color.a = Mathf.Lerp(1f, 0f, t * t);
        spriteRenderer.color = color;

        switch (dummyType)
        {
            case SynergyDummyVisualType.RingBurst:
                transform.localScale = Vector3.Lerp(startScale * 0.25f, startScale * 1.6f, eased);
                transform.Rotate(0f, 0f, 180f * Time.unscaledDeltaTime);
                break;

            case SynergyDummyVisualType.Flash:
                float pulse = 1f + Mathf.Sin(t * Mathf.PI) * 0.55f;
                transform.localScale = startScale * Mathf.Lerp(0.35f, 1.35f, eased) * pulse;
                transform.Rotate(0f, 0f, 320f * Time.unscaledDeltaTime);
                break;

            case SynergyDummyVisualType.Slash:
                transform.localScale = new Vector3(
                    Mathf.Lerp(startScale.x * 0.25f, startScale.x * 1.7f, eased),
                    Mathf.Lerp(startScale.y * 0.45f, startScale.y * 0.9f, eased),
                    1f);
                transform.position += (Vector3)(direction * 1.5f * Time.unscaledDeltaTime);
                break;

            case SynergyDummyVisualType.Pulse:
                transform.localScale = startScale * (0.65f + Mathf.Sin(t * Mathf.PI) * 0.75f);
                break;
        }
    }

    private static Quaternion DirectionToRotation(Vector2 dir)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        return Quaternion.Euler(0f, 0f, angle);
    }
}

internal static class SynergyDummySpriteCache
{
    private static Sprite ring;
    private static Sprite flash;
    private static Sprite slash;
    private static Sprite pulse;

    public static Sprite Get(SynergyDummyVisualType type)
    {
        return type switch
        {
            SynergyDummyVisualType.RingBurst => ring ??= CreateRing(),
            SynergyDummyVisualType.Flash => flash ??= CreateFlash(),
            SynergyDummyVisualType.Slash => slash ??= CreateSlash(),
            SynergyDummyVisualType.Pulse => pulse ??= CreatePulse(),
            _ => ring ??= CreateRing()
        };
    }

    private static Sprite CreateRing()
    {
        const int size = 64;
        Texture2D texture = CreateTexture(size);
        Color[] pixels = new Color[size * size];
        Vector2 center = new((size - 1) * 0.5f, (size - 1) * 0.5f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                bool visible = distance >= 20f && distance <= 26f;
                pixels[y * size + x] = visible ? Color.white : Color.clear;
            }
        }

        return Finish(texture, pixels, size, "DummySynergyRing");
    }

    private static Sprite CreateFlash()
    {
        const int size = 64;
        Texture2D texture = CreateTexture(size);
        Color[] pixels = new Color[size * size];
        float center = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - center);
                float dy = Mathf.Abs(y - center);
                bool cross = (dx <= 2f && dy <= 27f) || (dy <= 2f && dx <= 27f);
                bool diagonal = Mathf.Abs(dx - dy) <= 1.5f && dx <= 18f;
                bool visible = cross || diagonal;
                pixels[y * size + x] = visible ? Color.white : Color.clear;
            }
        }

        return Finish(texture, pixels, size, "DummySynergyFlash");
    }

    private static Sprite CreateSlash()
    {
        const int width = 96;
        const int height = 32;
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.hideFlags = HideFlags.DontSave;

        Color[] pixels = new Color[width * height];
        float cy = (height - 1) * 0.5f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float normalized = x / (float)(width - 1);
                float wave = Mathf.Sin(normalized * Mathf.PI) * 7f;
                float targetY = cy + wave * 0.35f;
                float thickness = Mathf.Lerp(1f, 3f, Mathf.Sin(normalized * Mathf.PI));
                bool visible = Mathf.Abs(y - targetY) <= thickness;
                pixels[y * width + x] = visible ? Color.white : Color.clear;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 32f);
        sprite.name = "DummySynergySlash";
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    private static Sprite CreatePulse()
    {
        const int size = 64;
        Texture2D texture = CreateTexture(size);
        Color[] pixels = new Color[size * size];
        Vector2 center = new((size - 1) * 0.5f, (size - 1) * 0.5f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01(1f - distance / 28f);
                alpha = alpha > 0.15f ? alpha : 0f;
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        return Finish(texture, pixels, size, "DummySynergyPulse");
    }

    private static Texture2D CreateTexture(int size)
    {
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.hideFlags = HideFlags.DontSave;
        return texture;
    }

    private static Sprite Finish(Texture2D texture, Color[] pixels, int size, string name)
    {
        texture.SetPixels(pixels);
        texture.Apply();
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 32f);
        sprite.name = name;
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }
}
