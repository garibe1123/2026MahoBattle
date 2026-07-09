using UnityEngine;

[CreateAssetMenu(menuName = "Game/Effect/Visual SO")]
public class EffectVisualSO : ScriptableObject
{
    public Sprite[] startSprites; // 예열/생성 연출
    public Sprite[] idleSprites;  // 유지/발사 연출
    public Sprite[] endSprites;   // 소멸 연출
    public float fps = 12f;

    [Header("Material Control (Shader)")]
    public Material customMaterial; // 모든 이펙트가 돌려쓸 원본 머티리얼 1개

    [ColorUsage(true, true)] // HDR을 켜서 Emission(빛방출) 가능하게 만듦
    public Color glowColor = Color.white;

    // 디졸브(투명도) 커브: 시작할 때 어떻게 나타나고 사라질지 그래프로 제어
    public AnimationCurve dissolveCurve = AnimationCurve.Linear(0, 0, 1, 1);
}
