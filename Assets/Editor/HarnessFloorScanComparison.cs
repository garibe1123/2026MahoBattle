using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

internal static class HarnessFloorScanComparison
{
    private const string MenuPath = "Tools/Harness/Measure Live Floor Scan";
    private const int WarmupScans = 8;
    private const int Batches = 20;
    private const int ScansPerBatch = 32;
    private const string RequestPath = "Temp/HarnessFloorScanComparison.request";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private struct Measurement
    {
        public long ticks;
        public long bytes;
    }

    private sealed class VisualState
    {
        public Transform transform;
        public Transform parent;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public bool active;
        public SpriteRenderer renderer;
        public bool hadRenderer;
        public Sprite sprite;
        public Color color;
        public int sortingLayer;
        public int sortingOrder;
        public bool rendererEnabled;
    }

    [InitializeOnLoadMethod]
    private static void InstallPendingMeasurement()
    {
        if (!File.Exists(RequestPath))
            return;
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        EditorApplication.delayCall += TryMeasurePending;
    }

    private static void HandlePlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
            EditorApplication.delayCall += TryMeasurePending;
    }

    private static void TryMeasurePending()
    {
        if (!File.Exists(RequestPath))
            return;
        BattleShowPresentationManager manager = BattleShowPresentationManager.Instance;
        File.WriteAllText("Temp/HarnessFloorScanComparison.status",
            $"Editor callback UTC: {DateTime.UtcNow:O}; Playing: {EditorApplication.isPlaying}; Manager: {manager != null}");
        if (EditorApplication.isPlaying && manager != null && manager.isActiveAndEnabled)
            Measure();
    }

    [MenuItem(MenuPath)]
    private static void Measure()
    {
        BattleShowPresentationManager manager = BattleShowPresentationManager.Instance;
        if (!EditorApplication.isPlaying || manager == null || !manager.isActiveAndEnabled)
        {
            EditorUtility.DisplayDialog("Floor Scan", "전투 씬을 Play한 뒤 실행하세요. 씬 파일은 저장하지 않습니다.", "확인");
            return;
        }

        Type type = typeof(BattleShowPresentationManager);
        FieldInfo baselineField = type.GetField("profileOriginalFloorScan", PrivateInstance);
        MethodInfo scanMethod = type.GetMethod("DecorateAllLiveFloor", PrivateInstance);
        if (baselineField == null || scanMethod == null)
        {
            Debug.LogError("[Harness] 비교 스위치 또는 탐색 메서드를 찾지 못했습니다. 측정하지 않았습니다.");
            return;
        }

        var scan = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), manager, scanMethod);
        var run = (BattleRunManager)type.GetField("runManager", PrivateInstance).GetValue(manager);
        var baseTemplate = (RoomBaseTemplate)type.GetField("baseTemplate", PrivateInstance).GetValue(manager);
        MapBlock[] blocks = UnityEngine.Object.FindObjectsByType<MapBlock>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Transform baseRoot = baseTemplate != null && baseTemplate.ActiveBase != null
            ? baseTemplate.ActiveBase.transform : null;
        Dictionary<int, VisualState> baseBefore = Capture(baseRoot);
        var blocksBefore = new Dictionary<int, VisualState>();
        for (int i = 0; i < blocks.Length; i++)
            Capture(blocks[i].transform, blocksBefore);

        bool originalSwitch = (bool)baselineField.GetValue(manager);
        UnityEngine.Random.State randomState = UnityEngine.Random.state;
        var baseline = new Measurement();
        var optimized = new Measurement();
        try
        {
            RunBatch(scan, baselineField, manager, true, WarmupScans);
            RunBatch(scan, baselineField, manager, false, WarmupScans);
            if (!Unchanged(baseBefore) || !Unchanged(blocksBefore) ||
                !SameHierarchy(baseRoot, blocks, baseBefore.Count + blocksBefore.Count))
            {
                Debug.LogWarning("[Harness] 준비 중 새 장식 또는 외형 변경이 발생해 비교를 취소했습니다. 장식이 완료된 같은 전투 상태에서 메뉴를 다시 실행하세요.");
                return;
            }

            // 한 프레임 안에서 AB/BA 순서를 번갈아 실행해 전투 상태와 대상 계층을 고정합니다.
            for (int batch = 0; batch < Batches; batch++)
            {
                if ((batch & 1) == 0)
                {
                    Add(ref baseline, RunBatch(scan, baselineField, manager, true, ScansPerBatch));
                    Add(ref optimized, RunBatch(scan, baselineField, manager, false, ScansPerBatch));
                }
                else
                {
                    Add(ref optimized, RunBatch(scan, baselineField, manager, false, ScansPerBatch));
                    Add(ref baseline, RunBatch(scan, baselineField, manager, true, ScansPerBatch));
                }
            }

            bool hierarchyPreserved = SameHierarchy(baseRoot, blocks, baseBefore.Count + blocksBefore.Count);
            bool basePreserved = Unchanged(baseBefore) && hierarchyPreserved;
            bool blocksPreserved = Unchanged(blocksBefore) && hierarchyPreserved;
            WriteReport(manager, run, blocks.Length, baseline, optimized,
                baseRoot != null && run != null && run.State == BattleRunState.RuleRoulette,
                basePreserved, blocksPreserved);
        }
        finally
        {
            baselineField.SetValue(manager, originalSwitch);
            UnityEngine.Random.state = randomState;
        }
    }

    private static Measurement RunBatch(Action<bool> scan, FieldInfo field,
        BattleShowPresentationManager manager, bool baseline, int count)
    {
        field.SetValue(manager, baseline);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < count; i++)
            scan(false);
        long ticks = Stopwatch.GetTimestamp() - start;
        return new Measurement { ticks = ticks, bytes = GC.GetAllocatedBytesForCurrentThread() - bytes };
    }

    private static void Add(ref Measurement total, Measurement batch)
    {
        total.ticks += batch.ticks;
        total.bytes += batch.bytes;
    }

    private static Dictionary<int, VisualState> Capture(Transform root)
    {
        var result = new Dictionary<int, VisualState>();
        Capture(root, result);
        return result;
    }

    private static void Capture(Transform root, Dictionary<int, VisualState> result)
    {
        if (root == null)
            return;
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform transform = transforms[i];
            transform.TryGetComponent(out SpriteRenderer renderer);
            result[transform.GetInstanceID()] = new VisualState
            {
                transform = transform, parent = transform.parent,
                position = transform.localPosition, rotation = transform.localRotation,
                scale = transform.localScale, active = transform.gameObject.activeSelf,
                renderer = renderer, hadRenderer = renderer != null,
                sprite = renderer != null ? renderer.sprite : null,
                color = renderer != null ? renderer.color : default,
                sortingLayer = renderer != null ? renderer.sortingLayerID : 0,
                sortingOrder = renderer != null ? renderer.sortingOrder : 0,
                rendererEnabled = renderer != null && renderer.enabled
            };
        }
    }

    private static bool Unchanged(Dictionary<int, VisualState> before)
    {
        foreach (VisualState state in before.Values)
        {
            Transform transform = state.transform;
            if (transform == null || transform.parent != state.parent ||
                !transform.localPosition.Equals(state.position) ||
                !transform.localRotation.Equals(state.rotation) ||
                !transform.localScale.Equals(state.scale) ||
                transform.gameObject.activeSelf != state.active)
                return false;
            if (state.hadRenderer && state.renderer == null)
                return false;
            if (state.renderer != null && (state.renderer.sprite != state.sprite ||
                !state.renderer.color.Equals(state.color) ||
                state.renderer.sortingLayerID != state.sortingLayer ||
                state.renderer.sortingOrder != state.sortingOrder ||
                state.renderer.enabled != state.rendererEnabled))
                return false;
        }
        return true;
    }

    private static bool SameHierarchy(Transform baseRoot, MapBlock[] blocks, int previousCount)
    {
        int count = baseRoot != null ? baseRoot.GetComponentsInChildren<Transform>(true).Length : 0;
        var blockTransforms = new HashSet<Transform>();
        for (int i = 0; i < blocks.Length; i++)
        {
            if (blocks[i] == null)
                return false;
            foreach (Transform child in blocks[i].GetComponentsInChildren<Transform>(true))
                blockTransforms.Add(child);
        }
        return count + blockTransforms.Count == previousCount;
    }

    private static void WriteReport(BattleShowPresentationManager manager, BattleRunManager run,
        int blockCount, Measurement baseline, Measurement optimized,
        bool roulette, bool basePreserved, bool blocksPreserved)
    {
        const double samples = Batches * ScansPerBatch;
        double baselineMs = baseline.ticks * 1000.0 / Stopwatch.Frequency / samples;
        double optimizedMs = optimized.ticks * 1000.0 / Stopwatch.Frequency / samples;
        double baselineBytes = baseline.bytes / samples;
        double optimizedBytes = optimized.bytes / samples;
        var report = new StringBuilder();
        report.AppendLine("# Floor Scan 비교 측정");
        report.AppendLine("UTC: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        report.AppendLine($"Unity: {Application.unityVersion}; CPU: {SystemInfo.processorType}");
        report.AppendLine($"Scene: {manager.gameObject.scene.path}; State: {run?.State}; Room: {run?.CurrentNode?.room?.name}");
        report.AppendLine($"Frame: {Time.frameCount}; MapBlock: {blockCount}; Template: {manager.ActiveFloorTemplate?.name}");
        report.AppendLine($"Time scale: {Time.timeScale}; Resolution: {Screen.width}x{Screen.height}");
        report.AppendLine($"조건: 같은 메인 스레드·프레임·계층에서 준비 {WarmupScans}회/경로 후 AB/BA 교대, {samples}회/경로.");
        report.AppendLine("범위: 장식 완료 후 DecorateAllLiveFloor(false)의 반복 호출. CPU는 Stopwatch 경과 시간, GC Alloc은 해당 메인 스레드의 관리 할당 바이트. 전투 전체 프레임·GPU·새 블록 생성 비용은 제외.");
        report.AppendLine();
        report.AppendLine("| 경로 | CPU 평균 ms/scan | GC Alloc 평균 B/scan |");
        report.AppendLine("| --- | ---: | ---: |");
        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "| 수정 전 | {0:F6} | {1:F2} |", baselineMs, baselineBytes));
        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "| 수정 후 | {0:F6} | {1:F2} |", optimizedMs, optimizedBytes));
        report.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "평균 감소량: CPU {0:F6} ms/scan, GC Alloc {1:F2} B/scan.",
            baselineMs - optimizedMs, baselineBytes - optimizedBytes));
        report.AppendLine($"비교 유효성 (측정 전후 기존 외형 불변): {(basePreserved && blocksPreserved ? "통과" : "실패 — 개선 근거로 사용할 수 없음")}");
        report.AppendLine($"CPU 평균 감소: {optimizedMs < baselineMs}; GC Alloc 평균 감소: {optimizedBytes < baselineBytes}.");
        report.AppendLine($"기존 바닥·손잡이의 스프라이트/색상/계층/로컬 배치 보존: {(blockCount == 0 ? "미검증 (MapBlock 없음)" : (blocksPreserved ? "통과" : "실패"))}");
        report.AppendLine($"룰렛 중 기존 베이스 보존: {(roulette ? (basePreserved ? "통과" : "실패") : "미검증 (현재 룰렛 상태가 아님)")}");
        report.AppendLine("새 블록 장식: 미검증 (장식 완료 상태의 비교만 실행)");
        report.AppendLine("도킹 이벤트 전후 배치 유지: 미검증 (도킹 이벤트를 실행하지 않음)");
        report.AppendLine("명시적 Refresh: 미검증 (Refresh를 실행하지 않음)");
        report.AppendLine("영향: BattleShowPresentationManager.cs의 자동 탐색 중 기존 장식 대상 제외 및 점유 계산 생략. SO·공개 API·풀링 변경 없음. Assets/Editor/HarnessFloorScanComparison.cs는 에디터 측정 전용.");
        report.AppendLine("한계: 이 결과는 단일 고정 상태의 비교이며, 전투 전체 성능 개선이나 미검증 동작의 통과를 뜻하지 않음.");
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../HarnessReports/FloorScanComparison.md"));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.AppendAllText(path, report.ToString() + Environment.NewLine, new UTF8Encoding(false));
        if (File.Exists(RequestPath))
            File.Delete(RequestPath);
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        Debug.Log("[Harness] Floor Scan 측정 완료: " + path + "\n" + report);
    }
}
