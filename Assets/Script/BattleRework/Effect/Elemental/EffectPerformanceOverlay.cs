using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

[DisallowMultipleComponent]
public sealed class EffectPerformanceOverlay : MonoBehaviour
{
    [Min(0.1f)] public float refreshInterval = 0.25f;
    [Min(200f)] public float width = 410f;
    [Range(10, 28)] public int fontSize = 14;
    [Min(0f)] public float margin = 12f;
    public Color backgroundColor = new(0f, 0f, 0f, 0.85f);
    public Color textColor = Color.white;
    public bool drawStandalone = true;
    [Min(1f)] public float targetFps = 60f;
    [Min(1f)] public float warningBudgetRatio = 1.1f;
    [Min(1f)] public float criticalBudgetRatio = 2f;
    public Color normalStatusColor = Color.green;
    public Color warningStatusColor = new(1f, 0.65f, 0.15f);
    public Color criticalStatusColor = Color.red;

    public string StatsText => content.text;
    public string StatusText { get; private set; } = "측정 중...";
    public Color StatusColor { get; private set; } = Color.white;
    private readonly StringBuilder text = new(768);
    private readonly GUIContent content = new();
    private readonly FrameTiming[] timings = new FrameTiming[1];
    private ProfilerRecorder calculation, upload, mainThread, gcAlloc, batches;
    private GUIStyle labelStyle;
    private bool captureTimings;
    private double elapsed, calculationTotal, uploadTotal, mainThreadTotal;
    private double calculationPeak, uploadPeak, mainThreadPeak;
    private float framePeak;
    private int frames, calculationFrames, uploadFrames, mainThreadFrames;
    private long allocatedBytes, batchCount;
    private bool hasGc, hasBatches;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private System.Diagnostics.Process process;
    private double processSeconds, processTimestamp;
#endif

    private void OnEnable()
    {
        calculation = ProfilerRecorder.StartNew(EffectBlobRenderer.CalculationMarker);
        upload = ProfilerRecorder.StartNew(EffectBlobRenderer.UploadMarker);
        mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
        gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        captureTimings = FrameTimingManager.IsFeatureEnabled();
        content.text = "Performance: collecting...";
        StatusText = "측정 중...";
        StatusColor = Color.white;
        ResetInterval();
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        try
        {
            process = System.Diagnostics.Process.GetCurrentProcess();
            processSeconds = process.TotalProcessorTime.TotalSeconds;
            processTimestamp = Time.realtimeSinceStartupAsDouble;
        }
        catch (System.Exception)
        {
            process?.Dispose();
            process = null;
        }
#endif
    }

    private void Update()
    {
        if (captureTimings) FrameTimingManager.CaptureFrameTimings();
        float delta = Time.unscaledDeltaTime;
        if (delta <= 0f) return;
        elapsed += delta;
        frames++;
        framePeak = Mathf.Max(framePeak, delta);
        Accumulate(calculation, ref calculationTotal, ref calculationPeak, ref calculationFrames);
        Accumulate(upload, ref uploadTotal, ref uploadPeak, ref uploadFrames);
        Accumulate(mainThread, ref mainThreadTotal, ref mainThreadPeak, ref mainThreadFrames);
        if (gcAlloc.Valid && gcAlloc.Count > 0)
        {
            allocatedBytes += gcAlloc.LastValue;
            hasGc = true;
        }
        if (batches.Valid && batches.Count > 0)
        {
            batchCount = batches.LastValue;
            hasBatches = true;
        }
        if (elapsed < EffectNumbers.Positive(refreshInterval, 0.25f)) return;
        RefreshText();
        ResetInterval();
    }

    private static void Accumulate(ProfilerRecorder recorder, ref double total, ref double peak, ref int count)
    {
        if (!recorder.Valid || recorder.Count == 0) return;
        double milliseconds = recorder.LastValue * 0.000001;
        total += milliseconds;
        peak = System.Math.Max(peak, milliseconds);
        count++;
    }

