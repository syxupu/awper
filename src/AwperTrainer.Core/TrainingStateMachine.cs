namespace AwperTrainer.Core;

public enum TrainingState { IdleReady, Prepare, Countdown, RandomDelay, BotMoving, EndpointPause, Finish, Reset, Aborted }
public enum FinishReason { BotKilled, ReachedEnd, PlayerKilled, Timeout, Stuck, Aborted, RuntimeFailure }

public abstract record TrainingAction;
public sealed record PrepareRoundAction : TrainingAction;
public sealed record BeginCountdownAction(double Seconds) : TrainingAction;
public sealed record BeginRandomDelayAction(double Seconds) : TrainingAction;
public sealed record BeginMotionAction(MotionSegment Segment) : TrainingAction;
public sealed record StopMotionAction : TrainingAction;
public sealed record FinishRoundAction(FinishReason Reason) : TrainingAction;
public sealed record ResetRoundAction(FinishReason Reason) : TrainingAction;

public sealed class TrainingStateMachine
{
    private AwperProfile? _profile;
    private DeterministicRandom? _random;
    private IReadOnlyList<MotionSegment> _route = [];
    private int _segmentIndex;
    private double _deadline;
    private double _runDeadline;
    private FinishReason _finishReason;

    public TrainingState State { get; private set; } = TrainingState.IdleReady;
    public ulong Seed { get; private set; }
    public int JiggleCount { get; private set; }
    public MotionSegment? CurrentSegment => State == TrainingState.BotMoving && _segmentIndex < _route.Count ? _route[_segmentIndex] : null;

    public IReadOnlyList<TrainingAction> Start(AwperProfile profile, double nowSeconds, ulong seed)
    {
        if (State != TrainingState.IdleReady) throw new InvalidOperationException($"Cannot start from {State}.");
        _profile = profile;
        _random = new(seed);
        Seed = seed;
        JiggleCount = profile.Training.Mode == TrainingMode.JiggleThenPeek
            ? _random.Inclusive(profile.Training.JiggleCountMin, profile.Training.JiggleCountMax) : 0;
        _route = MotionRouteBuilder.Build(profile, JiggleCount);
        _segmentIndex = 0;
        State = TrainingState.Prepare;
        return [new PrepareRoundAction()];
    }

    public IReadOnlyList<TrainingAction> Prepared(double nowSeconds)
    {
        Require(TrainingState.Prepare);
        State = TrainingState.Countdown;
        _deadline = nowSeconds + _profile!.Training.CountdownSeconds;
        return [new BeginCountdownAction(_profile.Training.CountdownSeconds)];
    }

    public IReadOnlyList<TrainingAction> Tick(double nowSeconds, bool playerAlive = true, bool botAlive = true, bool stuck = false)
    {
        if (!playerAlive && State is not (TrainingState.IdleReady or TrainingState.Reset or TrainingState.Aborted))
            return Finish(nowSeconds, FinishReason.PlayerKilled, immediate: true);
        if (!botAlive && State is TrainingState.Countdown or TrainingState.RandomDelay or TrainingState.BotMoving or TrainingState.EndpointPause)
            return Finish(nowSeconds, State is TrainingState.BotMoving or TrainingState.EndpointPause
                ? FinishReason.BotKilled : FinishReason.RuntimeFailure, immediate: State is not (TrainingState.BotMoving or TrainingState.EndpointPause));
        if (stuck && State == TrainingState.BotMoving)
            return Finish(nowSeconds, FinishReason.Stuck, immediate: true);

        if (State == TrainingState.Countdown && nowSeconds >= _deadline)
        {
            State = TrainingState.RandomDelay;
            var delay = _random!.Between(_profile!.Training.RandomDelayMinSeconds, _profile.Training.RandomDelayMaxSeconds);
            _deadline = nowSeconds + delay;
            return [new BeginRandomDelayAction(delay)];
        }
        if (State == TrainingState.RandomDelay && nowSeconds >= _deadline)
        {
            State = TrainingState.BotMoving;
            _runDeadline = nowSeconds + _profile!.Training.RunTimeoutSeconds;
            return [new BeginMotionAction(_route[0])];
        }
        if (State == TrainingState.EndpointPause && nowSeconds >= _deadline)
        {
            _segmentIndex++;
            State = TrainingState.BotMoving;
            return [new BeginMotionAction(_route[_segmentIndex])];
        }
        if (State == TrainingState.BotMoving && nowSeconds >= _runDeadline)
            return Finish(nowSeconds, FinishReason.Timeout, immediate: true);
        if (State == TrainingState.Finish && nowSeconds >= _deadline)
        {
            State = TrainingState.Reset;
            return [new ResetRoundAction(_finishReason)];
        }
        return [];
    }

    public IReadOnlyList<TrainingAction> TargetReached(double nowSeconds)
    {
        Require(TrainingState.BotMoving);
        var current = _route[_segmentIndex];
        if (_segmentIndex == _route.Count - 1) return Finish(nowSeconds, FinishReason.ReachedEnd);
        if (current.PauseAfter)
        {
            State = TrainingState.EndpointPause;
            var pause = _random!.Between(_profile!.Training.JiggleEndpointPauseMinSeconds, _profile.Training.JiggleEndpointPauseMaxSeconds);
            _deadline = nowSeconds + pause;
            return [new StopMotionAction()];
        }
        _segmentIndex++;
        return [new BeginMotionAction(_route[_segmentIndex])];
    }

    public IReadOnlyList<TrainingAction> Abort(double nowSeconds, FinishReason reason = FinishReason.Aborted)
    {
        if (State == TrainingState.IdleReady) return [];
        State = TrainingState.Aborted;
        _finishReason = reason;
        return [new StopMotionAction(), new ResetRoundAction(reason)];
    }

    public void ResetCompleted()
    {
        if (State is not (TrainingState.Reset or TrainingState.Aborted)) throw new InvalidOperationException($"Cannot complete reset from {State}.");
        State = TrainingState.IdleReady;
        _profile = null;
        _route = [];
    }

    private IReadOnlyList<TrainingAction> Finish(double nowSeconds, FinishReason reason, bool immediate = false)
    {
        _finishReason = reason;
        if (immediate || _profile!.Training.FinishFeedbackSeconds <= 0)
        {
            State = TrainingState.Reset;
            return [new StopMotionAction(), new FinishRoundAction(reason), new ResetRoundAction(reason)];
        }
        State = TrainingState.Finish;
        _deadline = nowSeconds + _profile.Training.FinishFeedbackSeconds;
        return [new StopMotionAction(), new FinishRoundAction(reason)];
    }

    private void Require(TrainingState expected)
    {
        if (State != expected) throw new InvalidOperationException($"Expected {expected}, got {State}.");
    }
}
