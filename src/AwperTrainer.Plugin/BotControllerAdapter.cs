using BotControllerApi;
using CounterStrikeSharp.API.Core.Capabilities;
using System.Runtime.InteropServices;

namespace AwperTrainer.Plugin;

internal static class BotControllerCapability
{
    public static readonly PluginCapability<IBotControllerApi> Cap = new("botcontroller:api");
}

internal sealed class BotControllerAdapter
{
    public const int ExpectedAbi = 19;
    private IBotControllerApi? _api;
    private int _slot = -1;
    private bool _allLocked;

    public int? ActualAbi => _api?.AbiVersion;
    public string CompatibilityMessage => _api is null
        ? "BotController capability 'botcontroller:api' is missing."
        : _api.AbiVersion != ExpectedAbi
            ? $"BotController ABI {_api.AbiVersion} is incompatible; expected {ExpectedAbi}."
            : "BotController ABI 19 is compatible.";
    public bool IsCompatible => _api?.AbiVersion == ExpectedAbi;

    public void Attach(IBotControllerApi? api) => _api = api;

    public bool Begin(int slot, nint pawnHandle)
    {
        Cleanup();
        if (!IsCompatible || !_api!.SetReplayPawn(slot, pawnHandle)) return false;
        _slot = slot;
        if (!_api.Lock(slot, LockKind.All)) { Cleanup(); return false; }
        _allLocked = true;
        return true;
    }

    public bool EquipAk47()
    {
        if (_api is null || _slot < 0) return false;
        return _api.SwitchBotWeapon(_slot, ReplayPathBuilder.Ak47DefinitionIndex);
    }

    public bool StartMovement(nint pawnHandle, ReplayTick[] ticks)
    {
        if (_api is null || _slot < 0 || pawnHandle == 0 || ticks.Length == 0) return false;
        _api.StopReplay(_slot);
        return _api.SetReplayPawn(_slot, pawnHandle)
            && _api.LoadReplay(_slot, ticks, [])
            && _api.StartReplay(_slot);
    }

    public bool IsMovementActive => _api is not null && _slot >= 0 && _api.IsReplaying(_slot);

    public void StopMovement()
    {
        if (_api is null || _slot < 0) return;
        _api.StopReplay(_slot);
    }

    public void Cleanup()
    {
        if (_api is not null && _slot >= 0)
        {
            _api.StopReplay(_slot);
            if (_allLocked) _api.Unlock(_slot, LockKind.All);
        }
        _slot = -1;
        _allLocked = false;
    }

    public string GetNativeDiagnostics()
    {
        try
        {
            return $"hook={NativeDiagnostics.GetHookCallCount()} " +
                $"playerRun={NativeDiagnostics.GetPlayerRunCommandCallCount()} " +
                $"resolve={NativeDiagnostics.GetSlotResolveCallCount()} " +
                $"resolveFail={NativeDiagnostics.GetSlotResolveFailureCount()} " +
                $"lastResolved={NativeDiagnostics.GetLastResolvedSlot()} " +
                $"lastOwner={NativeDiagnostics.GetLastOwnerSlot()}";
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return $"native diagnostics unavailable ({ex.GetType().Name})";
        }
    }

    private static class NativeDiagnostics
    {
        [DllImport("BotController", EntryPoint = "BotController_GetHookCallCount", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong GetHookCallCount();

        [DllImport("BotController", EntryPoint = "BotController_GetPlayerRunCommandCallCount", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong GetPlayerRunCommandCallCount();

        [DllImport("BotController", EntryPoint = "BotController_GetSlotResolveCallCount", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong GetSlotResolveCallCount();

        [DllImport("BotController", EntryPoint = "BotController_GetSlotResolveFailureCount", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong GetSlotResolveFailureCount();

        [DllImport("BotController", EntryPoint = "BotController_GetLastResolvedSlot", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetLastResolvedSlot();

        [DllImport("BotController", EntryPoint = "BotController_GetLastOwnerSlot", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetLastOwnerSlot();
    }
}
