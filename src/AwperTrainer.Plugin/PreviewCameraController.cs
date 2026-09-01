using AwperTrainer.Core;
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace AwperTrainer.Plugin;

internal sealed class PreviewCameraController
{
    public const string GhostModel = "characters/models/ctm_sas/ctm_sas.vmdl";
    public const string ScriptAsset = "scripts/awper/awper_camera.vjs";
    public const string ScriptAssetCompiled = "scripts/awper/awper_camera.vjs_c";
    private const string ScriptTargetName = "awper_camera_bridge";
    private readonly Dictionary<int, PreviewHandle> _active = [];
    private uint _scriptRaw;

    public bool IsActive(int playerSlot) => _active.ContainsKey(playerSlot);
    public IReadOnlyCollection<int> ActiveSlots => _active.Keys.ToArray();

    public bool Begin(CCSPlayerController player, PlayerAnchor anchor, Vec3 botPosition, Stance botStance,
        float botFacingYaw, out string diagnostic)
    {
        End(player.Slot);
        if (player.PlayerPawn.Value is not { IsValid: true } pawn)
        {
            diagnostic = "Player Pawn is invalid.";
            return false;
        }

        CBaseEntity script;
        try
        {
            script = EnsureScriptController();
        }
        catch (Exception ex)
        {
            diagnostic = $"Camera bridge failed: {ex.Message}";
            return false;
        }

        var ghost = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (ghost is null || !ghost.IsValid)
        {
            diagnostic = "The server could not create the preview ghost entity.";
            return false;
        }

        try
        {
            using var keyValues = new CEntityKeyValues();
            keyValues.SetString("model", GhostModel);
            ghost.DispatchSpawn(keyValues);
            ghost.RenderMode = RenderMode_t.kRenderTransAlpha;
            ghost.Render = Color.FromArgb(150, 120, 210, 255);
            ghost.TakesDamage = false;
            if (ghost.Collision is { } collision) collision.SolidType = SolidType_t.SOLID_NONE;
            ghost.Teleport(
                new Vector(botPosition.X, botPosition.Y, botPosition.Z),
                new QAngle(0, botFacingYaw, 0),
                Vector.Zero);
            Utilities.SetStateChanged(ghost, "CBaseModelEntity", "m_nRenderMode");
            Utilities.SetStateChanged(ghost, "CBaseModelEntity", "m_clrRender");
        }
        catch (Exception ex)
        {
            if (ghost.IsValid) ghost.Remove();
            diagnostic = $"Preview ghost failed: {ex.Message}";
            return false;
        }

        CBaseEntity? camera = null;
        var enabled = false;
        var previousPlayerLocked = pawn.PlayerLocked;
        var previousMoveType = pawn.MoveType;
        var preservedPawnAngles = new EulerAngles(pawn.EyeAngles.X, pawn.EyeAngles.Y, pawn.EyeAngles.Z);
        try
        {
            // GetCamera() performs engine-side Pawn ownership work that is missing
            // when cs_player_camera is spawned and its schema fields are written by hand.
            RunBridgeInput(script, pawn, player, "prepare");
            camera = FindOwnedCamera(pawn.EntityHandle.Raw)
                ?? throw new InvalidOperationException("GetCamera() did not expose a camera owned by this Pawn.");

            var cameraAngles = MotionMath.LookAt(anchor.EyePosition, MotionMath.EyePosition(botPosition, botStance));
            ApplyPose(camera, anchor.EyePosition, cameraAngles);
            RunBridgeInput(script, pawn, player, "enable");
            enabled = IsCameraEnabled(camera);
            if (!enabled)
                throw new InvalidOperationException("GetCamera().SetEnabled(true) did not persist on the server entity.");

            pawn.PlayerLocked = 1;
            ApplyPawnAngles(pawn, preservedPawnAngles);
            ApplyPose(camera, anchor.EyePosition, cameraAngles);
            _active[player.Slot] = new(
                camera.EntityHandle.Raw,
                ghost.EntityHandle.Raw,
                pawn.EntityHandle.Raw,
                previousMoveType,
                previousPlayerLocked,
                preservedPawnAngles,
                anchor.EyePosition,
                cameraAngles);
            diagnostic = "Camera fixed at PlayerAnchor while preserving the Pawn facing captured at preview start.";
            return true;
        }
        catch (Exception ex)
        {
            if (enabled)
            {
                try { RunBridgeInput(script, pawn, player, "disable"); }
                catch { }
            }
            if (ghost.IsValid) ghost.Remove();
            pawn.PlayerLocked = previousPlayerLocked;
            pawn.MoveType = previousMoveType;
            pawn.ActualMoveType = previousMoveType;
            ApplyPawnAngles(pawn, preservedPawnAngles);
            diagnostic = $"Pawn-owned camera failed: {ex.Message}";
            return false;
        }
    }

