using AwperTrainer.Core;
using Xunit;

namespace AwperTrainer.Core.Tests;

public sealed class CoreTests
{
    [Fact]
    public void SeasonFivePolicyContainsExpectedSevenMaps()
    {
        var policy = new MapPolicy();
        Assert.Equal(7, policy.AllowedMaps.Count);
        Assert.Equal([
            "de_dust2", "de_inferno", "de_mirage", "de_anubis", "de_ancient", "de_nuke", "de_cache"
        ], policy.AllowedMaps);
        Assert.True(policy.IsAllowed("DE_CACHE"));
        Assert.False(policy.IsAllowed("de_overpass"));
        Assert.Equal("de_mirage", MapPolicy.NormalizeAlias("Mirage"));
        Assert.Equal("de_nuke", MapPolicy.NormalizeAlias("DE_NUKE"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("angle.json")]
    [InlineData("")]
    public void ProfileNamesRejectUnsafeInput(string value)
        => Assert.Throws<ArgumentException>(() => ProfileNames.Normalize(value));

    [Fact]
    public void WorldDirectionProjectionUsesBotLocalAxes()
    {
        var eastWhileFacingNorth = MotionMath.ProjectWorldDirection(new(0, 0, 0), new(100, 0, 0), 90);
        Assert.Equal(0, eastWhileFacingNorth.Forward, 4);
        Assert.Equal(-1, eastWhileFacingNorth.Left, 4);
        var northWhileFacingNorth = MotionMath.ProjectWorldDirection(new(0, 0, 0), new(0, 100, 0), 90);
        Assert.Equal(1, northWhileFacingNorth.Forward, 4);
        Assert.Equal(0, northWhileFacingNorth.Left, 4);
    }

    [Fact]
    public void PreviewLookAtUsesPlayerEyeAndBotEyePositions()
    {
        var playerEye = new Vec3(0, 0, 64);
        var botEye = MotionMath.EyePosition(new Vec3(100, 100, 0), Stance.Standing);
        var angles = MotionMath.LookAt(playerEye, botEye);

        Assert.Equal(0, angles.Pitch, 4);
        Assert.Equal(45, angles.Yaw, 4);
        Assert.Equal(0, angles.Roll, 4);
        Assert.Equal(46, MotionMath.EyePosition(Vec3.Zero, Stance.Crouching).Z);
    }

    [Fact]
    public void JiggleRouteAlwaysReturnsToStartBeforeFinalPeek()
    {
        var profile = Profile(TrainingMode.JiggleThenPeek);
        var route = MotionRouteBuilder.Build(profile, 2);
        Assert.Equal(["jiggle-out-1", "jiggle-back-1", "jiggle-out-2", "jiggle-back-2", "final-peek"], route.Select(x => x.Label));
        Assert.Equal(profile.BotPath.Start, route[^2].Target);
        Assert.Equal(profile.BotPath.End, route[^1].Target);
    }

    [Fact]
    public void StateMachineUsesDeterministicJiggleAndDelay()
    {
        var left = RunInitialSequence(12345);
        var right = RunInitialSequence(12345);
        Assert.Equal(left, right);
        Assert.InRange(left.Jiggles, 1, 4);
        Assert.InRange(left.Delay, 0.5, 3.0);
    }

    [Fact]
    public void DirectRoundFinishesOnceAndReturnsToIdle()
    {
        var machine = new TrainingStateMachine();
        machine.Start(Profile(TrainingMode.DirectPeek), 0, 77);
        machine.Prepared(0);
        machine.Tick(3.0);
        var motion = machine.Tick(6.0);
        Assert.IsType<BeginMotionAction>(Assert.Single(motion));

        var finish = machine.TargetReached(6.1);
        Assert.Collection(finish,
            action => Assert.IsType<StopMotionAction>(action),
            action => Assert.Equal(FinishReason.ReachedEnd, Assert.IsType<FinishRoundAction>(action).Reason));
        Assert.Empty(machine.Tick(6.15, botAlive: false));

        var reset = Assert.IsType<ResetRoundAction>(Assert.Single(machine.Tick(6.31, botAlive: false)));
        Assert.Equal(FinishReason.ReachedEnd, reset.Reason);
        machine.ResetCompleted();
        Assert.Equal(TrainingState.IdleReady, machine.State);
    }

    [Fact]
    public void BotLossDuringCountdownFailsImmediately()
    {
        var machine = new TrainingStateMachine();
        machine.Start(Profile(TrainingMode.DirectPeek), 0, 1);
        machine.Prepared(0);
        var actions = machine.Tick(1, botAlive: false);
        Assert.Equal(TrainingState.Reset, machine.State);
        Assert.Contains(actions, x => x is ResetRoundAction { Reason: FinishReason.RuntimeFailure });
    }


    [Fact]
    public void DraftComputesDefaultFacingTowardPlayer()
    {
        var draft = new ProfileDraft("DE_MIRAGE");
        draft.SetEditAnchor(new(new(0, 0, 0), new(0, 0, 0)));
        draft.SetPlayerAnchor(new(new(0, 100, 0), new(0, 100, 64), new(0, 0, 0)));
        draft.SetBotStart(new(0, 0, 0));
        draft.SetBotEnd(new(100, 0, 0));
        var profile = draft.Build("default-facing");
        Assert.Equal(90, profile.BotPath.FacingYaw, 4);
        Assert.Equal("de_mirage", profile.MapName);
    }

    [Fact]
    public void DraftTargetSpeedIsConfigurableWithinAk47Range()
    {
        var draft = new ProfileDraft("de_mirage");
        draft.SetTargetSpeed(180);
        Assert.Equal(180, draft.Training.TargetSpeed);
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.SetTargetSpeed(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.SetTargetSpeed(216));
    }

    [Fact]
    public void OneRoundSpeedVariantDoesNotMutateTheLoadedProfile()
    {
        var loaded = Profile(TrainingMode.DirectPeek);

        var runtime = ProfileVariants.WithSpeed(loaded, 150);

        Assert.Equal(TrainingSettings.MaximumAk47Speed, loaded.Training.TargetSpeed);
        Assert.Equal(150, runtime.Training.TargetSpeed);
        Assert.Equal(loaded.ProfileName, runtime.ProfileName);
        Assert.Equal(loaded.BotPath, runtime.BotPath);
    }

    [Fact]
    public void ProfileCopyChangesOnlyNameAndSpeed()
    {
        var source = Profile(TrainingMode.JiggleThenPeek) with { ValidatedMapFingerprint = "fingerprint" };

        var copy = ProfileVariants.CopyWithSpeed(source, "slower-copy", 180);
        var expected = source with
        {
            ProfileName = "slower-copy",
            Training = source.Training with { TargetSpeed = 180 }
        };

        Assert.Equal(expected, copy);
        Assert.Equal(TrainingSettings.MaximumAk47Speed, source.Training.TargetSpeed);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProfileVariants.WithSpeed(source, 216));
    }

    [Fact]
    public void MapPolicyAcceptsAConfiguredWhitelist()
    {
        var policy = new MapPolicy(["DE_TRAIN"]);
        Assert.True(policy.IsAllowed("de_train"));
        Assert.False(policy.IsAllowed("de_mirage"));
    }

    [Fact]
    public void ValidatorRejectsMapMismatchAndInvalidSettings()
    {
        var profile = Profile(TrainingMode.DirectPeek) with
        {
            Training = new TrainingSettings { RandomDelayMinSeconds = 3, RandomDelayMaxSeconds = 1 }
        };
        var result = new ProfileValidator(new FakeWorldProbe()).Validate(profile, "de_nuke");
        Assert.Contains(result.Issues, x => x.Code == "map.mismatch");
        Assert.Contains(result.Issues, x => x.Code == "training.invalid");
    }

    [Fact]
    public void OneThousandSimulatedRoundsReturnCleanlyToIdle()
    {
        var machine = new TrainingStateMachine();
        var now = 0d;
        for (var round = 0; round < 1_000; round++)
        {
            var mode = round % 2 == 0 ? TrainingMode.DirectPeek : TrainingMode.JiggleThenPeek;
            machine.Start(Profile(mode), now, (ulong)round + 1);
            machine.Prepared(now);
            now += 3.001;
            var delay = Assert.IsType<BeginRandomDelayAction>(Assert.Single(machine.Tick(now))).Seconds;
            now += delay + 0.001;
            Assert.IsType<BeginMotionAction>(Assert.Single(machine.Tick(now)));

            while (machine.State is TrainingState.BotMoving or TrainingState.EndpointPause)
            {
                if (machine.State == TrainingState.BotMoving)
                {
                    var actions = machine.TargetReached(now);
                    Assert.NotEmpty(actions);
                }
                else
                {
                    now += 0.201;
                    Assert.IsType<BeginMotionAction>(Assert.Single(machine.Tick(now)));
                }
            }

            Assert.Equal(TrainingState.Finish, machine.State);
            now += 0.201;
            Assert.IsType<ResetRoundAction>(Assert.Single(machine.Tick(now)));
            machine.ResetCompleted();
            Assert.Equal(TrainingState.IdleReady, machine.State);
            now += 0.01;
        }
    }

    [Fact]
    public async Task RepositoryRoundTripsAndContainsPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "awper-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new ProfileRepository(root);
            var profile = Profile(TrainingMode.DirectPeek);
            await repository.SaveAsync(profile);
            var loaded = await repository.LoadAsync(profile.MapName, profile.ProfileName);
            Assert.Equal(profile, loaded);
            Assert.Equal([profile.ProfileName], repository.List(profile.MapName));
            Assert.StartsWith(Path.GetFullPath(root), repository.Resolve(profile.MapName, profile.ProfileName), StringComparison.OrdinalIgnoreCase);
            Assert.True(repository.Delete(profile.MapName, profile.ProfileName));
            Assert.False(repository.Delete(profile.MapName, profile.ProfileName));
            Assert.Empty(repository.List(profile.MapName));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ValidatorRejectsBlockedHullAndAllowsLosWarnings()
    {
        var probe = new FakeWorldProbe { Clear = false, Visible = false };
        var result = new ProfileValidator(probe).Validate(Profile(TrainingMode.DirectPeek), "de_mirage");
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, x => x.Code == "bot.start-end.blocked" && x.Severity == ValidationSeverity.Error);
        Assert.Contains(result.Issues, x => x.Code == "los.start" && x.Severity == ValidationSeverity.Warning);
    }


