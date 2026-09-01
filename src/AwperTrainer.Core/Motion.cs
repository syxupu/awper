namespace AwperTrainer.Core;

public readonly record struct LocalMove(float Forward, float Left);

public static class MotionMath
{
    public static Vec3 EyePosition(Vec3 pawnPosition, Stance stance)
        => pawnPosition with { Z = pawnPosition.Z + (stance == Stance.Crouching ? 46f : 64f) };

    public static EulerAngles LookAt(Vec3 from, Vec3 target)
    {
        var dx = target.X - from.X;
        var dy = target.Y - from.Y;
        var dz = target.Z - from.Z;
        var horizontal = MathF.Sqrt(dx * dx + dy * dy);
        if (horizontal < 0.0001f && MathF.Abs(dz) < 0.0001f) return new(0, 0, 0);

        var yaw = NormalizeYaw(MathF.Atan2(dy, dx) * 180f / MathF.PI);
        var pitch = NormalizeYaw(-MathF.Atan2(dz, horizontal) * 180f / MathF.PI);
        return new(pitch, yaw, 0);
    }

    public static LocalMove ProjectWorldDirection(Vec3 from, Vec3 to, float facingYaw)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 0.0001f) return new LocalMove(0, 0);
        dx /= length;
        dy /= length;

        var radians = NormalizeYaw(facingYaw) * MathF.PI / 180f;
        var forwardX = MathF.Cos(radians);
        var forwardY = MathF.Sin(radians);
        var leftX = -forwardY;
        var leftY = forwardX;
        return new LocalMove(
            Math.Clamp(dx * forwardX + dy * forwardY, -1f, 1f),
            Math.Clamp(dx * leftX + dy * leftY, -1f, 1f));
    }

    public static float YawFacing(Vec3 from, Vec3 to)
        => NormalizeYaw(MathF.Atan2(to.Y - from.Y, to.X - from.X) * 180f / MathF.PI);

    public static float NormalizeYaw(float yaw)
    {
        var normalized = yaw % 360f;
        if (normalized >= 180f) normalized -= 360f;
        if (normalized < -180f) normalized += 360f;
        return normalized;
    }
}

public sealed record MotionSegment(Vec3 Target, bool PauseAfter, string Label);

public static class MotionRouteBuilder
{
    public static IReadOnlyList<MotionSegment> Build(AwperProfile profile, int jiggleCount)
    {
        var route = new List<MotionSegment>();
        if (profile.Training.Mode == TrainingMode.JiggleThenPeek)
        {
            if (profile.BotPath.Jiggle is null) throw new InvalidOperationException("Jiggle point is required.");
            if (jiggleCount < 1) throw new ArgumentOutOfRangeException(nameof(jiggleCount));
            for (var i = 0; i < jiggleCount; i++)
            {
                route.Add(new(profile.BotPath.Jiggle.Value, true, $"jiggle-out-{i + 1}"));
                route.Add(new(profile.BotPath.Start, true, $"jiggle-back-{i + 1}"));
            }
        }
        route.Add(new(profile.BotPath.End, false, "final-peek"));
        return route;
    }
}

public sealed class DeterministicRandom(ulong seed)
{
    private ulong _state = seed;
    private ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));
    public double Between(double min, double max) => min + NextDouble() * (max - min);
    public int Inclusive(int min, int max)
    {
        if (max < min) throw new ArgumentOutOfRangeException(nameof(max));
        return min + (int)(NextDouble() * (max - min + 1));
    }
}
