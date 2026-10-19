using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class BattleUiMaterialController : MonoBehaviour
{
    public enum UiChannel { Presenter, PresenterDialogue, ItemWindow, CurrentItems, ChatWindow }
    public enum PropertyType { Float, Integer, Color, Vector, Texture }

    [Serializable]
    public sealed class ShaderProperty
    {
        [Tooltip("셰이더 속성의 정확한 참조 이름을 입력하세요(예: _Color). 없는 속성은 건너뜁니다.")]
        public string propertyName;
        [Tooltip("셰이더에 선언된 속성 타입을 선택하고, 아래에서 해당 타입의 Value만 설정하세요.")]
        public PropertyType type;
        public float floatValue;
        public int integerValue;
        public Color colorValue = Color.white;
        public Vector4 vectorValue;
        public Texture textureValue;

        public void Apply(Material instance)
        {
            if (string.IsNullOrWhiteSpace(propertyName) || !instance.HasProperty(propertyName))
                return;

            switch (type)
            {
                case PropertyType.Float: instance.SetFloat(propertyName, floatValue); break;
                case PropertyType.Integer: instance.SetInteger(propertyName, integerValue); break;
                case PropertyType.Color: instance.SetColor(propertyName, colorValue); break;
                case PropertyType.Vector: instance.SetVector(propertyName, vectorValue); break;
                case PropertyType.Texture: instance.SetTexture(propertyName, textureValue); break;
            }
        }
    }

    [Serializable]
    public sealed class UiMaterialEntry
    {
        [Tooltip("필수: 적용할 UI 오브젝트의 Image, RawImage, Text 등 Graphic 컴포넌트를 연결하세요.")]
        public Graphic target;
        [Tooltip("필수: UI용 셰이더를 사용하는 머티리얼을 연결하세요. 원본 대신 대상별 복제본을 조절합니다.")]
        public Material material;
        [Tooltip("조절할 속성을 추가하고 Property Name, Type, 해당 Value를 설정하세요. 비워 두면 머티리얼만 적용합니다.")]
        public List<ShaderProperty> properties = new List<ShaderProperty>();
    }

    [Serializable]
    public sealed class UiMaterialGroup
    {
        [Tooltip("이 위치에 적용할 머티리얼 하나를 연결하세요. 비우면 기존 머티리얼을 유지합니다. 머티리얼 자체의 값을 설정하면 복제본에 반영됩니다.")]
        public Material material;
    }

    private sealed class MaterialState
    {
        public Graphic target;
        public Material original;
        public Material instance;
        public Material source;
        public List<ShaderProperty> properties;
        public bool automatic;
        public UiMaterialGroup group;
    }

    [SerializeField, Tooltip("SpriteManager의 BattleShowPresentationManager를 연결하세요. 같은 오브젝트에서는 자동 연결됩니다. UI나 Root를 하나씩 지정할 필요가 없습니다.")]
    private BattleShowPresentationManager presentation;
    private BattleShowPresentationManager boundPresentation;
    [SerializeField, Tooltip("SpriteManager 오브젝트의 Add Component에서 BattleUiMaterialController를 추가하세요. 켜면 플레이 중 활성화될 때 자동 적용합니다.")]
    private bool applyOnEnable = true;
    [SerializeField, Tooltip("켜면 Inspector 값으로 프리뷰합니다. 끄면 원복합니다. 컴포넌트 메뉴의 Apply Materials / Start Preview / End Preview / Restore Materials로도 적용·프리뷰·원복할 수 있습니다. 비활성화·제거 시에도 원복하고 복제본을 정리합니다.")]
    private bool preview;

    [Header("사회자")]
    [SerializeField] private UiMaterialGroup presenter = new UiMaterialGroup();
    [Header("사회자 대화창")]
    [SerializeField] private UiMaterialGroup presenterDialogue = new UiMaterialGroup();
    [Header("아이템창")]
    [SerializeField] private UiMaterialGroup itemWindow = new UiMaterialGroup();
    [Header("현재 아이템 출력")]
    [SerializeField] private UiMaterialGroup currentItems = new UiMaterialGroup();
    [Header("채팅창")]
    [SerializeField] private UiMaterialGroup chatWindow = new UiMaterialGroup();

    [Header("개별 UI 설정 (선택)")]
    [SerializeField, Tooltip("개별 예외가 필요할 때만 설정하세요. 기본 대상은 자동 연결되며, 개별 설정은 그룹 설정보다 우선합니다.")]
    private List<UiMaterialEntry> entries = new List<UiMaterialEntry>();

    private readonly List<MaterialState> states = new List<MaterialState>();
    private bool applied;
    private bool previewApplied;
    private bool refreshRequested;

    private void OnEnable()
    {
        ConnectPresentation();
        preview = false;
        if (Application.IsPlaying(gameObject) && applyOnEnable)
            Apply();
    }

    private void Start()
    {
        ConnectPresentation();
        if (applied)
            ApplyMaterials();
    }

    private void ConnectPresentation()
    {
        if (presentation == null)
            presentation = GetComponent<BattleShowPresentationManager>();
        if (presentation == null)
            presentation = BattleShowPresentationManager.Instance;
        if (boundPresentation == presentation)
            return;
        if (boundPresentation != null)
            boundPresentation.UiMaterialTargetAssigned -= ApplyRegisteredTarget;
        boundPresentation = presentation;
        if (boundPresentation != null)
            boundPresentation.UiMaterialTargetAssigned += ApplyRegisteredTarget;
    }

    private void OnValidate()
    {
        refreshRequested = true;
    }

    private void Update()
    {
        if (!refreshRequested)
            return;

        refreshRequested = false;
        if (previewApplied && !preview)
            Restore();
        else if (preview || (Application.IsPlaying(gameObject) && applied))
            ApplyMaterials();
        else
            Restore();
    }

    [ContextMenu("Apply Materials")]
    public void Apply()
    {
        if (!isActiveAndEnabled)
            return;

        ConnectPresentation();
        if (!Application.IsPlaying(gameObject))
            preview = true;
        ApplyMaterials();
    }

    [ContextMenu("Start Preview")]
    public void StartPreview()
    {
        if (!isActiveAndEnabled)
            return;

        ConnectPresentation();
        preview = true;
        ApplyMaterials();
    }

    [ContextMenu("End Preview")]
    public void EndPreview()
    {
        Restore();
    }

    [ContextMenu("Restore Materials")]
    public void Restore()
    {
        preview = false;
        applied = false;
        previewApplied = false;
        ReleaseMaterials();
    }

    private void ApplyMaterials()
    {
        ReleaseMaterials();
        applied = true;
        previewApplied = preview;
        var targets = new HashSet<Graphic>();
        if (entries != null)
        {
            foreach (UiMaterialEntry entry in entries)
            {
                if (entry != null)
                    ApplyTarget(entry.target, entry.material, entry.properties, targets);
            }
        }
        if (boundPresentation != null)
        {
            foreach (var target in boundPresentation.UiMaterialTargets)
                ApplyRegisteredTarget(target.Key, target.Value);
        }
    }

    private UiMaterialGroup GetGroup(UiChannel channel)
    {
        switch (channel)
        {
            case UiChannel.Presenter: return presenter;
            case UiChannel.PresenterDialogue: return presenterDialogue;
            case UiChannel.ItemWindow: return itemWindow;
            case UiChannel.CurrentItems: return currentItems;
            case UiChannel.ChatWindow: return chatWindow;
            default: return null;
        }
    }

    private void ApplyRegisteredTarget(Graphic target, UiChannel channel)
    {
        if (!applied || !isActiveAndEnabled || target == null)
            return;
        UiMaterialGroup group = GetGroup(channel);

        for (int i = 0; i < states.Count; i++)
        {
            MaterialState state = states[i];
            if (state.target != target)
                continue;
            if (!state.automatic)
            {
                if (target.material == state.instance)
                    return;

                Material source = state.source;
                List<ShaderProperty> properties = state.properties;
                if (entries != null)
                {
                    foreach (UiMaterialEntry entry in entries)
                    {
                        if (entry == null || entry.target != target || entry.material == null)
                            continue;
                        source = entry.material;
                        properties = entry.properties;
                        break;
                    }
                }

                if (source == state.source && properties == state.properties &&
                    (target.material == state.original ||
                     (state.original == null && target.material == target.defaultMaterial)))
                {
                    target.material = state.instance;
                    return;
                }

                DestroyInstance(state.instance);
                states.RemoveAt(i);
                ApplyTarget(target, source != null ? source : target.material, properties, null);
                return;
            }
            if (state.group != group)
            {
                if (target.material == state.instance)
                    target.material = state.original;
                DestroyInstance(state.instance);
                states.RemoveAt(i);
                break;
            }
            if (target.material == state.instance)
                return;
            if (target.material == state.original ||
                (state.original == null && target.material == target.defaultMaterial))
            {
                target.material = state.instance;
                return;
            }
            DestroyInstance(state.instance);
            states.RemoveAt(i);
            break;
        }

        if (group == null || group.material == null)
            return;
        int previousCount = states.Count;
        ApplyTarget(target, group.material, null, null);
        if (states.Count > previousCount)
        {
            states[states.Count - 1].automatic = true;
            states[states.Count - 1].group = group;
        }
    }

    private void LateUpdate()
    {
        foreach (MaterialState state in states)
        {
            if (state.target == null || !state.target.isActiveAndEnabled ||
                state.instance == null || state.source == null)
                continue;
            state.instance.CopyPropertiesFromMaterial(state.source);
            if (state.properties != null)
            {
                foreach (ShaderProperty property in state.properties)
                    property?.Apply(state.instance);
            }
            state.target.SetMaterialDirty();
        }
    }

    private void ApplyTarget(Graphic target, Material source, List<ShaderProperty> properties,
        HashSet<Graphic> targets)
    {
        if (target == null || source == null)
            return;
#if UNITY_EDITOR
        if (UnityEditor.EditorUtility.IsPersistent(target) || !target.gameObject.scene.IsValid())
            return;
#endif
        if (targets != null && !targets.Add(target))
            return;

        Material original = target.material;
#if UNITY_EDITOR
        // Preserve an unassigned material as null during editor preview.
        if (!Application.IsPlaying(gameObject))
        {
            var serializedTarget = new UnityEditor.SerializedObject(target);
            var materialProperty = serializedTarget.FindProperty("m_Material");
            if (materialProperty != null)
                original = materialProperty.objectReferenceValue as Material;
        }
#endif
        var instance = new Material(source)
        {
            name = source.name + " (UI Instance)",
            hideFlags = HideFlags.HideAndDontSave
        };
        states.Add(new MaterialState
        {
            target = target, original = original, instance = instance,
            source = source, properties = properties
        });
        if (properties != null)
        {
            foreach (ShaderProperty property in properties)
                property?.Apply(instance);
        }
        target.material = instance;
        target.SetMaterialDirty();
    }

    private void ReleaseMaterials()
    {
        foreach (MaterialState state in states)
        {
            if (state.target != null)
            {
                state.target.material = state.original;
                state.target.SetMaterialDirty();
            }

            DestroyInstance(state.instance);
        }
        states.Clear();
    }

    private void DestroyInstance(Material instance)
    {
        if (instance == null)
            return;
        if (Application.IsPlaying(gameObject))
            Destroy(instance);
        else
            DestroyImmediate(instance);
    }

    private void OnDisable()
    {
        if (boundPresentation != null)
            boundPresentation.UiMaterialTargetAssigned -= ApplyRegisteredTarget;
        boundPresentation = null;
        Restore();
    }

    private void OnDestroy()
    {
        Restore();
    }
}
