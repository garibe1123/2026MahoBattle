using System;
using System.Collections.Generic;
using UnityEngine;

public enum EffectPhase { None, Intro, Idle, End, Finished }
public enum EffectFamily { Gas, Liquid }
public enum EffectUsage { Standalone, AttachToProjectile, SpawnOnImpact, ModifyProjectile }
public enum EffectIdleEnd { LoopBoundary, Immediate }
public enum EffectShortLife { AccelerateIntroAndEnd, EndOnly }
public enum EffectIntroStop { Immediate, AfterIntro }
public enum EffectLightMode { Steady, Flicker }

[Serializable]
public sealed class EffectClipDefinition
{
    [Tooltip("켜면 시트를 잘라 쓰고, 끄면 Sprites 목록을 사용합니다.")]
    public bool useSheet = true;
    [Tooltip("시작 프레임 번호입니다. 0부터 세며 시트는 왼쪽→오른쪽, 위→아래 순서입니다.")]
    [Min(0)] public int startIndex;
    [Tooltip("시작 번호부터 사용할 프레임 수입니다. 0이면 해당 단계의 프레임을 사용하지 않습니다.")]
    [Min(0)] public int frameCount = 4;
    [Tooltip("초당 재생할 프레임 수입니다. 클수록 애니메이션이 빨라집니다.")]
    [Min(0.01f)] public float fps = 12f;
    [Tooltip("Use Sheet를 끈 경우 사용할 프레임 목록입니다.")]
    public Sprite[] sprites = Array.Empty<Sprite>();
    public float SafeFps => EffectNumbers.Positive(fps, 12f);
}

