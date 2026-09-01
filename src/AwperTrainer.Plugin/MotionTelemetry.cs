using AwperTrainer.Core;
using System.Globalization;
using System.Text;

namespace AwperTrainer.Plugin;

internal sealed class MotionTelemetry : IDisposable
{
    private readonly StreamWriter _writer;

    private MotionTelemetry(StreamWriter writer)
    {
        _writer = writer;
        _writer.WriteLine("generation,seed,server_time,state,segment,origin_x,origin_y,origin_z,velocity_x,velocity_y,velocity_z,horizontal_speed,target_speed,distance_to_target,bot_alive,command_forward,command_left,command_yaw");
    }

    public string Path { get; private init; } = string.Empty;

    public static MotionTelemetry Create(string directory, int generation, ulong seed, AwperProfile profile)
    {
        Directory.CreateDirectory(directory);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var path = System.IO.Path.Combine(directory, $"{timestamp}-g{generation}-{profile.ProfileName}.csv");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        return new MotionTelemetry(new StreamWriter(stream, new UTF8Encoding(false))) { Path = path };
    }

    public void Write(int generation, ulong seed, double serverTime, TrainingState state, string segment,
        Vec3 origin, Vec3 velocity, float targetSpeed, float distanceToTarget, bool botAlive,
        float commandForward = 0, float commandLeft = 0, float commandYaw = 0)
    {
        var horizontalSpeed = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
        _writer.WriteLine(string.Join(',',
            generation.ToString(CultureInfo.InvariantCulture),
            seed.ToString(CultureInfo.InvariantCulture),
            serverTime.ToString("0.000000", CultureInfo.InvariantCulture),
            state,
            segment,
            origin.X.ToString("0.000", CultureInfo.InvariantCulture),
            origin.Y.ToString("0.000", CultureInfo.InvariantCulture),
            origin.Z.ToString("0.000", CultureInfo.InvariantCulture),
            velocity.X.ToString("0.000", CultureInfo.InvariantCulture),
            velocity.Y.ToString("0.000", CultureInfo.InvariantCulture),
            velocity.Z.ToString("0.000", CultureInfo.InvariantCulture),
            horizontalSpeed.ToString("0.000", CultureInfo.InvariantCulture),
            targetSpeed.ToString("0.000", CultureInfo.InvariantCulture),
            distanceToTarget.ToString("0.000", CultureInfo.InvariantCulture),
            botAlive ? "1" : "0",
            commandForward.ToString("0.000", CultureInfo.InvariantCulture),
            commandLeft.ToString("0.000", CultureInfo.InvariantCulture),
            commandYaw.ToString("0.000", CultureInfo.InvariantCulture)));
    }

    public void Dispose() => _writer.Dispose();
}
