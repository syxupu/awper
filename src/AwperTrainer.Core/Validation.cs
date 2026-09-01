namespace AwperTrainer.Core;

public enum ValidationSeverity { Warning, Error }
public sealed record ValidationIssue(string Code, ValidationSeverity Severity, string Message);
public sealed record ValidationResult(IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsValid => Issues.All(x => x.Severity != ValidationSeverity.Error);
}

public interface IWorldProbe
{
    bool CanFitStandingPlayer(Vec3 point);
    bool HasStandableGround(Vec3 point, out float normalZ);
    bool IsStandingHullPathClear(Vec3 from, Vec3 to);
    bool HasLineOfSight(Vec3 from, Vec3 to);
    string? CurrentMapFingerprint { get; }
}

public sealed record ProfileValidationOptions
{
    public float MinimumPathLength { get; init; } = 16f;
    public float MaximumPathLength { get; init; } = 1024f;
    public float MaximumHeightDifference { get; init; } = 18f;
    public float MinimumGroundNormalZ { get; init; } = 0.7f;
}

public sealed class ProfileValidator(IWorldProbe world, ProfileValidationOptions? options = null)
{
    private readonly ProfileValidationOptions _options = options ?? new();

    public ValidationResult Validate(AwperProfile profile, string currentMap)
    {
        var issues = new List<ValidationIssue>();
        if (profile.SchemaVersion != AwperProfile.CurrentSchemaVersion)
            Error("schema.unsupported", $"Schema {profile.SchemaVersion} is unsupported.");
        if (!string.Equals(MapPolicy.Normalize(profile.MapName), MapPolicy.Normalize(currentMap), StringComparison.Ordinal))
            Error("map.mismatch", $"Profile map '{profile.MapName}' does not match '{currentMap}'.");

        CheckPoint("player", profile.PlayerAnchor.PawnPosition);
        CheckPoint("bot.start", profile.BotPath.Start);
        CheckPoint("bot.end", profile.BotPath.End);
        CheckSegment("bot.start-end", profile.BotPath.Start, profile.BotPath.End);

        if (profile.Training.Mode == TrainingMode.JiggleThenPeek)
        {
            if (profile.BotPath.Jiggle is not { } jiggle) Error("bot.jiggle.missing", "Jiggle mode requires BotJiggle.");
            else
            {
                CheckPoint("bot.jiggle", jiggle);
                CheckSegment("bot.start-jiggle", profile.BotPath.Start, jiggle);
            }
        }

        var botStartChest = profile.BotPath.Start with { Z = profile.BotPath.Start.Z + 48 };
        var botEndChest = profile.BotPath.End with { Z = profile.BotPath.End.Z + 48 };
        if (!world.HasLineOfSight(profile.PlayerAnchor.EyePosition, botStartChest))
            Warn("los.start", "PlayerAnchor has no direct line of sight to BotStart; this is allowed.");
        if (!world.HasLineOfSight(profile.PlayerAnchor.EyePosition, botEndChest))
            Warn("los.end", "PlayerAnchor has no direct line of sight to BotEnd; this is allowed.");
        if (!SettingsAreValid(profile.Training))
            Error("training.invalid", "Training settings contain an invalid value or range.");
        return new(issues);

        void CheckPoint(string name, Vec3 point)
        {
            if (!world.CanFitStandingPlayer(point)) Error($"{name}.hull", $"{name} cannot fit a standing player hull.");
            if (!world.HasStandableGround(point, out var normalZ)) Error($"{name}.ground", $"{name} has no standable ground.");
            else if (normalZ < _options.MinimumGroundNormalZ) Error($"{name}.slope", $"{name} ground is too steep ({normalZ:0.00}).");
        }

        void CheckSegment(string name, Vec3 from, Vec3 to)
        {
            var length = from.HorizontalDistanceTo(to);
            if (length < _options.MinimumPathLength || length > _options.MaximumPathLength)
                Error($"{name}.length", $"{name} length {length:0.0} is outside {_options.MinimumPathLength:0}-{_options.MaximumPathLength:0}.");
            if (MathF.Abs(from.Z - to.Z) > _options.MaximumHeightDifference)
                Error($"{name}.height", $"{name} height difference exceeds {_options.MaximumHeightDifference:0.0}.");
            if (!world.IsStandingHullPathClear(from, to)) Error($"{name}.blocked", $"{name} is blocked.");
        }

        void Error(string code, string message) => issues.Add(new(code, ValidationSeverity.Error, message));
        void Warn(string code, string message) => issues.Add(new(code, ValidationSeverity.Warning, message));
    }

    private static bool SettingsAreValid(TrainingSettings value)
    {
        return !(value.CountdownSeconds < 0 || value.RandomDelayMinSeconds < 0 ||
            value.RandomDelayMaxSeconds < value.RandomDelayMinSeconds ||
            value.TargetSpeed is < 1 or > TrainingSettings.MaximumAk47Speed ||
            value.JiggleCountMin < 1 || value.JiggleCountMax < value.JiggleCountMin ||
            value.JiggleEndpointPauseMinSeconds < 0 ||
            value.JiggleEndpointPauseMaxSeconds < value.JiggleEndpointPauseMinSeconds ||
            value.CompletionRadius <= 0 || value.RunTimeoutSeconds <= 0 || value.FinishFeedbackSeconds < 0);
    }
}
