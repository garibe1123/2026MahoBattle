using DG.Tweening;
using NavMeshPlus.Components;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public class StageSetManager : MonoBehaviour
{
    public static StageSetManager Instance;
    public enum SetSize { Small, Medium, Boss }
    private enum ModuleDirection { Top, Bottom, Left, Right }

    [Header("Navigation")]
    [SerializeField] private NavMeshSurface navSurface; // 씬에 배치된 NavMeshSurface

    [Header("Anchors (Base Platform)")]
    [SerializeField] private Transform anchorTop;
    [SerializeField] private Transform anchorBottom;
    [SerializeField] private Transform anchorLeft;
    [SerializeField] private Transform anchorRight;

    [Header("References")]
    [SerializeField] private CinemachineImpulseSource impulseSource;

    [Header("Theme Control")]
    [SerializeField] private ClanDataSO currentTheme;
    [SerializeField] private ClanDataSO nextTheme;

    [Header("Movement Settings")]
    [SerializeField] private float moveDuration = 1.2f;
    [SerializeField] private float spawnOffset = 25f;

    private MapModule currentActiveModule;
    private Vector3 lastEntryDir;

    private void Awake() => Instance = this;

    private void Start()
    {
        UpdateNavigationMesh();
    }

    public void RequestNewSet(SetSize size)
    {
        if (currentTheme == null) return;

        ModuleDirection selectedDir = (size == SetSize.Boss) ? ModuleDirection.Top : (ModuleDirection)Random.Range(0, 4);
        lastEntryDir = GetVectorFromDir(selectedDir);

        MapModule selectedModule = GetRandomModule(currentTheme, size, selectedDir);
        if (selectedModule == null) return;

        SpawnAndAlign(selectedModule, selectedDir);
    }

    private void SpawnAndAlign(MapModule modulePrefab, ModuleDirection dir)
    {
        if (currentActiveModule != null) Destroy(currentActiveModule.gameObject);

        Transform targetAnchor = GetAnchor(dir);
        Vector3 spawnPos = targetAnchor.position + (lastEntryDir * spawnOffset);
        currentActiveModule = Instantiate(modulePrefab, spawnPos, Quaternion.identity);

        Vector3 finalDestination = targetAnchor.position - currentActiveModule.GetOffset();

        currentActiveModule.transform.DOMove(finalDestination, moveDuration)
            .SetEase(Ease.InQuad)
            .OnComplete(() => {
                // 1. [시네머신 임펄스 발생]
                // 세트가 부딪히는 방향과 강도를 고려하여 충격 발생
                if (impulseSource != null)
                {
                    // 묵직한 느낌을 위해 진동 세기를 조절한 임펄스 발생
                    impulseSource.GenerateImpulse(lastEntryDir * 0.5f);
                }

                // 2. 세트 반동 (DOTween은 트랜스폼 기반이라 시네머신과 무관하게 작동)
                currentActiveModule.transform.DOPunchPosition(-lastEntryDir * 0.5f, 0.6f, 10);

                // 3. 내비게이션 갱신
                UpdateNavigationMesh();
            });
    }

    /// <summary>
    /// 기존 NavMesh를 지우고 새로 굽습니다.
    /// </summary>
    private void UpdateNavigationMesh()
    {
        if (navSurface != null)
        {
            // 기존 데이터 클리어 후 실시간 베이킹
            navSurface.RemoveData();
            navSurface.BuildNavMesh();

            Debug.Log("<color=cyan>[NavMesh]</color> 새로운 세트에 맞춰 경로 데이터가 갱신되었습니다.");
        }
    }

    #region Helpers
    private MapModule GetRandomModule(ClanDataSO data, SetSize size, ModuleDirection dir)
    {
        List<MapModule> targetList = null;
        if (size == SetSize.Boss) return GetRandomFromList(data.bossModules);

        if (size == SetSize.Small)
        {
            targetList = dir switch
            {
                ModuleDirection.Top => data.smallTop,
                ModuleDirection.Bottom => data.smallBottom,
                ModuleDirection.Left => data.smallLeft,
                _ => data.smallRight
            };
        }
        else
        {
            targetList = dir switch
            {
                ModuleDirection.Top => data.mediumTop,
                ModuleDirection.Bottom => data.mediumBottom,
                ModuleDirection.Left => data.mediumLeft,
                _ => data.mediumRight
            };
        }
        return GetRandomFromList(targetList);
    }

    private MapModule GetRandomFromList(List<MapModule> list)
    {
        if (list == null || list.Count == 0) return null;
        return list[Random.Range(0, list.Count)];
    }

    private Transform GetAnchor(ModuleDirection dir) => dir switch
    {
        ModuleDirection.Top => anchorTop,
        ModuleDirection.Bottom => anchorBottom,
        ModuleDirection.Left => anchorLeft,
        _ => anchorRight
    };

    private Vector3 GetVectorFromDir(ModuleDirection dir) => dir switch
    {
        ModuleDirection.Top => Vector3.up,
        ModuleDirection.Bottom => Vector3.down,
        ModuleDirection.Left => Vector3.left,
        _ => Vector3.right
    };
    #endregion
}