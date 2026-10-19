using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class BattleUiMaterialController : MonoBehaviour
{
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

    private sealed class MaterialState
    {
        public Graphic target;
        public Material original;
        public Material instance;
    }

    [SerializeField, Tooltip("SpriteManager 오브젝트의 Add Component에서 BattleUiMaterialController를 추가하세요. 켜면 플레이 중 활성화될 때 자동 적용합니다.")]
    private bool applyOnEnable = true;
    [SerializeField, Tooltip("켜면 Inspector 값으로 프리뷰합니다. 끄면 원복합니다. 컴포넌트 메뉴의 Apply Materials / Start Preview / End Preview / Restore Materials로도 적용·프리뷰·원복할 수 있습니다. 비활성화·제거 시에도 원복하고 복제본을 정리합니다.")]
    private bool preview;
    [SerializeField, Tooltip("UI마다 항목을 추가해 Target과 Material을 반드시 연결하고 Properties를 설정하세요. 같은 Graphic은 첫 번째 유효 항목만 적용됩니다.")]
    private List<UiMaterialEntry> entries = new List<UiMaterialEntry>();

    private readonly List<MaterialState> states = new List<MaterialState>();
    private bool applied;
    private bool previewApplied;
    private bool refreshRequested;

    private void OnEnable()
    {
        preview = false;
        if (Application.IsPlaying(gameObject) && applyOnEnable)
            Apply();
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

        if (!Application.IsPlaying(gameObject))
            preview = true;
        ApplyMaterials();
    }

    [ContextMenu("Start Preview")]
    public void StartPreview()
    {
        if (!isActiveAndEnabled)
            return;

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
        if (entries == null)
            return;

        var targets = new HashSet<Graphic>();
        foreach (UiMaterialEntry entry in entries)
        {
            if (entry == null || entry.target == null || entry.material == null)
                continue;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(entry.target) ||
                !entry.target.gameObject.scene.IsValid())
                continue;
#endif
            if (!targets.Add(entry.target))
                continue;

            Material original = entry.target.material;
#if UNITY_EDITOR
            // Preserve an unassigned material as null during editor preview.
            if (!Application.IsPlaying(gameObject))
            {
                var serializedTarget = new UnityEditor.SerializedObject(entry.target);
                var materialProperty = serializedTarget.FindProperty("m_Material");
                if (materialProperty != null)
                    original = materialProperty.objectReferenceValue as Material;
            }
#endif
            var instance = new Material(entry.material)
            {
                name = entry.material.name + " (UI Instance)",
                hideFlags = HideFlags.HideAndDontSave
            };
            states.Add(new MaterialState { target = entry.target, original = original, instance = instance });
            if (entry.properties != null)
            {
                foreach (ShaderProperty property in entry.properties)
                    property?.Apply(instance);
            }
            entry.target.material = instance;
            entry.target.SetMaterialDirty();
        }
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

            if (state.instance == null)
                continue;
            if (Application.IsPlaying(gameObject))
                Destroy(state.instance);
            else
                DestroyImmediate(state.instance);
        }
        states.Clear();
    }

    private void OnDisable()
    {
        Restore();
    }

    private void OnDestroy()
    {
        Restore();
    }
}
