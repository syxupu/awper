using AwperTrainer.Core;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Utils;
using RayTraceAPI;
using RayTraceOptions = RayTraceAPI.TraceOptions;
using RayTraceResult = RayTraceAPI.TraceResult;

namespace AwperTrainer.Plugin;

internal static class RayTraceCapability
{
    public static readonly PluginCapability<CRayTraceInterface> Cap = new("raytrace:craytraceinterface");
}

internal sealed class RayTraceWorldProbe : IWorldProbe
{
    private static readonly Vector StandingMins = new(-16, -16, 1);
    private static readonly Vector StandingMaxs = new(16, 16, 72);
    private static readonly RayTraceOptions PlayerMove = new(InteractionLayers.MaskPlayerMove);
    private static readonly RayTraceOptions WorldOnly = new(InteractionLayers.MaskWorldOnly);
    private CRayTraceInterface? _api;
    private string? _lastError;

    public bool IsAttached => _api is not null;
    public string CompatibilityMessage => _api is null
        ? "RayTrace capability 'raytrace:craytraceinterface' is missing."
        : _lastError is null
            ? "RayTrace capability is attached; each validation performs a live native trace."
            : $"RayTrace live trace failed: {_lastError}";
    public string? CurrentMapFingerprint => _api is null ? null : $"{MapPolicy.Normalize(Server.MapName)}:live-raytrace";

    public void Attach(CRayTraceInterface? api)
    {
        _api = api;
        _lastError = null;
    }

    public bool CanFitStandingPlayer(Vec3 point)
    {
        if (!TryHull(point, point with { Z = point.Z + 0.01f }, out var result)) return false;
        return !result.IsAllSolid && result.Fraction >= 0.999f;
    }

    public bool HasStandableGround(Vec3 point, out float normalZ)
    {
        normalZ = 0;
        if (!TryLine(point with { Z = point.Z + 2 }, point with { Z = point.Z - 20 }, WorldOnly, out var result))
            return false;
        normalZ = result.NormalZ;
        return result.DidHit && !result.IsAllSolid;
    }

    public bool IsStandingHullPathClear(Vec3 from, Vec3 to)
    {
        if (!TryHull(from, to, out var result)) return false;
        return !result.IsAllSolid && result.Fraction >= 0.999f;
    }

    public bool HasLineOfSight(Vec3 from, Vec3 to)
    {
        if (!TryLine(from, to, WorldOnly, out var result)) return false;
        return !result.IsAllSolid && result.Fraction >= 0.999f;
    }

    private bool TryHull(Vec3 from, Vec3 to, out RayTraceResult result)
    {
        result = default;
        if (_api is null) return Fail("capability is not attached");
        try
        {
            if (!_api.TraceHullShape(ToVector(from), ToVector(to), StandingMins, StandingMaxs, null, PlayerMove, out result))
                return Fail("native TraceHullShape returned false");
            _lastError = null;
            return true;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private bool TryLine(Vec3 from, Vec3 to, RayTraceOptions options, out RayTraceResult result)
    {
        result = default;
        if (_api is null) return Fail("capability is not attached");
        try
        {
            if (!_api.TraceEndShape(ToVector(from), ToVector(to), null, options, out result))
                return Fail("native TraceEndShape returned false");
            _lastError = null;
            return true;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private bool Fail(string message)
    {
        _lastError = message;
        return false;
    }

    private static Vector ToVector(Vec3 value) => new(value.X, value.Y, value.Z);
}