    private void RefreshText()
    {
        EffectBlobWorld.DebugCounts counts = EffectBlobWorld.CaptureDebugCounts();
        double frameMilliseconds = elapsed * 1000d / frames;
        double budget = 1000d / EffectNumbers.Positive(targetFps, 60f);
        double warning = budget * Mathf.Max(1f, EffectNumbers.Positive(warningBudgetRatio, 1.1f));
        double critical = budget * Mathf.Max(1f, Mathf.Max(EffectNumbers.Positive(warningBudgetRatio, 1.1f),
            EffectNumbers.Positive(criticalBudgetRatio, 2f)));
        double peakMilliseconds = framePeak * 1000d;
        if (frameMilliseconds >= critical)
        {
            StatusText = "심각: 평균 프레임 시간이 크게 초과합니다.";
            StatusColor = criticalStatusColor;
        }
        else if (frameMilliseconds > warning || peakMilliseconds >= critical)
        {
            StatusText = frameMilliseconds > warning ? "주의: 목표 FPS를 유지하지 못합니다." : "주의: 순간적인 프레임 끊김이 있습니다.";
            StatusColor = warningStatusColor;
        }
        else
        {
            StatusText = "원활: 목표 프레임 시간 이내입니다.";
            StatusColor = normalStatusColor;
        }
        text.Clear();
        text.Append("목표 ").Append(EffectNumbers.Positive(targetFps, 60f).ToString("F0")).Append(" FPS | 예산 ");
        text.Append(budget.ToString("F2")).Append(" ms | 평균 ").Append((frameMilliseconds / budget * 100d).ToString("F0")).AppendLine("%");
        text.Append("주의 > ").Append(warning.ToString("F2")).Append(" ms | 심각 >= ").Append(critical.ToString("F2")).AppendLine(" ms");
        text.Append("FPS ").Append((frames / elapsed).ToString("F1"));
        text.Append(" | frame ").Append((elapsed * 1000d / frames).ToString("F2"));
        text.Append(" ms | peak ").Append((framePeak * 1000f).ToString("F2")).AppendLine(" ms");
        text.Append(Application.isEditor ? "Editor CPU " : "Player CPU ");
        double cpu = ProcessCpuPercent();
        text.Append(cpu >= 0d ? cpu.ToString("F1") + "% (all cores)" : "--").AppendLine();
        AppendTiming("Main thread", mainThreadTotal, mainThreadPeak, mainThreadFrames);
        text.Append("GPU frame ");
        if (captureTimings && FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0d)
            text.Append(timings[0].gpuFrameTime.ToString("F2")).AppendLine(" ms");
        else text.AppendLine("-- (unavailable)");
        text.Append("Unity memory ").Append((Profiler.GetTotalAllocatedMemoryLong() / 1048576d).ToString("F1"));
        text.Append(" MiB | managed ").Append((Profiler.GetMonoUsedSizeLong() / 1048576d).ToString("F1")).AppendLine(" MiB");
        text.Append("GC alloc/frame ").Append(hasGc ? (allocatedBytes / (1024d * frames)).ToString("F1") + " KiB" : "--");
        text.Append(" | batches ").Append(hasBatches ? batchCount.ToString() : "--").AppendLine();
        text.AppendLine("Merged / fire visuals (ending)");
        text.Append("Fire ").Append(counts.fire).Append(" (").Append(counts.endingFire).Append(") | Water ");
        text.Append(counts.liquid).Append(" (").Append(counts.endingLiquid).Append(") | Fog ");
        text.Append(counts.gas).Append(" (").Append(counts.endingGas).AppendLine(")");
        text.Append("Visible tiles ").Append(counts.tiles).Append(" | tile samples ").Append(counts.samples).AppendLine();
        AppendTiming("Blob calculate", calculationTotal, calculationPeak, calculationFrames);
        AppendTiming("Texture upload", uploadTotal, uploadPeak, uploadFrames);
        text.Append("avg / peak per frame; tile samples include duplicates");
        content.text = text.ToString();
    }

    private void AppendTiming(string title, double total, double peak, int count)
    {
        text.Append(title).Append(' ');
        if (count == 0) text.AppendLine("--");
        else text.Append((total / count).ToString("F2")).Append(" / ").Append(peak.ToString("F2")).AppendLine(" ms");
    }

    private double ProcessCpuPercent()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (process != null)
        {
            try
            {
                double now = Time.realtimeSinceStartupAsDouble;
                double seconds = process.TotalProcessorTime.TotalSeconds;
                double interval = now - processTimestamp;
                double percent = interval > 0d ? (seconds - processSeconds) * 100d /
                    (interval * System.Math.Max(1, SystemInfo.processorCount)) : 0d;
                processSeconds = seconds;
                processTimestamp = now;
                return System.Math.Max(0d, System.Math.Min(100d, percent));
            }
            catch (System.Exception)
            {
                process.Dispose();
                process = null;
            }
        }
#endif
        return -1d;
    }

    private void ResetInterval()
    {
        elapsed = calculationTotal = uploadTotal = mainThreadTotal = 0d;
        calculationPeak = uploadPeak = mainThreadPeak = 0d;
        framePeak = 0f;
        frames = calculationFrames = uploadFrames = mainThreadFrames = 0;
        allocatedBytes = batchCount = 0;
        hasGc = hasBatches = false;
    }

    private void OnGUI()
    {
        if (!drawStandalone || !Application.isPlaying || Event.current.type != EventType.Repaint) return;
        labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = true };
        labelStyle.fontSize = fontSize;
        labelStyle.normal.textColor = textColor;
        float edge = Mathf.Max(0f, margin);
        float panelWidth = Mathf.Min(Mathf.Max(200f, width), Mathf.Max(1f, Screen.width - edge * 2f));
        float innerWidth = Mathf.Max(1f, panelWidth - 20f);
        float height = labelStyle.CalcHeight(content, innerWidth) + 20f;
        Rect panel = new(Screen.width - edge - panelWidth, edge, panelWidth, height);
        Color previousColor = GUI.color;
        int previousDepth = GUI.depth;
        GUI.depth = -1000;
        GUI.color = backgroundColor;
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(panel.x + 10f, panel.y + 10f, innerWidth, height - 20f), content, labelStyle);
        GUI.color = previousColor;
        GUI.depth = previousDepth;
    }

    private void OnDisable()
    {
        calculation.Dispose();
        upload.Dispose();
        mainThread.Dispose();
        gcAlloc.Dispose();
        batches.Dispose();
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        process?.Dispose();
        process = null;
#endif
    }
}
