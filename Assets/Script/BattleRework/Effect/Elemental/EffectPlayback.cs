using System;

public sealed class EffectPlayback
{
    private double introLength, idleLength, endLength, idleBudget, phaseElapsed;
    private bool stopAfterIntro;
    private EffectIntroStop introStop;
    public EffectPhase Phase { get; private set; }
    public int RunId { get; private set; }
    public bool IsPlaying => Phase == EffectPhase.Intro || Phase == EffectPhase.Idle || Phase == EffectPhase.End;
    public double Elapsed { get; private set; }
    public double PhaseElapsed => phaseElapsed;
    public double PhaseProgress => PhaseLength > 0d && !double.IsInfinity(PhaseLength)
        ? Math.Min(1d, phaseElapsed / PhaseLength) : 0d;
    public double Remaining => IsPlaying ? Math.Max(0d, PhaseLength - phaseElapsed) +
        (Phase == EffectPhase.Intro ? idleBudget + endLength : Phase == EffectPhase.Idle ? endLength : 0d) : 0d;
    private double PhaseLength => Phase == EffectPhase.Intro ? introLength : Phase == EffectPhase.Idle ? idleBudget : endLength;
    public event Action<int, EffectPhase> PhaseStarted;
    public event Action<int> Finished;

    public void Play(EffectDefinitionSO definition, float duration,
        Action<int, EffectPhase> phaseStarted = null, Action<int> finished = null, bool startAtEnd = false)
    {
        Reset();
        int run = RunId;
        PhaseStarted = phaseStarted;
        Finished = finished;
        introLength = definition.GetFrames(EffectPhase.Intro).Length / (double)definition.GetFps(EffectPhase.Intro);
        idleLength = definition.GetFrames(EffectPhase.Idle).Length / (double)definition.GetFps(EffectPhase.Idle);
        endLength = definition.GetFrames(EffectPhase.End).Length / (double)definition.GetFps(EffectPhase.End);
        introStop = definition.introStop;
        if (startAtEnd)
        {
            Enter(EffectPhase.End);
            if (run == RunId) Tick(0d);
            return;
        }
        bool infinite = duration == -1f;
        double life = EffectNumbers.NonNegative(duration);
        double fixedLength = introLength + endLength;
        if (!infinite && life < fixedLength)
        {
            if (definition.shortLife == EffectShortLife.EndOnly)
            {
                introLength = 0d;
                endLength = endLength > 0d ? life : 0d;
            }
            else if (fixedLength > 0d)
            {
                introLength *= life / fixedLength;
                endLength *= life / fixedLength;
            }
        }
        else if (infinite || idleLength > 0d)
        {
            double budget = life - fixedLength;
            if (infinite) idleBudget = double.PositiveInfinity;
            else if (budget >= idleLength)
                idleBudget = definition.idleEnd == EffectIdleEnd.LoopBoundary
                    ? Math.Ceiling(budget / idleLength) * idleLength : budget;
        }
        Enter(EffectPhase.Intro);
        if (run == RunId) Tick(0d);
    }

    public void Stop()
    {
        if (!IsPlaying || Phase == EffectPhase.End || stopAfterIntro) return;
        if (Phase == EffectPhase.Intro && introStop == EffectIntroStop.AfterIntro)
        {
            stopAfterIntro = true;
            idleBudget = 0d;
            return;
        }
        idleBudget = 0d;
        int run = RunId;
        Enter(EffectPhase.End);
        if (run == RunId) Tick(0d);
    }

    public void Tick(double deltaTime)
    {
        if (double.IsNaN(deltaTime) || double.IsInfinity(deltaTime) || deltaTime < 0d) return;
        int run = RunId;
        for (int transitions = 0; transitions < 4 && IsPlaying && run == RunId; transitions++)
        {
            double step = Math.Min(deltaTime, Math.Max(0d, PhaseLength - phaseElapsed));
            phaseElapsed += step;
            Elapsed += step;
            deltaTime -= step;
            if (phaseElapsed < PhaseLength) return;
            if (Phase == EffectPhase.Intro) Enter(stopAfterIntro ? EffectPhase.End : EffectPhase.Idle);
            else if (Phase == EffectPhase.Idle) Enter(EffectPhase.End);
            else
            {
                Phase = EffectPhase.Finished;
                phaseElapsed = 0d;
                Finished?.Invoke(run);
                return;
            }
        }
    }

    public int FrameIndex(int count, double delay = 0d)
    {
        if (count <= 1) return 0;
        double time = Math.Max(0d, phaseElapsed - delay);
        double progress = Phase == EffectPhase.Idle
            ? (idleLength > 0d ? time % idleLength / idleLength : 0d)
            : (PhaseLength > 0d ? time / PhaseLength : 1d);
        return Math.Min(count - 1, Math.Max(0, (int)(progress * count)));
    }

    private void Enter(EffectPhase phase)
    {
        Phase = phase;
        phaseElapsed = 0d;
        if (PhaseLength > 0d) PhaseStarted?.Invoke(RunId, phase);
    }

    public void Reset()
    {
        unchecked { RunId++; }
        Phase = EffectPhase.None;
        Elapsed = phaseElapsed = introLength = idleLength = endLength = idleBudget = 0d;
        stopAfterIntro = false;
        PhaseStarted = null;
        Finished = null;
    }
}
