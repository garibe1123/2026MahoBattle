using UnityEngine;

/// <summary>
/// 씬별로 같은 공용 Universal Stage Dressing Manager에 Profile만 공급하는 선택 컴포넌트입니다.
/// 이 컴포넌트가 없어도 Floor가 발견되면 기본 모드로 동작합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleUniversalStageDecorSceneConfig : MonoBehaviour
{
    [SerializeField] private BattleUniversalStageDecorProfileSO profile;
    [SerializeField] private bool disableUniversalStageDecor;

    public BattleUniversalStageDecorProfileSO Profile => profile;
    public bool DisableUniversalStageDecor => disableUniversalStageDecor;

    private void OnEnable()
    {
        BattleUniversalStageDecorController.RequestRefresh(0.05f);
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            BattleUniversalStageDecorController.RequestRefresh(0.05f);
    }
}
