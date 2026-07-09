using UnityEngine;

public class MapModule : MonoBehaviour
{
    [Header("Connection Settings")]
    [Tooltip("중앙 플랫폼의 앵커와 맞닿을 실제 연결 오브젝트입니다.")]
    public Transform connector;

    /// <summary>
    /// 모듈의 루트(Pivot)와 커넥터 사이의 로컬 거리 차이를 반환합니다.
    /// </summary>
    public Vector3 GetOffset()
    {
        if (connector == null)
        {
            Debug.LogWarning($"{gameObject.name}: Connector가 설정되지 않았습니다!");
            return Vector3.zero;
        }
        return connector.localPosition;
    }
}
