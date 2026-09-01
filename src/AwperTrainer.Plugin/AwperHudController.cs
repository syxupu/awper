using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace AwperTrainer.Plugin;

internal sealed class AwperHudController
{
    public const string LayoutAsset = "panorama/layout/custom_game/awper_hud.vxml";
    public const string StyleAsset = "panorama/styles/custom_game/awper_hud.vcss";
    public const string ScriptAsset = "scripts/awper/awper_hud.vjs";
    private const string LayoutTargetName = "awper_hud_layout";
    private const string ScriptTargetName = "awper_hud_controller";
    private uint _layoutRaw;
    private uint _scriptRaw;
    public bool ScriptReady { get; private set; }

    public bool Initialize(out string diagnostic)
    {
        try
        {
            var script = EnsureEntities();
            diagnostic = $"HUD entities initialized (layout={_layoutRaw}, script={script.EntityHandle.Raw}).";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = ex.Message;
            return false;
        }
    }

    public bool Probe(out string diagnostic)
    {
        try
        {
            var script = EnsureEntities();
            script.AcceptInput("RunScriptInput", script, script, "Probe");
            diagnostic = "HUD script probe sent.";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = ex.Message;
            return false;
        }
    }

    public void MarkReady() => ScriptReady = true;

    public bool Toggle(CCSPlayerController player, out string diagnostic)
        => RunForPlayer(player, "ToggleMenu", out diagnostic);

    public bool Close(CCSPlayerController player)
        => RunForPlayer(player, "CloseMenu", out _);

    public void CloseAll()
    {
        foreach (var player in Utilities.GetPlayers().Where(player => player is { IsValid: true, IsBot: false }))
            Close(player);
    }

    public void Shutdown()
    {
        CloseAll();
        var script = ResolveEntity(_scriptRaw);
        if (script is { IsValid: true }) script.Remove();
        var layout = ResolveEntity(_layoutRaw);
        if (layout is { IsValid: true }) layout.Remove();
        _scriptRaw = 0;
        _layoutRaw = 0;
        ScriptReady = false;
    }

    private bool RunForPlayer(CCSPlayerController player, string input, out string diagnostic)
    {
        if (player.PlayerPawn.Value is not { IsValid: true } pawn)
        {
            diagnostic = "Player Pawn is unavailable.";
            return false;
        }

        try
        {
            var script = EnsureEntities();
            script.AcceptInput("RunScriptInput", pawn, player, input);
            diagnostic = $"HUD input {input} sent for slot {player.Slot}.";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = ex.Message;
            return false;
        }
    }

    private CBaseEntity EnsureEntities()
    {
        var layout = ResolveEntity(_layoutRaw);
        if (layout is not { IsValid: true })
        {
            layout = Utilities.CreateEntityByName<CBaseEntity>("custom_hud_layout")
                ?? throw new InvalidOperationException("The server could not create custom_hud_layout.");
            using var layoutValues = new CEntityKeyValues();
            layoutValues.SetString("targetname", LayoutTargetName);
            layoutValues.SetString("layout", LayoutAsset);
            layout.DispatchSpawn(layoutValues);
            _layoutRaw = layout.EntityHandle.Raw;
        }

        var script = ResolveEntity(_scriptRaw);
        if (script is { IsValid: true }) return script;
        script = Utilities.CreateEntityByName<CBaseEntity>("point_script")
            ?? throw new InvalidOperationException("The server could not create the HUD point_script.");
        using var scriptValues = new CEntityKeyValues();
        scriptValues.SetString("targetname", ScriptTargetName);
        scriptValues.SetString("cs_script", ScriptAsset);
        script.DispatchSpawn(scriptValues);
        _scriptRaw = script.EntityHandle.Raw;
        ScriptReady = false;
        return script;
    }

    private static CBaseEntity? ResolveEntity(uint raw)
        => raw == 0 ? null : new CEntityHandle(raw).Value?.As<CBaseEntity>();
}
