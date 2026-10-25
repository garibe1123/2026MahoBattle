using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class BattleUiOpticsVisibility : MonoBehaviour, IMaterialModifier
{
    private Graphic target;
    private BattleUiOpticsCanvas owner;
    private Material protectionMaterial;
    private bool hasMask;
    internal bool NativePresenter { get; private set; }

    internal bool Visible => target != null && target.isActiveAndEnabled &&
        !target.canvasRenderer.cull && target.canvasRenderer.GetInheritedAlpha() > 0.001f;

    internal bool ToneSurface
    {
        get
        {
            if (target is BattleUiPanelDecoration) return true;
            if (!(target is Image image) || image.type == Image.Type.Filled) return false;
            string key = image.name;
            if (key.Contains("Icon") || key.Contains("Thumbnail") || key.Contains("Portrait")) return false;
            return image.sprite == null || image.sprite == BattleHudSpriteCache.RoundedPanel ||
                key.Contains("Frame") || key.Contains("Shadow") || key.Contains("Background") || key.Contains("Panel") ||
                key == "TailShape" || key == "TailFill";
        }
    }

    private void Awake()
    {
        target = GetComponent<Graphic>();
        hasMask = GetComponent<Mask>() != null;
    }

    internal void Bind(BattleUiOpticsCanvas value)
    {
        owner = value;
        BattleShowPresentationManager presentation = BattleShowPresentationManager.Instance;
        NativePresenter = target != null && presentation != null &&
            presentation.UiMaterialTargets.TryGetValue(target, out var channel) &&
            channel == BattleUiMaterialController.UiChannel.Presenter;
        Refresh();
    }

    public Material GetModifiedMaterial(Material baseMaterial)
    {
        if (isActiveAndEnabled && owner != null && NativePresenter && owner.PreserveNativePresenters)
            return owner.IsCapturing ? owner.ProtectionHiddenMaterial : baseMaterial;
        Material hidden = isActiveAndEnabled && owner != null ? owner.HiddenSourceMaterial : null;
        if (hidden != null) return hidden;
        if (!isActiveAndEnabled || owner == null || !owner.CapturingProtection || !ToneSurface) return baseMaterial;
        if (baseMaterial == null || !baseMaterial.HasProperty("_ColorMask"))
            return hasMask ? baseMaterial : owner.ProtectionHiddenMaterial;
        if (protectionMaterial == null || protectionMaterial.shader != baseMaterial.shader)
        {
            ReleaseProtection();
            protectionMaterial = new Material(baseMaterial) { hideFlags = HideFlags.HideAndDontSave };
        }
        protectionMaterial.CopyPropertiesFromMaterial(baseMaterial);
        protectionMaterial.SetFloat("_ColorMask", 0f);
        return protectionMaterial;
    }

    internal void Refresh()
    {
        if (target != null) target.SetMaterialDirty();
    }

    internal void Release(BattleUiOpticsCanvas value)
    {
        if (owner != value) return;
        owner = null;
        Refresh();
        Destroy(this);
    }

    internal void ReleaseProtection()
    {
        if (protectionMaterial != null) Destroy(protectionMaterial);
        protectionMaterial = null;
    }

    private void OnEnable() => Refresh();
    private void OnDisable() => Refresh();
    private void OnDestroy()
    {
        if (owner != null) owner.RemoveVisibility(this, target);
        ReleaseProtection();
        Refresh();
    }
}
