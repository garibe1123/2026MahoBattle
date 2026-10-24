using TMPro;
using UnityEngine;

[AddComponentMenu("")]
public sealed class BattleUiStyleTitleText : TextMeshProUGUI
{
    private Bounds motionBounds;
    private bool hasMotionBounds;

    internal void SetMotionBounds(Bounds bounds)
    {
        motionBounds = bounds;
        hasMotionBounds = true;
    }

    internal void ClearMotionBounds() => hasMotionBounds = false;

    protected override Bounds GetCompoundBounds()
    {
        Bounds bounds = base.GetCompoundBounds();
        if (hasMotionBounds) bounds.Encapsulate(motionBounds);
        return bounds;
    }
}
