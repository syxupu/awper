using AwperTrainer.Core;
using BotControllerApi;

namespace AwperTrainer.Plugin;

internal static class ReplayPathBuilder
{
    internal const int TickRate = 64;
    internal const int Ak47DefinitionIndex = 7;
    internal const float Ak47NormalMoveSpeed = 215f;
    internal const float SourceGroundAcceleration = 5.5f;
    internal const float SourceGroundFriction = 5.2f;
    internal const float SourceStopSpeed = 80f;
    private const byte WalkMoveType = 2;
    private const uint OnGroundFlag = 1;
    private const ulong InForward = 1UL << 3;
    private const ulong InBack = 1UL << 4;
    private const ulong InMoveLeft = 1UL << 9;
    private const ulong InMoveRight = 1UL << 10;

    public static ReplayTick[] Build(Vec3 from, Vec3 to, float speed, float facingYaw,
        GroundMovementSettings? movementSettings = null)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        if (!float.IsFinite(speed) || speed <= 0 || distance <= 0.001f) return [];

        var settings = movementSettings ?? GroundMovementSettings.SourceDefaults;
        if (!settings.IsValid) return [];

        var unitX = dx / distance;
        var unitY = dy / distance;
        var movement = MotionMath.ProjectWorldDirection(from, to, facingYaw);
        var buttons = MovementButtons(movement);
        var ticks = new List<ReplayTick>();
        var travelled = 0f;
        var currentSpeed = 0f;

        while (travelled < distance)
        {
            var pre = PointAt(from, to, unitX, unitY, travelled, distance);
            var preVelocity = new Vec3(unitX * currentSpeed, unitY * currentSpeed, 0);
            var frictionSpeed = ApplyGroundFriction(currentSpeed, settings);
            var accelerationDelta = settings.Acceleration * settings.TickInterval * Ak47NormalMoveSpeed
                * settings.SurfaceFriction;
            var nextSpeed = MathF.Min(speed, frictionSpeed + accelerationDelta);
            if (!float.IsFinite(nextSpeed) || nextSpeed <= 0) return [];

            var postDistance = MathF.Min(distance, travelled + nextSpeed * settings.TickInterval);
            var post = PointAt(from, to, unitX, unitY, postDistance, distance);
            var isFirst = ticks.Count == 0;
            var isLast = postDistance >= distance;
            var postVelocity = isLast ? Vec3.Zero : new Vec3(unitX * nextSpeed, unitY * nextSpeed, 0);
            ticks.Add(new ReplayTick
            {
                Pre = Snapshot(pre, preVelocity, facingYaw, buttons, isFirst ? buttons : 0, 0),
                Post = Snapshot(post, postVelocity, facingYaw,
                    isLast ? 0 : buttons, 0, isLast ? buttons : 0),
                WeaponDefIndex = Ak47DefinitionIndex,
                EventWeaponDefIndex = -1
            });
            travelled = postDistance;
            currentSpeed = nextSpeed;
        }

        return ticks.ToArray();
    }

    private static float ApplyGroundFriction(float speed, GroundMovementSettings settings)
    {
        if (speed <= 0) return 0;
        var control = MathF.Max(speed, settings.StopSpeed);
        var drop = control * settings.Friction * settings.SurfaceFriction * settings.TickInterval;
        return MathF.Max(0, speed - drop);
    }

    private static Vec3 PointAt(Vec3 from, Vec3 to, float unitX, float unitY, float travelled, float distance)
        => travelled >= distance
            ? to
            : new Vec3(from.X + unitX * travelled, from.Y + unitY * travelled,
                from.Z + (to.Z - from.Z) * (travelled / distance));

    private static MovementSnapshot Snapshot(
        Vec3 origin,
        Vec3 velocity,
        float yaw,
        ulong buttons,
        ulong pressed,
        ulong released)
        => new()
        {
            OriginX = origin.X,
            OriginY = origin.Y,
            OriginZ = origin.Z,
            VelX = velocity.X,
            VelY = velocity.Y,
            VelZ = velocity.Z,
            Yaw = yaw,
            EntityFlags = OnGroundFlag,
            MoveType = WalkMoveType,
            ActualMoveType = WalkMoveType,
            Buttons = buttons,
            Buttons1 = pressed,
            Buttons2 = released,
            DuckSpeed = 8f
        };

    private static ulong MovementButtons(LocalMove movement)
    {
        ulong buttons = 0;
        if (movement.Forward > 0.001f) buttons |= InForward;
        else if (movement.Forward < -0.001f) buttons |= InBack;
        if (movement.Left > 0.001f) buttons |= InMoveLeft;
        else if (movement.Left < -0.001f) buttons |= InMoveRight;
        return buttons;
    }

    internal readonly record struct GroundMovementSettings(
        float TickInterval,
        float Acceleration,
        float Friction,
        float StopSpeed,
        float SurfaceFriction = 1f)
    {
        public static GroundMovementSettings SourceDefaults { get; } = new(
            1f / TickRate,
            SourceGroundAcceleration,
            SourceGroundFriction,
            SourceStopSpeed);

        public bool IsValid =>
            float.IsFinite(TickInterval) && TickInterval > 0 &&
            float.IsFinite(Acceleration) && Acceleration > 0 &&
            float.IsFinite(Friction) && Friction >= 0 &&
            float.IsFinite(StopSpeed) && StopSpeed >= 0 &&
            float.IsFinite(SurfaceFriction) && SurfaceFriction > 0;
    }
}