[CreateAssetMenu(menuName = "Game/Effect/Elemental Visual")]
public sealed class EffectDefinitionSO : ScriptableObject
{
    public const float DefaultSizeVariation = 0.2f;
    [Tooltip("효과의 기본 원소 정의입니다.")]
    public ElementDefinitionSO element;
    [Tooltip("기체(Gas)·액체(Liquid) 계열입니다. 이동·표면 표현 경로를 구분합니다.")]
    public EffectFamily family;
    [Tooltip("독립 효과·투사체 부착·충돌 생성 등 효과의 용도입니다.")]
    public EffectUsage usage;
    [Tooltip("기본 원소에 더해 효과에 포함할 원소 목록입니다.")]
    public ElementDefinitionSO[] additionalElements = Array.Empty<ElementDefinitionSO>();
    [Header("Sheet: indices run left to right, top to bottom")]
    [Tooltip("프레임을 잘라 쓸 원본 스프라이트 시트입니다.")]
    public Sprite sourceSheet;
    [Tooltip("시트 한 칸의 가로·세로 픽셀 수입니다.")]
    public Vector2Int cellSize = new(32, 32);
    [Tooltip("시트의 가로 칸 수와 세로 칸 수입니다.")]
    public Vector2Int layout = new(4, 4);
    [Tooltip("생성 직후 재생하는 시작 애니메이션입니다.")]
    public EffectClipDefinition intro = new() { startIndex = 0, frameCount = 4 };
    [Tooltip("시작 이후 유지 중 반복하는 애니메이션입니다.")]
    public EffectClipDefinition idle = new() { startIndex = 4, frameCount = 8 };
    [Tooltip("종료할 때 재생하는 애니메이션입니다.")]
    public EffectClipDefinition end = new() { startIndex = 12, frameCount = 4 };
    [Header("Lifetime includes Intro and End; -1 waits for Stop")]
    [Tooltip("시작·유지·종료를 포함한 요청 수명(초)입니다. -1이면 정지 요청을 기다립니다.")]
    public float duration = 2f;
    [Tooltip("유지 종료 방식입니다. Loop Boundary는 반복 경계까지, Immediate는 시간에 맞춰 끝냅니다.")]
    public EffectIdleEnd idleEnd;
    [Tooltip("수명이 시작·종료보다 짧을 때 둘을 압축하거나 종료만 재생합니다.")]
    public EffectShortLife shortLife;
    [Tooltip("시작 중 정지 요청을 받으면 즉시 종료로 넘어갈지, 시작을 마칠지 정합니다.")]
    public EffectIntroStop introStop;
    [Header("Material properties are applied only if supported")]
    [Tooltip("사용할 머티리얼입니다. 비워 두면 기본 머티리얼을 사용합니다.")]
    public Material customMaterial;
    [Tooltip("효과 전체에 곱할 기본 색과 알파입니다.")]
    public Color tint = Color.white;
    [Tooltip("시작 단계의 RGB 밝기 배율입니다.")]
    public float introBrightness = 1.15f;
    [Tooltip("유지 단계의 RGB 밝기 배율입니다.")]
    public float idleBrightness = 1f;
    [Tooltip("종료 단계의 RGB 밝기 배율입니다.")]
    public float endBrightness = 0.65f;
    [Tooltip("켜면 종료 단계가 진행될수록 투명하게 만듭니다.")]
    public bool fadeOnEnd;
    [Header("본체 수명 곡선")]
    [Tooltip("펼친 뒤 Enabled를 켜고 곡선을 클릭하면 키를 드래그해 색상 진행·투명도·위치를 조절할 수 있습니다.")]
    public EffectVisualCurves visualCurves = new();
    [Header("Procedural fire: pooled round particles share one surface")]
    [Tooltip("켜면 불꽃 조각을 재사용하며 하나의 불 표면으로 합쳐 그립니다.")]
    public bool fireParticles;
    [Tooltip("불 발생 지점의 최소·최대 개수입니다. 실제 범위는 2~3개입니다.")]
    public Vector2Int fireSourceCount = new(2, 3);
    [Tooltip("발생 지점별 기준 크기의 최소·최대 배율입니다.")]
    public Vector2 fireSourceScale = new(0.7f, 1.25f);
    [Tooltip("불 발생 지점이 중심에서 퍼지는 거리입니다. 전체 크기 배율이 적용됩니다.")]
    [Min(0f)] public float fireSourceSpread = 0.35f;
    [Tooltip("동시에 유지할 불꽃 조각 수의 상한입니다. 최대 96개입니다.")]
    [Range(8, 96)] public int fireParticleCapacity = 48;
    [Tooltip("불꽃 조각의 초당 생성 빈도입니다. 0이면 초기 조각 이후 생성하지 않습니다.")]
    [Min(0f)] public float fireEmissionRate = 26f;
    [Tooltip("불꽃 생성 간격의 배율입니다. 클수록 드문드문 생성됩니다.")]
    [Min(1f)] public float fireEmissionIntervalScale = 1.6f;
    [Tooltip("불꽃 조각 수명의 최소·최대(초)입니다.")]
    public Vector2 fireParticleLifetime = new(0.8f, 1.3f);
    [Tooltip("불꽃 조각의 기준 반지름 최소·최대입니다. 발생 지점과 생성 크기 배율을 곱합니다.")]
    public Vector2 fireParticleRadius = new(0.12f, 0.24f);
    [Tooltip("불꽃 조각의 가로·세로 배율입니다. 전체 크기는 Visual Scale로 조절합니다.")]
    public Vector2 fireShapeScale = new(1f, 1.35f);
    [Tooltip("불꽃의 좌우 이동과 흔들림에 사용할 퍼짐 정도입니다.")]
    [Min(0f)] public float fireSpread = 0.45f;
    [Tooltip("불꽃 수명 중 알파 감소를 시작할 비율입니다. Flame Duration보다 먼저 줄어들지는 않습니다.")]
    [Range(0f, 0.9f)] public float fireFadeStart = 0.5f;
    [Tooltip("불꽃 수명 중 밝은 불 상태를 유지할 비율입니다. 이후 연기로 전환됩니다.")]
    [Range(0.35f, 0.9f)] public float fireFlameDuration = 0.68f;
    [Tooltip("불꽃이 간헐적으로 길어지고 높이 솟는 강도입니다. 0이면 추가 솟구침을 끕니다.")]
    [Range(0f, 2f)] public float fireSurgeStrength = 0.9f;
    [Tooltip("불꽃 솟구침과 기류 변화의 속도입니다. 클수록 빨리 변합니다.")]
    [Min(0.01f)] public float fireSurgeFrequency = 1.2f;
    [Tooltip("연기로 전환된 조각에 더할 좌우 퍼짐 정도입니다.")]
    [Min(0f)] public float fireSmokeSpread = 1.6f;
    [Tooltip("연기로 전환될 때 더할 팽창 배율입니다.")]
    [Min(0f)] public float fireSmokeExpansion = 0.65f;
    [Tooltip("연기로 전환된 조각의 알파 배율입니다. 작을수록 옅습니다.")]
    [Range(0f, 1f)] public float fireSmokeOpacity = 0.14f;
    [Tooltip("연기가 좌우와 표면 가장자리에서 사라지는 강도입니다. 클수록 급격히 옅어집니다.")]
    [Min(0f)] public float fireSmokeEdgeFade = 3f;
    [Tooltip("불꽃 조각의 외곽선을 불규칙하게 만드는 정도입니다.")]
    [Range(0f, 0.4f)] public float fireContourVariation = 0.22f;
    [Tooltip("불꽃 끝을 뾰족하게 만드는 정도입니다.")]
    [Range(0f, 0.9f)] public float fireTipSharpness = 0.7f;
    [Tooltip("불꽃·연기 조각 크기가 흔들리는 정도입니다.")]
    [Range(0f, 0.6f)] public float fireTurbulence = 0.3f;
    [Tooltip("불꽃 안쪽에 움직이는 빈틈을 만드는 강도입니다.")]
    [Range(0f, 1f)] public float fireTearStrength = 1f;
    [Tooltip("불꽃 외곽과 내부 빈틈 둘레의 어두운 테두리 폭입니다.")]
    [Range(0.01f, 0.6f)] public float fireEdgeWidth = 0.28f;
    [Tooltip("불꽃의 어두운 테두리를 적용할 강도입니다.")]
    [Range(0f, 1f)] public float fireEdgeStrength = 0.85f;
    [Tooltip("갓 생성된 가장 밝은 불꽃의 색이며 불 표면 팔레트에도 사용됩니다.")]
    public Color fireHotColor = new(1f, 0.9f, 0.3f);
    [Tooltip("불 표면 팔레트의 금빛 색입니다.")]
    public Color fireGoldColor = new(1f, 0.64f, 0.14f);
    [Tooltip("수명이 진행된 따뜻한 불꽃의 색이며 불 표면 팔레트에도 사용됩니다.")]
    public Color fireWarmColor = new(0.95f, 0.27f, 0.045f);
    [Tooltip("불 표면 팔레트의 어두운 불빛 색입니다. 별도 불씨 색은 Ember Start/End Color입니다.")]
    public Color fireEmberColor = new(0.5f, 0.17f, 0.1f);
    [Tooltip("불 표면 팔레트에 포함할 연기 색입니다.")]
    public Color fireSmokeColor = new(0.22f, 0.13f, 0.16f);
    [Tooltip("불꽃이 연기로 바뀔 때의 최종 색과 어두운 테두리 색입니다.")]
    public Color fireSootColor = new(0.065f, 0.055f, 0.075f);
    [Header("Additional pooled details")]
    [Tooltip("별도 1픽셀 불씨가 생성될 때의 밝은 색입니다.")]
    public Color emberStartColor = new Color32(255, 242, 176, 255);
    [Tooltip("별도 1픽셀 불씨가 수명 끝에 도달할 때의 어두운 색입니다.")]
    public Color emberEndColor = new Color32(128, 48, 26, 255);
    [Tooltip("별도 불씨의 개수·수명·기류 및 수명 곡선 설정입니다.")]
    public EffectDetailSettings embers = new();
    [Tooltip("Embers 수명 범위에 곱할 배율입니다. 클수록 불씨가 오래 남습니다.")]
    [Min(0.1f)] public float emberLifetimeScale = 2.5f;
    [Tooltip("불씨의 초기 속도와 좌우 기류 속도 배율입니다. 클수록 멀리 날아갑니다.")]
    [Min(0.1f)] public float emberTravelScale = 1.8f;
    [Tooltip("불씨 수명 중 알파 감소를 시작할 비율입니다. 0.6이면 수명의 60%까지 알파를 유지합니다.")]
    [Range(0f, 0.9f)] public float emberFadeStart = 0.6f;
    [Tooltip("작은 비산 물방울의 생성·이동 설정입니다.")]
    public EffectDetailSettings waterDetails = new()
    {
        lifetime = new Vector2(0.3f, 0.65f), radius = new Vector2(0.025f, 0.05f),
        spread = 1.2f, rise = 1.2f, expansion = 0f
    };
    [Tooltip("주변으로 퍼지는 구름 형태 안개 조각의 생성·이동 설정입니다.")]
    public EffectDetailSettings gasDetails = new()
    {
        lifetime = new Vector2(0.8f, 1.6f), radius = new Vector2(0.12f, 0.2f),
        spread = 0.4f, rise = 0.3f, expansion = 1f
    };
    [Tooltip("안개 조각 생성 간격의 하한(초)입니다. 클수록 드물게 나옵니다.")]
    [Min(0.1f)] public float cloudDetailInterval = 0.9f;
    [Tooltip("안개 조각에 곱할 알파 배율입니다.")]
    [Range(0f, 1f)] public float cloudDetailOpacity = 0.18f;
    [Tooltip("머티리얼이 지원할 때 적용할 발광 색입니다.")]
    [ColorUsage(true, true)] public Color emissionColor = Color.white;
    [Tooltip("머티리얼이 지원할 때 적용할 발광 세기입니다.")]
    [Min(0f)] public float emissionStrength = 1f;
    [Tooltip("발광 색을 전달할 셰이더 프로퍼티 이름입니다.")]
    public string emissionColorProperty = "_EmissionColor";
    [Tooltip("발광 세기를 전달할 셰이더 프로퍼티 이름입니다.")]
    public string emissionStrengthProperty = "_Glow";
    [Tooltip("조명 반응 정도를 전달할 셰이더 프로퍼티 이름입니다.")]
    public string lightResponseProperty = "_LightResponse";
    [Tooltip("머티리얼이 지원할 때 적용할 조명 반응 정도입니다.")]
    [Range(0f, 1f)] public float lightResponse = 1f;
    [Tooltip("가장자리 강조 값을 전달할 셰이더 프로퍼티 이름입니다.")]
    public string rimProperty = "_RimStrength";
    [Tooltip("머티리얼이 지원할 때 적용할 가장자리 강조 값입니다.")]
    public float rimStrength;
    [Tooltip("중심 강조 값을 전달할 셰이더 프로퍼티 이름입니다.")]
    public string centerProperty = "_CenterStrength";
    [Tooltip("머티리얼이 지원할 때 적용할 중심 강조 값입니다.")]
    public float centerStrength;
    [Header("Gas / trailing layers")]
    [Tooltip("본체를 포함한 기본 조각 수입니다. 여러 크기 사용 시 비불꽃 효과는 최소 3개입니다.")]
    [Range(1, 5)] public int pieceCount = 1;
    [Tooltip("뒤쪽 조각마다 더할 재생 지연(초)입니다.")]
    [Min(0f)] public float pieceDelay = 0.07f;
    [Tooltip("뒤쪽 조각으로 갈 때마다 곱할 밝기·알파 감쇠 배율입니다.")]
    [Range(0f, 1f)] public float pieceAttenuation = 0.75f;
    [Tooltip("뒤쪽 조각으로 갈수록 기본 색에 곱할 꼬리 색입니다.")]
    public Color tailTint = Color.white;
    [Tooltip("종료 상태에서 기본 색에 곱할 색입니다. 곡선을 켜면 색 진행도 곡선으로 적용 시점을 조절합니다.")]
    public Color spentTint = Color.white;
    [Tooltip("뒤쪽 조각으로 갈수록 적용할 크기 배율입니다.")]
    [Min(0f)] public float tailExpansion = 1f;
    [Tooltip("뒤쪽 조각의 가장자리가 흔들리고 퍼지는 정도입니다.")]
    [Min(0f)] public float edgeSpread = 0.1f;
    [Tooltip("기체·불꽃이 위로 이동하는 기본 속도입니다.")]
    public float gasRise = 0.4f;
    [Tooltip("기체·불꽃 조각이 시간에 따라 팽창하는 정도입니다.")]
    public float gasExpansion = 0.4f;
    [Tooltip("뒤쪽 조각마다 더할 가로·세로 위치 간격입니다.")]
    public Vector2 tailOffset = new(-0.15f, 0.1f);
    [Tooltip("효과의 전체 기준 크기 배율입니다.")]
    [Min(0f)] public float visualScale = 1f;
    [Header("Per-play motion and shape variation")]
    [Tooltip("본체 이동에 더할 불규칙한 흔들림 정도입니다.")]
    [Min(0f)] public float motionWander;
    [Tooltip("형태와 위치 변화에 사용하는 노이즈의 진행 속도입니다.")]
    [Min(0.01f)] public float variationFrequency = 1.5f;
    [Tooltip("생성 시 한 번 정하는 크기 편차입니다. 0 또는 0.2는 기준의 80~120%이며 수명 중 다시 추첨하지 않습니다.")]
    [Range(0f, 0.5f)] public float sizeVariation = DefaultSizeVariation;
    [Tooltip("켜면 조각에 대·중·소 기준 크기를 섞어 적용합니다.")]
    public bool mixedPieceSizes = true;
    [Tooltip("대·중·소 조각의 기준 배율입니다. 각 조각의 생성 크기 편차를 추가로 곱합니다.")]
    public Vector3 pieceSizeScales = new(1f, 0.7f, 0.45f);
    [Tooltip("효과와 조각의 가로·세로 형태가 흔들리는 정도입니다.")]
    [Range(0f, 0.3f)] public float shapeVariation;
    [Header("Nearby blobs share one translucent surface")]
    [Tooltip("켜면 주변 조각을 하나의 반투명 표면으로 합쳐 그립니다.")]
    public bool mergeNearby;
    [Tooltip("병합 표면을 만들 때 사용할 조각 반지름 배율입니다.")]
    [Range(0.2f, 1f)] public float mergeRadius = 0.7f;
    [Tooltip("병합 표면 가장자리의 알파 전환 폭입니다.")]
    [Range(0.02f, 0.5f)] public float mergeSoftness = 0.18f;
    [Tooltip("개별 병합 렌더러의 초당 갱신 빈도입니다. 월드 타일에서는 월드 갱신 설정을 사용합니다.")]
    [Range(5f, 30f)] public float mergeRefreshRate = 20f;
    [Tooltip("병합 표면 무늬의 공간 크기 설정입니다.")]
    [Min(0.01f)] public float surfaceNoiseScale = 0.65f;
    [Tooltip("병합 표면 색에 적용할 무늬 강도입니다.")]
    [Range(0f, 1f)] public float surfaceNoiseStrength = 0.15f;
    [Tooltip("병합 표면 무늬가 시간에 따라 변하는 속도입니다.")]
    [Min(0f)] public float surfaceNoiseSpeed = 0.08f;
    [Tooltip("병합 표면의 기본 몸통 색입니다.")]
    public Color surfaceBodyColor = Color.white;
    [Tooltip("병합 표면의 밝은 무늬 색입니다.")]
    public Color surfaceHighlightColor = Color.white;
    [Tooltip("병합 표면의 밝은 무늬 강도입니다.")]
    [Range(0f, 1f)] public float surfaceHighlightStrength;
    [Tooltip("병합 표면의 입체적인 명암 강도입니다.")]
    [Range(0f, 2f)] public float surfaceRelief;
    [Tooltip("병합 표면의 어두운 명암 색입니다.")]
    public Color surfaceShadowColor = new(0.24f, 0.27f, 0.33f);
    [Tooltip("병합 표면 명암 계산에 사용할 빛의 방향입니다.")]
    public Vector3 surfaceLightDirection = new(-0.45f, 0.65f, 0.65f);
    [Tooltip("병합 표면 반사광 강도입니다.")]
    [Range(0f, 2f)] public float surfaceSpecular;
    [Tooltip("병합 표면 반사광의 집중도입니다. 클수록 반사 영역이 좁습니다.")]
    [Range(4f, 128f)] public float surfaceShininess = 48f;
    [Tooltip("물 착지 후 충돌 거품이 유지되는 기준 시간(초)입니다.")]
    [Min(0.01f)] public float impactFoamDuration = 0.65f;
    [Tooltip("물 착지 시 충돌 거품의 강도입니다.")]
    [Range(0f, 2f)] public float impactFoamStrength;
    [Tooltip("물 거품의 알파 배율입니다.")]
    [Range(0f, 1f)] public float foamOpacity = 0.85f;
    [Header("Smoke layers at the end of the fire trail")]
    [Tooltip("불 꼬리에 추가할 연기 효과 정의입니다.")]
    public EffectDefinitionSO smokeTail;
    [Tooltip("불 꼬리 조각 중 연기 설정으로 표시할 조각 수입니다.")]
    [Range(1, 4)] public int smokeTailPieces = 2;
    [Header("Liquid flight; landing starts Intro")]
    [Tooltip("액체가 착지하기 전 비행할 시간(초)입니다.")]
    [Min(0f)] public float airborneDuration = 0.25f;
    [Tooltip("액체의 비행 중 포물선 높이입니다.")]
    public float airborneHeight = 0.5f;
    [Tooltip("액체가 착지한 뒤 적용할 가로·세로 배율입니다.")]
    public Vector2 puddleScale = new(1.4f, 0.7f);
    [Header("Independent local Light2D")]
    [Tooltip("켜면 효과에 별도의 2D 점광원을 사용합니다.")]
    public bool useLight;
    [Tooltip("광원을 일정하게 유지할지, 불처럼 깜박이게 할지 정합니다.")]
    public EffectLightMode lightMode;
    [Tooltip("효과의 2D 점광원 색입니다.")]
    public Color lightColor = Color.white;
    [Tooltip("효과의 2D 점광원 기본 밝기입니다.")]
    [Min(0f)] public float lightIntensity = 1f;
    [Tooltip("효과의 2D 점광원 바깥 반지름입니다.")]
    [Min(0f)] public float lightRadius = 2f;
    [Tooltip("점광원 밝기가 깜박이는 정도입니다.")]
    [Range(0f, 1f)] public float flickerAmount = 0.15f;
    [Tooltip("점광원 밝기가 깜박이는 속도입니다.")]
    [Min(0f)] public float flickerFrequency = 12f;
    [Tooltip("스프라이트가 속할 Sorting Layer의 ID입니다.")]
    public int sortingLayerId;
    [Tooltip("같은 Sorting Layer 안의 그리기 순서입니다. 클수록 앞에 그립니다.")]
    public int sortingOrder = 20;
    [Header("Future integration data only")]
    [Tooltip("투사체 부착 연동을 위한 위치 보정 데이터입니다.")]
    public Vector2 attachmentOffset;
    [Tooltip("충돌 시 효과 생성 연동을 위한 설정 데이터입니다.")]
    public bool spawnOnImpact = true;
    [Tooltip("투사체 연동에 사용할 시각적 크기 배율 데이터입니다.")]
    public float projectileVisualScale = 1f;

