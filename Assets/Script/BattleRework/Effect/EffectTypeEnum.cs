public enum EffectTypeEnum
{
    Zone = 0,     // 1. 단순/부착형 체류 장판
    Laser = 1,    // 2. 레이저 빔
    Mine = 2,     // 3. 지뢰형 (밟으면 지연 후 폭발)
    Spawner = 3   // 4. 오브젝트/투사체 생성형
}

public enum EffectMovementType
{
    Static = 0,       // 제자리 고정
    MoveForward = 1   // 바라보는 방향(정면)으로 직진
}

public enum EffectRotationType
{
    None = 0,             // 회전 없음
    ContinuousSpin = 1,   // 지속 회전 (빙글빙글)
    LookAtTarget = 2      // 가장 가까운 적을 향해 조준 (레이저 등에 적합)
}

public enum EffectScaleType
{
    Fixed = 0,      // 크기 고정
    Expand = 1,     // 점점 커짐
    Shrink = 2      // 점점 작아짐
}