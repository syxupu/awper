using System.Text.Json.Serialization;

namespace AwperTrainer.Core;

public readonly record struct Vec3(float X, float Y, float Z)
{
    public static Vec3 Zero => new(0, 0, 0);
    public float HorizontalDistanceTo(Vec3 other)
        => MathF.Sqrt(MathF.Pow(other.X - X, 2) + MathF.Pow(other.Y - Y, 2));
    public float DistanceTo(Vec3 other)
        => MathF.Sqrt(MathF.Pow(other.X - X, 2) + MathF.Pow(other.Y - Y, 2) + MathF.Pow(other.Z - Z, 2));
}

public readonly record struct EulerAngles(float Pitch, float Yaw, float Roll);

[JsonConverter(typeof(JsonStringEnumConverter<Stance>))]
public enum Stance { Standing, Crouching }

[JsonConverter(typeof(JsonStringEnumConverter<TrainingMode>))]
public enum TrainingMode { DirectPeek, JiggleThenPeek }

public sealed record WorldAnchor(Vec3 Position, EulerAngles Angles);

public sealed record PlayerAnchor(
    Vec3 PawnPosition,
    Vec3 EyePosition,
    EulerAngles EyeAngles,
    Stance Stance = Stance.Standing);

public sealed record BotPath(
    Vec3 Start,
    Vec3 End,
    Vec3? Jiggle,
    float FacingYaw,
    Stance Stance = Stance.Standing);

public sealed record TrainingSettings
{
    public const float MaximumAk47Speed = 215.0f;
    public TrainingMode Mode { get; init; } = TrainingMode.DirectPeek;
    public double CountdownSeconds { get; init; } = 3.0;
    public double RandomDelayMinSeconds { get; init; } = 0.5;
    public double RandomDelayMaxSeconds { get; init; } = 3.0;
    public float TargetSpeed { get; init; } = MaximumAk47Speed;
    public int JiggleCountMin { get; init; } = 1;
    public int JiggleCountMax { get; init; } = 4;
    public double JiggleEndpointPauseMinSeconds { get; init; } = 0.05;
    public double JiggleEndpointPauseMaxSeconds { get; init; } = 0.20;
    public float CompletionRadius { get; init; } = 4.0f;
    public double RunTimeoutSeconds { get; init; } = 10.0;
    public double FinishFeedbackSeconds { get; init; } = 0.20;

    public static float RequireValidTargetSpeed(float value)
    {
        if (!float.IsFinite(value) || value is < 1 or > MaximumAk47Speed)
            throw new ArgumentOutOfRangeException(nameof(value), "Target speed must be 1-215 units/s.");
        return value;
    }
}

public sealed record AwperProfile
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string MapName { get; init; }
    public required string ProfileName { get; init; }
    public required WorldAnchor EditAnchor { get; init; }
    public required PlayerAnchor PlayerAnchor { get; init; }
    public required BotPath BotPath { get; init; }
    public TrainingSettings Training { get; init; } = new();
    public string? ValidatedMapFingerprint { get; init; }
}

public static class ProfileVariants
{
    public static AwperProfile WithSpeed(AwperProfile source, float targetSpeed)
    {
        ArgumentNullException.ThrowIfNull(source);
        var speed = TrainingSettings.RequireValidTargetSpeed(targetSpeed);
        return source with { Training = source.Training with { TargetSpeed = speed } };
    }

    public static AwperProfile CopyWithSpeed(AwperProfile source, string profileName, float targetSpeed)
        => WithSpeed(source, targetSpeed) with { ProfileName = ProfileNames.Normalize(profileName) };
}

public sealed class ProfileDraft
{
    public string MapName { get; }
    public WorldAnchor? EditAnchor { get; private set; }
    public PlayerAnchor? PlayerAnchor { get; private set; }
    public Vec3? BotStart { get; private set; }
    public Vec3? BotEnd { get; private set; }
    public Vec3? BotJiggle { get; private set; }
    public float? BotFacingYaw { get; private set; }
    public TrainingSettings Training { get; private set; } = new();

    public ProfileDraft(string mapName) => MapName = MapPolicy.Normalize(mapName);
    public void SetEditAnchor(WorldAnchor anchor) => EditAnchor = anchor;
    public void SetPlayerAnchor(PlayerAnchor anchor) => PlayerAnchor = anchor;
    public void SetBotStart(Vec3 value) => BotStart = value;
    public void SetBotEnd(Vec3 value) => BotEnd = value;
    public void SetBotJiggle(Vec3 value) => BotJiggle = value;
    public void SetBotFacingYaw(float yaw) => BotFacingYaw = MotionMath.NormalizeYaw(yaw);
    public void SetMode(TrainingMode mode) => Training = Training with { Mode = mode };
    public void SetTargetSpeed(float value)
    {
        Training = Training with { TargetSpeed = TrainingSettings.RequireValidTargetSpeed(value) };
    }

    public AwperProfile Build(string profileName)
    {
        if (EditAnchor is null || PlayerAnchor is null || BotStart is null || BotEnd is null)
            throw new InvalidOperationException("Draft is missing required anchors.");
        if (Training.Mode == TrainingMode.JiggleThenPeek && BotJiggle is null)
            throw new InvalidOperationException("Jiggle mode requires BotJiggle.");

        var facing = BotFacingYaw ?? MotionMath.YawFacing(BotStart.Value, PlayerAnchor.PawnPosition);
        return new AwperProfile
        {
            MapName = MapName,
            ProfileName = ProfileNames.Normalize(profileName),
            EditAnchor = EditAnchor,
            PlayerAnchor = PlayerAnchor,
            BotPath = new BotPath(BotStart.Value, BotEnd.Value, BotJiggle, facing),
            Training = Training
        };
    }
}