    public bool Refresh(int playerSlot)
    {
        if (!_active.TryGetValue(playerSlot, out var handle)) return false;
        var camera = ResolveEntity(handle.CameraRaw);
        var pawn = ResolveEntity(handle.PawnRaw)?.As<CCSPlayerPawn>();
        if (camera is not { IsValid: true } || pawn is not { IsValid: true } || !IsCameraEnabled(camera))
            return false;

        try
        {
            ApplyPawnAngles(pawn, handle.PreservedPawnAngles);
            ApplyPose(camera, handle.CameraPosition, handle.CameraAngles);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool End(int playerSlot)
    {
        if (!_active.Remove(playerSlot, out var handle)) return false;
        var player = Utilities.GetPlayerFromSlot(playerSlot);
        var ghost = ResolveEntity(handle.GhostRaw);
        var pawn = ResolveEntity(handle.PawnRaw)?.As<CCSPlayerPawn>();
        try
        {
            var script = ResolveEntity(_scriptRaw);
            if (script is { IsValid: true } && pawn is { IsValid: true })
                RunBridgeInput(script, pawn, player, "disable");
        }
        catch
        {
            // Restoration below must still run if the experimental script method
            // changes or the map is already tearing down.
        }
        finally
        {
            if (ghost is { IsValid: true }) ghost.Remove();
            if (pawn is { IsValid: true })
            {
                pawn.PlayerLocked = handle.PreviousPlayerLocked;
                pawn.MoveType = handle.PreviousMoveType;
                pawn.ActualMoveType = handle.PreviousMoveType;
                ApplyPawnAngles(pawn, handle.PreservedPawnAngles);
            }
        }
        return true;
    }

    public void EndAll()
    {
        foreach (var slot in _active.Keys.ToArray()) End(slot);
    }

    public void Shutdown()
    {
        EndAll();
        var script = ResolveEntity(_scriptRaw);
        if (script is { IsValid: true }) script.Remove();
        _scriptRaw = 0;
    }

    private CBaseEntity EnsureScriptController()
    {
        var existing = ResolveEntity(_scriptRaw);
        if (existing is { IsValid: true }) return existing;

        var script = Utilities.CreateEntityByName<CBaseEntity>("point_script");
        if (script is null || !script.IsValid)
            throw new InvalidOperationException("The server could not create point_script.");
        using var keyValues = new CEntityKeyValues();
        keyValues.SetString("targetname", ScriptTargetName);
        keyValues.SetString("cs_script", ScriptAsset);
        script.DispatchSpawn(keyValues);
        _scriptRaw = script.EntityHandle.Raw;
        return script;
    }

    private static void RunBridgeInput(CBaseEntity script, CBaseEntity pawn,
        CBaseEntity? player, string input)
        => script.AcceptInput("RunScriptInput", pawn, player, input);

    private static CBaseEntity? FindOwnedCamera(uint pawnRaw)
    {
        foreach (var camera in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("cs_player_camera"))
        {
            if (camera is not { IsValid: true }) continue;
            try
            {
                var owner = Schema.GetDeclaredClass<CHandle<CCSPlayerPawnBase>>(
                    camera.Handle, "CCSPlayerCamera", "m_hPawn");
                if (owner.Raw == pawnRaw) return camera;
            }
            catch
            {
                // Ignore unrelated or stale experimental camera entities.
            }
        }
        return null;
    }

    private static bool IsCameraEnabled(CBaseEntity camera)
        => Schema.GetRef<bool>(camera.Handle, "CCSPlayerCamera", "m_bEnabled");

    private static CBaseEntity? ResolveEntity(uint raw)
        => raw == 0 ? null : new CEntityHandle(raw).Value?.As<CBaseEntity>();

    private static void ApplyPose(CBaseEntity camera, Vec3 position, EulerAngles angles)
        => camera.Teleport(
            new Vector(position.X, position.Y, position.Z),
            new QAngle(angles.Pitch, angles.Yaw, angles.Roll),
            Vector.Zero);

    private static void ApplyPawnAngles(CCSPlayerPawn pawn, EulerAngles angles)
        => pawn.Teleport(null, new QAngle(angles.Pitch, angles.Yaw, angles.Roll), Vector.Zero);

    private sealed record PreviewHandle(
        uint CameraRaw,
        uint GhostRaw,
        uint PawnRaw,
        MoveType_t PreviousMoveType,
        int PreviousPlayerLocked,
        EulerAngles PreservedPawnAngles,
        Vec3 CameraPosition,
        EulerAngles CameraAngles);
}
