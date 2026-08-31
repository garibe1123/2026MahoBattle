using System;
using System.Collections.Generic;
using UnityEngine;

public class FanMissionSystem : MonoBehaviour
{
    [SerializeField] private RunProgressSystem runProgress;
    [SerializeField, Range(3, 6)] private int unlockedSlots = 3;

    private readonly List<FanMissionRuntime> activeMissions = new();

    public IReadOnlyList<FanMissionRuntime> ActiveMissions => activeMissions;
    public int UnlockedSlots => unlockedSlots;

    public event Action MissionsChanged;

    private void Update()
    {
        // duration <= 0인 미션은 시간 제한이 없는 미션입니다.
        for (int i = activeMissions.Count - 1; i >= 0; i--)
        {
            FanMissionRuntime runtime = activeMissions[i];
            if (runtime == null || runtime.Definition == null)
            {
                activeMissions.RemoveAt(i);
                continue;
            }

            if (runtime.Definition.duration <= 0f)
                continue;

            runtime.RemainingTime = Mathf.Max(0f, runtime.RemainingTime - Time.deltaTime);
            if (runtime.RemainingTime <= 0f)
                FailMission(i);
        }
    }

    public void ResetForRun()
    {
        activeMissions.Clear();
        MissionsChanged?.Invoke();
    }

    public bool TryAddMission(FanMissionSO mission)
    {
        if (mission == null || activeMissions.Count >= unlockedSlots) return false;

        activeMissions.Add(new FanMissionRuntime(mission));
        MissionsChanged?.Invoke();
        return true;
    }

    public void SetUnlockedSlots(int count)
    {
        unlockedSlots = Mathf.Clamp(count, 3, 6);
        MissionsChanged?.Invoke();
    }

    public void AddProgress(FanMissionType type, int amount = 1)
    {
        for (int i = activeMissions.Count - 1; i >= 0; i--)
        {
            FanMissionRuntime runtime = activeMissions[i];
            if (runtime == null || runtime.Definition == null) continue;
            if (runtime.Definition.type != type) continue;

            runtime.Progress += Mathf.Max(0, amount);
            if (runtime.Progress >= Mathf.Max(1, runtime.Definition.targetCount))
                ResolveSuccess(i);
        }

        MissionsChanged?.Invoke();
    }

    public void FailMission(int index)
    {
        if (index < 0 || index >= activeMissions.Count) return;
        FanMissionRuntime runtime = activeMissions[index];

        if (runtime != null && runtime.Definition != null)
        {
            runProgress?.AddPopularity(runtime.Definition.failPopularity);
            runProgress?.AddFanPoints(runtime.Definition.failFanPoints);
        }

        activeMissions.RemoveAt(index);
        MissionsChanged?.Invoke();
    }

    // 최신 기획 기준: 포기도 실패와 동일 페널티를 적용.
    public void RejectMission(int index)
    {
        FailMission(index);
    }

    private void ResolveSuccess(int index)
    {
        if (index < 0 || index >= activeMissions.Count) return;

        FanMissionRuntime runtime = activeMissions[index];
        if (runtime != null && runtime.Definition != null)
        {
            runProgress?.AddPopularity(runtime.Definition.successPopularity);
            runProgress?.AddFanPoints(runtime.Definition.successFanPoints);
        }

        activeMissions.RemoveAt(index);
    }
}

[Serializable]
public class FanMissionRuntime
{
    public FanMissionSO Definition;
    public int Progress;
    public float RemainingTime;

    public FanMissionRuntime(FanMissionSO definition)
    {
        Definition = definition;
        Progress = 0;
        RemainingTime = definition != null ? definition.duration : 0f;
    }
}