    [NonSerialized] private Sprite[][] clips;
    [NonSerialized] private List<Sprite> ownedSprites;

    public Sprite[] GetFrames(EffectPhase phase)
    {
        if (clips == null)
        {
            if (!Application.isPlaying) ClearClips();
            ownedSprites ??= new List<Sprite>();
            clips = new[] { BuildFrames(intro), BuildFrames(idle), BuildFrames(end) };
        }
        int index = (int)phase - (int)EffectPhase.Intro;
        return index >= 0 && index < clips.Length ? clips[index] : Array.Empty<Sprite>();
    }

    public float GetFps(EffectPhase phase)
    {
        EffectClipDefinition clip = phase == EffectPhase.Intro ? intro : phase == EffectPhase.Idle ? idle : end;
        return clip != null ? clip.SafeFps : 12f;
    }

    private Sprite[] BuildFrames(EffectClipDefinition clip)
    {
        if (clip == null || clip.frameCount <= 0 || clip.startIndex < 0)
            return Array.Empty<Sprite>();
        var result = new List<Sprite>();
        if (!clip.useSheet)
        {
            if (clip.sprites == null || clip.startIndex >= clip.sprites.Length)
                return Array.Empty<Sprite>();
            int count = Math.Min(clip.frameCount, clip.sprites.Length - clip.startIndex);
            for (int i = 0; i < count; i++)
                if (clip.sprites[clip.startIndex + i] != null)
                    result.Add(clip.sprites[clip.startIndex + i]);
            return result.ToArray();
        }
        if (sourceSheet == null || sourceSheet.packed || cellSize.x <= 0 || cellSize.y <= 0 || layout.x <= 0 || layout.y <= 0)
            return Array.Empty<Sprite>();
        Rect rect = sourceSheet.rect;
        if ((long)layout.x * cellSize.x > rect.width || (long)layout.y * cellSize.y > rect.height)
            return Array.Empty<Sprite>();
        long available = (long)layout.x * layout.y - clip.startIndex;
        int frameCount = (int)Math.Min(clip.frameCount, Math.Max(0L, available));
        Vector2 pivot = new(sourceSheet.pivot.x / rect.width, sourceSheet.pivot.y / rect.height);
        for (int i = 0; i < frameCount; i++)
        {
            int index = clip.startIndex + i;
            Rect cell = new(rect.x + index % layout.x * cellSize.x,
                rect.yMax - (index / layout.x + 1) * cellSize.y, cellSize.x, cellSize.y);
            Sprite sprite = Sprite.Create(sourceSheet.texture, cell, pivot, sourceSheet.pixelsPerUnit,
                0, SpriteMeshType.FullRect);
            sprite.name = name + "_" + index;
            ownedSprites.Add(sprite);
            result.Add(sprite);
        }
        return result.ToArray();
    }

