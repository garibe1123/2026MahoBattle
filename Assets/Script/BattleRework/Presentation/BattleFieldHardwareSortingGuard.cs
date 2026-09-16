using UnityEngine;

/// <summary>
/// Legacy compatibility component.
/// Field/Actor Sorting ownership moved to BattleWorldSortingController + BattleWorldSortAnchor.
/// Existing Scene/Prefab references may keep this component safely; it no longer performs Scene scans
/// or writes SpriteRenderer.sortingOrder.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleFieldHardwareSortingGuard : MonoBehaviour
{
}
