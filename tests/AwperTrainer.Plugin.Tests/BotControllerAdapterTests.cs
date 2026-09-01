using AwperTrainer.Plugin;
using AwperTrainer.Core;
using BotControllerApi;
using System.Runtime.InteropServices;
using Xunit;

namespace AwperTrainer.Plugin.Tests;

public sealed class BotControllerAdapterTests
{
    [Fact]
    public void MotionTelemetryWritesInvariantCsvAndClosesTheFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "awper-telemetry-" + Guid.NewGuid().ToString("N"));
        try
        {
            var profile = new AwperProfile
            {
                MapName = "de_mirage",
                ProfileName = "telemetry",
                EditAnchor = new(Vec3.Zero, new(0, 0, 0)),
                PlayerAnchor = new(Vec3.Zero, new(0, 0, 64), new(0, 0, 0)),
                BotPath = new(Vec3.Zero, new(100, 0, 0), null, 90)
            };
            string path;
            using (var telemetry = MotionTelemetry.Create(root, 7, 42, profile))
            {
                path = telemetry.Path;
                telemetry.Write(7, 42, 1.25, TrainingState.BotMoving, "final-peek",
                    new(1.5f, 2.5f, 3.5f), new(100, 0, 0), 215, 98.5f, true);
            }
            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.Contains("1.250000,BotMoving,final-peek,1.500,2.500,3.500", lines[1]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PluginConfigNormalizesAndRejectsUnsafeWhitelists()
    {
        Assert.Equal(["de_train"], AwperTrainerConfig.NormalizeAllowedMaps(["DE_TRAIN", "de_train"]));
        Assert.Throws<InvalidDataException>(() => AwperTrainerConfig.NormalizeAllowedMaps([]));
        Assert.Throws<InvalidDataException>(() => AwperTrainerConfig.NormalizeAllowedMaps(["../de_mirage"]));
    }

    [Fact]
    public void MissingOrMismatchedCapabilityFailsClosed()
    {
        var adapter = new BotControllerAdapter();
        Assert.False(adapter.Begin(7, 1234));
        Assert.Contains("missing", adapter.CompatibilityMessage, StringComparison.OrdinalIgnoreCase);

        var api = new FakeApi { AbiVersion = 18 };
        adapter.Attach(api);
        Assert.False(adapter.Begin(7, 1234));
        Assert.Empty(api.Calls);
        Assert.Contains("incompatible", adapter.CompatibilityMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BeginReplayAndCleanupKeepAiLockedAndRecycleReplay()
    {
        var api = new FakeApi();
        var adapter = new BotControllerAdapter();
        adapter.Attach(api);
        var ticks = ReplayPathBuilder.Build(new(0, 0, 0), new(100, 0, 0), 215, 90);

        Assert.True(adapter.Begin(7, 1234));
        Assert.True(adapter.EquipAk47());
        Assert.True(adapter.StartMovement(1234, ticks));
        Assert.True(adapter.IsMovementActive);
        adapter.Cleanup();
        adapter.Cleanup();

        Assert.Equal(1, api.Calls.Count(x => x == "lock:7:All"));
        Assert.Equal(1, api.Calls.Count(x => x == "unlock:7:All"));
        Assert.Equal(2, api.Calls.Count(x => x == "pawn:7:1234"));
        Assert.Equal(2, api.Calls.Count(x => x == "replay-stop:7"));
        Assert.Contains("weapon:7:7", api.Calls);
        Assert.Contains($"replay-load:7:{ticks.Length}", api.Calls);
        Assert.Contains("replay-start:7", api.Calls);
    }

    [Fact]
    public void StopMovementStopsReplayWithoutUnlockingBotAi()
    {
        var api = new FakeApi();
        var adapter = new BotControllerAdapter();
        adapter.Attach(api);
        var ticks = ReplayPathBuilder.Build(new(0, 0, 0), new(32, 0, 0), 215, 0);

        Assert.True(adapter.Begin(4, 4321));
        Assert.True(adapter.StartMovement(4321, ticks));
        adapter.StopMovement();
        Assert.False(adapter.IsMovementActive);
        Assert.True(adapter.StartMovement(4321, ticks));
        adapter.Cleanup();

        Assert.Equal(1, api.Calls.Count(x => x == "lock:4:All"));
        Assert.Equal(1, api.Calls.Count(x => x == "unlock:4:All"));
        Assert.Equal(2, api.Calls.Count(x => x == "replay-start:4"));
    }

    [Fact]
    public void ReplayPathEndsExactlyAtTargetAtAkSpeed()
    {
        Assert.Equal(228, Marshal.SizeOf<ReplayTick>());
        var ticks = ReplayPathBuilder.Build(new(0, 0, 10), new(182, 0, 10), 215, 90);
        var expectedFirstTickSpeed = ReplayPathBuilder.SourceGroundAcceleration
            * (1f / ReplayPathBuilder.TickRate) * ReplayPathBuilder.Ak47NormalMoveSpeed;
        var expectedSecondTickSpeed = expectedFirstTickSpeed
            - ReplayPathBuilder.SourceStopSpeed * ReplayPathBuilder.SourceGroundFriction / ReplayPathBuilder.TickRate
            + expectedFirstTickSpeed;

        Assert.True(ticks.Length > 55);
        Assert.Equal(182, ticks[^1].Post.OriginX, 3);
        Assert.Equal(10, ticks[^1].Post.OriginZ, 3);
        Assert.Equal(0, ticks[0].Pre.VelX, 3);
        Assert.Equal(expectedFirstTickSpeed, ticks[0].Post.VelX, 3);
        Assert.Equal(ticks[0].Post.VelX, ticks[1].Pre.VelX, 3);
        Assert.Equal(expectedSecondTickSpeed, ticks[1].Post.VelX, 3);
        Assert.Contains(ticks, tick => MathF.Abs(tick.Pre.VelX - 215) < 0.001f);
        Assert.Equal(0, ticks[^1].Post.VelX, 3);
        Assert.All(ticks, tick => Assert.Equal(7, tick.WeaponDefIndex));
    }

    [Fact]
    public void ReplayPathUsesProvidedServerMovementSettings()
    {
        var settings = new ReplayPathBuilder.GroundMovementSettings(
            1f / 64f,
            4f,
            5.2f,
            80f);

        var ticks = ReplayPathBuilder.Build(new(0, 0, 0), new(100, 0, 0), 215, 0, settings);

        Assert.Equal(4f * (1f / 64f) * 215f, ticks[0].Post.VelX, 3);
        Assert.Equal(ticks[0].Post.VelX, ticks[1].Pre.VelX, 3);
    }

    private sealed class FakeApi : IBotControllerApi
    {
        private readonly HashSet<LockKind> _locks = [];
        public int AbiVersion { get; init; } = BotControllerAdapter.ExpectedAbi;
        public List<string> Calls { get; } = [];
        private bool _replaying;
        public bool Lock(int slot, LockKind kind) { Calls.Add($"lock:{slot}:{kind}"); return _locks.Add(kind); }
        public bool Unlock(int slot, LockKind kind) { Calls.Add($"unlock:{slot}:{kind}"); return _locks.Remove(kind); }
        public bool IsLocked(int slot, LockKind kind) => _locks.Contains(kind);
        public bool SetReplayPawn(int slot, nint pawn) { Calls.Add($"pawn:{slot}:{pawn}"); return pawn != 0; }
        public bool LoadReplay(int slot, ReplayTick[] ticks, SubtickMove[] subs) { Calls.Add($"replay-load:{slot}:{ticks.Length}"); return ticks.Length > 0; }
        public bool StartReplay(int slot, bool loop = false) { Calls.Add($"replay-start:{slot}"); _replaying = true; return true; }
        public bool StopReplay(int slot) { Calls.Add($"replay-stop:{slot}"); _replaying = false; return true; }
        public bool IsReplaying(int slot) => _replaying;
        public bool SwitchBotWeapon(int slot, int defIndex) { Calls.Add($"weapon:{slot}:{defIndex}"); return defIndex == 7; }
        public int BotActiveWeaponDef(int slot) => 7;
        public long StartUsercmdMovement(int slot, float forwardMove, float leftMove) => 1;
        public bool UpdateUsercmdMovement(int slot, long movementId, float forwardMove, float leftMove) => true;
        public bool CancelUsercmdMovement(int slot, long movementId) { Calls.Add($"move-cancel:{slot}:{movementId}"); return true; }
        public long StartUsercmdSuppression(int slot, ulong buttonMask) => 1;
        public bool CancelUsercmdSuppression(int slot, long suppressionId) { Calls.Add($"suppress-cancel:{slot}:{suppressionId}"); return true; }
    }
}