    private void OnDisable() { ClearClips(); }
    private void OnValidate() { clips = null; }

    private void ClearClips()
    {
        if (ownedSprites != null)
            foreach (Sprite sprite in ownedSprites)
                if (sprite != null)
                {
                    if (Application.isPlaying) Destroy(sprite);
                    else DestroyImmediate(sprite);
                }
        ownedSprites = null;
        clips = null;
    }
}

[Serializable]
public sealed class EffectDetailSettings
{
    [Tooltip("켜면 이 추가 디테일을 생성합니다.")]
    public bool enabled = true;
    [Tooltip("동시에 유지할 디테일 수의 상한입니다. 실제 최대는 불씨 32개·물방울 8개·안개 2개입니다.")]
    [Range(1, 32)] public int capacity = 24;
    [Tooltip("추가 디테일의 초당 생성 빈도입니다. Minimum Interval보다 촘촘하게 생성되지는 않습니다.")]
    [Min(0f)] public float emissionRate = 12f;
    [Tooltip("추가 디테일 생성 간격의 하한(초)입니다.")]
    [Min(0.01f)] public float minimumInterval = 0.25f;
    [Tooltip("불씨의 상승기류 가속도입니다. 냉각될수록 부력이 줄어듭니다.")]
    [Min(0f)] public float updraftAcceleration = 1.6f;
    [Tooltip("추가 디테일 수명의 최소·최대(초)입니다. 불씨에는 Ember Lifetime Scale을 곱합니다.")]
    public Vector2 lifetime = new(0.4f, 0.9f);
    [Tooltip("안개 조각의 기준 반지름 범위입니다. 불씨·물방울은 이 값 대신 표면의 1픽셀 크기로 그립니다.")]
    public Vector2 radius = new(0.025f, 0.055f);
    [Tooltip("추가 디테일의 좌우·방사 방향 퍼짐 속도입니다.")]
    [Min(0f)] public float spread = 0.8f;
    [Tooltip("추가 디테일의 기본 상승 속도입니다. 양수는 위쪽입니다.")]
    public float rise = 0.6f;
    [Tooltip("안개 조각이 수명 동안 커지는 정도입니다. 불씨·물방울은 1픽셀을 유지합니다.")]
    [Min(0f)] public float expansion = 0.4f;
    [Tooltip("펼친 뒤 Enabled를 켜고 곡선을 클릭하면 키를 드래그해 색상 진행·투명도·위치를 조절할 수 있습니다.")]
    public EffectVisualCurves visualCurves = new();
}