    private static (int Jiggles, double Delay) RunInitialSequence(ulong seed)
    {
        var machine = new TrainingStateMachine();
        machine.Start(Profile(TrainingMode.JiggleThenPeek), 0, seed);
        machine.Prepared(0);
        var actions = machine.Tick(3.0);
        var delay = Assert.IsType<BeginRandomDelayAction>(Assert.Single(actions)).Seconds;
        return (machine.JiggleCount, delay);
    }

    private static AwperProfile Profile(TrainingMode mode) => new()
    {
        MapName = "de_mirage",
        ProfileName = "test-angle",
        EditAnchor = new(new(0, 0, 0), new(0, 0, 0)),
        PlayerAnchor = new(new(0, 100, 0), new(0, 100, 64), new(0, -90, 0)),
        BotPath = new(new(0, 0, 0), new(100, 0, 0), new(-32, 0, 0), 90),
        Training = new() { Mode = mode }
    };

    private sealed class FakeWorldProbe : IWorldProbe
    {
        public bool Clear { get; init; } = true;
        public bool Visible { get; init; } = true;
        public string? CurrentMapFingerprint => "test";
        public bool CanFitStandingPlayer(Vec3 point) => true;
        public bool HasStandableGround(Vec3 point, out float normalZ) { normalZ = 1; return true; }
        public bool IsStandingHullPathClear(Vec3 from, Vec3 to) => Clear;
        public bool HasLineOfSight(Vec3 from, Vec3 to) => Visible;
    }
}
