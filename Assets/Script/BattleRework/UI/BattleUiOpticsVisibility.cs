using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class BattleUiOpticsVisibility : MonoBehaviour, IMaterialModifier
{
    private Graphic target;
    private BattleUiOpticsCanvas owner;
    private Material protectionMaterial;
    private bool hasMask;
    private Canvas[] parentCanvases;
    internal Canvas SortingCanvas
    {
        get
        {
            if (parentCanvases == null) return null;
            foreach (Canvas canvas in parentCanvases)
                if (canvas != null && (canvas.overrideSorting || canvas.isRootCanvas)) return canvas;
            return null;
        }
    }
    internal bool NativePresenter { get; private set; }

    internal bool Visible => target != null && target.isActiveAndEnabled &&
        !target.canvasRenderer.cull && target.canvasRenderer.GetInheritedAlpha() > 0.001f;

    internal bool ToneSurface => IsToneSurface(target);

    internal void EncapsulateScreenBounds(Vector3[] corners, ref Vector2 min, ref Vector2 max)
    {
        if (!Visible || (NativePresenter && owner != null && owner.PreserveNativePresenters)) return;
        target.rectTransform.GetWorldCorners(corners);
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, corners[i]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
    }

    public static bool IsToneSurface(Graphic target)
    {
        if (target is BattleUiPanelDecoration) return true;
        if (!(target is Image image) || image.type == Image.Type.Filled) return false;
        string key = image.name;
        if (key.Contains("Icon") || key.Contains("Thumbnail") || key.Contains("Portrait")) return false;
        return image.sprite == null || image.sprite == BattleHudSpriteCache.RoundedPanel ||
            key.Contains("Frame") || key.Contains("Shadow") || key.Contains("Background") || key.Contains("Panel") ||
            key == "TailShape" || key == "TailFill";
    }

    private void Awake()
    {
        target = GetComponent<Graphic>();
        hasMask = GetComponent<Mask>() != null;
    }

    internal void Bind(BattleUiOpticsCanvas value)
    {
        if (owner != null && owner != value) owner.RemoveVisibility(this, target, true);
        owner = value;
        parentCanvases = GetComponentsInParent<Canvas>(true);
        BattleShowPresentationManager presentation = BattleShowPresentationManager.Instance;
        NativePresenter = target != null && presentation != null &&
            presentation.UiMaterialTargets.TryGetValue(target, out var channel) &&
            channel == BattleUiMaterialController.UiChannel.Presenter;
        Refresh();
    }

    public Material GetModifiedMaterial(Material baseMaterial)
    {
        BattleUiOpticsCanvas capturing = BattleUiOpticsCanvas.ActiveCapture;
        if (capturing != null && capturing != owner)
            return hasMask ? HideColor(baseMaterial, capturing.ProtectionHiddenMaterial) : capturing.ProtectionHiddenMaterial;
        if (isActiveAndEnabled && owner != null && NativePresenter && owner.PreserveNativePresenters)
            return owner.IsCapturing ? owner.ProtectionHiddenMaterial : baseMaterial;
        Material hidden = isActiveAndEnabled && owner != null ? owner.HiddenSourceMaterial : null;
        if (hidden != null) return hidden;
        if (!isActiveAndEnabled || owner == null || !owner.CapturingProtection || !ToneSurface) return baseMaterial;
        return HideColor(baseMaterial, owner.ProtectionHiddenMaterial);
    }

    private Material HideColor(Material baseMaterial, Material fallback)
    {
        if (baseMaterial == null || !baseMaterial.HasProperty("_ColorMask"))
            return hasMask ? baseMaterial : fallback;
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
    internal void RegisterCanvas() => BattleUiOpticsCanvas.Register(target);
    private void OnCanvasHierarchyChanged()
    {
        if (BattleUiOpticsCanvas.ActiveCapture == null) RegisterCanvas();
    }
    private void OnDisable() => Refresh();
    private void OnDestroy()
    {
        if (owner != null) owner.RemoveVisibility(this, target);
        ReleaseProtection();
        Refresh();
    }
}