[Serializable]
public sealed class EffectVisualCurves
{
    [Tooltip("켜면 아래 곡선을 적용합니다. 곡선을 클릭해 키를 추가·드래그하세요. 불꽃·디테일은 각 조각의 수명, 일반 본체는 전체 재생 수명을 0~1로 사용합니다. 무기한 본체는 Duration이 양수면 그 시간, 아니면 2초마다 반복합니다.")]
    public bool enabled;
    [Tooltip("가로축은 수명 비율 0~1, 세로축은 기존 색 전환 진행도 0~1입니다. 불은 밝은 불→따뜻한 불→그을음, 불씨는 시작→끝 색, 나머지는 Tint→Tint×Spent Tint입니다.")]
    public AnimationCurve colorProgress = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Tooltip("가로축은 수명 비율 0~1, 세로축은 추가 알파 배율 0~1입니다. 기존 소멸·종료 알파에 곱합니다.")]
    public AnimationCurve opacity = AnimationCurve.Linear(0f, 1f, 1f, 1f);
    [Tooltip("가로축은 수명 비율 0~1, 세로축은 기준 크기에서의 추가 가로 이동 거리입니다. 양수는 오른쪽이며 전체 크기 배율을 곱합니다.")]
    public AnimationCurve offsetX = AnimationCurve.Linear(0f, 0f, 1f, 0f);
    [Tooltip("가로축은 수명 비율 0~1, 세로축은 기준 크기에서의 추가 세로 이동 거리입니다. 양수는 위쪽이며 전체 크기 배율을 곱합니다.")]
    public AnimationCurve offsetY = AnimationCurve.Linear(0f, 0f, 1f, 0f);

    internal float ColorProgress(float age) => enabled ? Mathf.Clamp01(Evaluate(colorProgress, age, age)) : age;
    internal float Opacity(float age) => enabled ? Mathf.Clamp01(Evaluate(opacity, age, 1f)) : 1f;
    internal Vector2 Offset(float age) => enabled
        ? new Vector2(Evaluate(offsetX, age, 0f), Evaluate(offsetY, age, 0f)) : Vector2.zero;

    private static float Evaluate(AnimationCurve curve, float age, float fallback)
    {
        if (curve == null || curve.length == 0) return fallback;
        float value = curve.Evaluate(Mathf.Clamp01(age));
        return EffectNumbers.Finite(value) ? value : fallback;
    }
}

internal static class EffectNumbers
{
    public static float SpawnScale(EffectDefinitionSO data)
    {
        float range = Mathf.Clamp(Positive(data.sizeVariation, EffectDefinitionSO.DefaultSizeVariation), 0f, 0.5f);
        return UnityEngine.Random.Range(1f - range, 1f + range);
    }

    public static float PieceScale(EffectDefinitionSO data, int index) =>
        Positive(index % 3 == 0 ? data.pieceSizeScales.x : index % 3 == 1 ? data.pieceSizeScales.y : data.pieceSizeScales.z, 1f);

    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static float Positive(float value, float fallback) => Finite(value) && value > 0f ? value : fallback;
    public static float NonNegative(float value) => Finite(value) ? Mathf.Max(0f, value) : 0f;
}
