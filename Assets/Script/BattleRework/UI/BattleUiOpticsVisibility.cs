using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class BattleUiOpticsVisibility : MonoBehaviour, IMaterialModifier
{
    private Graphic target;
    private BattleUiOpticsCanvas owner;

    private void Awake() => target = GetComponent<Graphic>();

    internal void Bind(BattleUiOpticsCanvas value)
    {
        owner = value;
        Refresh();
    }

    public Material GetModifiedMaterial(Material baseMaterial)
    {
        Material hidden = isActiveAndEnabled && owner != null ? owner.HiddenSourceMaterial : null;
        return hidden != null ? hidden : baseMaterial;
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

    private void OnEnable() => Refresh();
    private void OnDisable() => Refresh();
    private void OnDestroy() => Refresh();
}
