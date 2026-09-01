using AwperTrainer.Core;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Globalization;

namespace AwperTrainer.Plugin;

[MinimumApiVersion(373)]
public sealed class AwperTrainerPlugin : BasePlugin, IPluginConfig<AwperTrainerConfig>
{
    private const double BotSpawnSettleSeconds = 0.5;
    private readonly Dictionary<int, EditorSession> _editors = [];
    private readonly BotControllerAdapter _bots = new();
    private MapPolicy _maps = new();
    private readonly PreviewCameraController _cameras = new();
    private AwperNativeMenu? _menu;
    private readonly RayTraceWorldProbe _world = new();
    private ProfileRepository? _profiles;
    private string? _evidenceDirectory;
    private RuntimeSession? _runtime;
    private int _nextGeneration;

    public override string ModuleName => "CS2 AWP Bot Trainer";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "AwperTrainer contributors";
    public override string ModuleDescription => "Deterministic single-player AWP peek training.";
    public AwperTrainerConfig Config { get; set; } = new();

    public void OnConfigParsed(AwperTrainerConfig config)
    {
        var normalized = AwperTrainerConfig.NormalizeAllowedMaps(config.AllowedMaps);
        if (config.MotionCsvSampleEveryTicks is < 1 or > 128)
            throw new InvalidDataException("MotionCsvSampleEveryTicks must be 1-128.");
        config.AllowedMaps = normalized;
        Config = config;
        _maps = new MapPolicy(normalized);
    }

    public override void Load(bool hotReload)
    {
        var profileDirectory = Path.Combine(Server.GameDirectory, "csgo", "addons", "counterstrikesharp", "configs",
            "plugins", "AwperTrainer", "profiles");
        _profiles = new ProfileRepository(profileDirectory);
        _menu = new AwperNativeMenu(this, () => _profiles.List(Server.MapName), () => _maps.AllowedMaps);
        _evidenceDirectory = Path.Combine(Server.GameDirectory, "csgo", "addons", "counterstrikesharp", "configs",
            "plugins", "AwperTrainer", "evidence");
        AddCommand("css_help", "Show the in-game AWPER command help.", ChatHelp);
        AddCommand("css_ui", "Toggle the in-game AWPER control panel.", ToggleHud);
        AddCommand("css_maps", "List the seven supported training maps.", ListMaps);
        AddCommand("css_map", "Switch to one of the seven supported training maps.", ChangeMap);
        AddCommand("css_edit", "Enter editing mode and declare the profile name.", BeginEditing);
        AddCommand("css_set_edit_anchor", "Record the Bot editing entry point.", SetEditAnchor);
        AddCommand("css_set_player_anchor", "Record the player training point and enter editing.", SetPlayerAnchor);
        AddCommand("css_set_bot_start", "Record BotStart at your feet.", (p, c) => SetBotPoint(p, c, BotPoint.Start));
        AddCommand("css_set_bot_end", "Record BotEnd at your feet.", (p, c) => SetBotPoint(p, c, BotPoint.End));
        AddCommand("css_set_bot_jiggle", "Record BotJiggle at your feet.", (p, c) => SetBotPoint(p, c, BotPoint.Jiggle));
        AddCommand("css_set_bot_facing", "Record BotFacingYaw from your view.", SetBotFacing);
        AddCommand("css_mode", "Set mode 1=direct or 2=jiggle.", SetMode);
        AddCommand("css_speed", "Set target AK-47 ground speed in units/s (1-215).", SetSpeed);
        AddCommand("css_validate", "Run validation and show missing runtime gates.", Validate);
        AddCommand("css_save", "Save a statically valid profile (M0 requires server probe evidence).", Save);
        AddCommand("css_load", "Load a profile for the current map.", LoadProfile);
        AddCommand("css_list", "List profiles for the current map.", ListProfiles);
        AddCommand("css_delete", "Delete a current-map profile.", DeleteProfile);
        AddCommand("css_copy", "Copy a current-map profile with a new name and speed.", CopyProfile);
        AddCommand("css_start", "Start one training round.", StartRound);
        AddCommand("css_start_speed", "Start one training round with a one-time speed override.", StartRoundWithSpeed);
        AddCommand("css_abort", "Abort and restore the player.", Abort);
        AddCommand("css_status", "Show compatibility and session status.", Status);
        AddCommand("css_preview_on", "Begin PlayerAnchor camera preview.", BeginPreview);
        AddCommand("css_preview_off", "End PlayerAnchor camera preview.", EndPreview);
        AddCommand("css_preview_toggle", "Toggle PlayerAnchor camera preview.", TogglePreview);
        AddCommand("+preview", "Begin PlayerAnchor camera preview.", BeginPreview);
        AddCommand("-preview", "End PlayerAnchor camera preview.", EndPreview);
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            manifest.AddResource(PreviewCameraController.GhostModel);
            manifest.AddResource(PreviewCameraController.ScriptAsset);
        });
        RegisterEventHandler<EventRoundEnd>((_, _) =>
        {
            _menu.CloseAll();
            _cameras.EndAll();
            AbortRuntime(FinishReason.Aborted);
            foreach (var editor in _editors.Values.ToArray()) RestoreEditor(editor);
            _editors.Clear();
            return HookResult.Continue;
        });
        RegisterEventHandler<EventPlayerDeath>((@event, _) =>
        {
            if (@event.Userid is { IsValid: true } victim)
            {
                _menu.Close(victim);
                _cameras.End(victim.Slot);
                if (_runtime?.PlayerSlot == victim.Slot) AbortRuntime(FinishReason.PlayerKilled);
                else _editors.Remove(victim.Slot);
            }
            return HookResult.Continue;
        });
        RegisterEventHandler<EventPlayerTeam>((@event, _) =>
        {
            if (@event.Userid is { IsValid: true } changed)
            {
                _menu.Close(changed);
                _cameras.End(changed.Slot);
                if (_runtime?.PlayerSlot == changed.Slot) AbortRuntime(FinishReason.Aborted);
            }
            return HookResult.Continue;
        });
        Logger.LogInformation("AwperTrainer M0 loaded. Current map policy: {Maps}", string.Join(", ", _maps.AllowedMaps));
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        try { _bots.Attach(BotControllerCapability.Cap.Get()); }
        catch (Exception ex) { Logger.LogError(ex, "Unable to acquire BotController capability."); }
        try { _world.Attach(RayTraceCapability.Cap.Get()); }
        catch (Exception ex) { Logger.LogError(ex, "Unable to acquire RayTrace capability."); }
        Logger.LogInformation("{Compatibility}", _bots.CompatibilityMessage);
        Logger.LogInformation("{Compatibility}", _world.CompatibilityMessage);
        Logger.LogInformation("BotController native diagnostics: {Diagnostics}.", _bots.GetNativeDiagnostics());
    }

    public override void Unload(bool hotReload)
    {
        AbortRuntime(FinishReason.Aborted);
        foreach (var editor in _editors.Values.ToArray()) RestoreEditor(editor);
        _editors.Clear();
        _menu?.CloseAll();
        _cameras.Shutdown();
        _bots.Cleanup();
    }

    private void BeginEditing(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryPlayer(player, command, out _) || !HasSetupPermission(player!, command) || !CheckMap(command)) return;
        if (command.ArgCount != 2)
        {
            command.ReplyToCommand("Usage: !edit <track_name>");
            return;
        }
        if (_editors.ContainsKey(player!.Slot))
        {
            command.ReplyToCommand("[AWPER] A profile session already exists; use !abort before entering editing mode.");
            return;
        }
        try
        {
            var editor = EditorSession.BeginEditing(player.Slot, Server.MapName, command.GetArg(1));
            _editors[player.Slot] = editor;
            var overwrite = _profiles!.List(Server.MapName).Contains(editor.EditProfileName!, StringComparer.OrdinalIgnoreCase);
            command.ReplyToCommand(overwrite
                ? $"[AWPER] Editing '{editor.EditProfileName}'. Saving will replace the existing track; now record EditAnchor."
                : $"[AWPER] Editing '{editor.EditProfileName}'; now stand at the editing entrance and record EditAnchor.");
        }
        catch (ArgumentException ex)
        {
            command.ReplyToCommand($"[AWPER] Cannot enter editing mode: {ex.Message}");
        }
    }

    private void SetEditAnchor(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryEditingSession(player, command, out var editor, out var pawn)) return;
        editor.Draft.SetEditAnchor(new(ToVec(pawn.AbsOrigin!), ToAngles(pawn.EyeAngles)));
        command.ReplyToCommand($"[AWPER] EditAnchor recorded for '{editor.EditProfileName}'.");
    }

    private void SetPlayerAnchor(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryEditingSession(player, command, out var editor, out var pawn)) return;
        if (editor.Draft.EditAnchor is null)
        {
            command.ReplyToCommand("[AWPER] Record EditAnchor before PlayerAnchor.");
            return;
        }
        var origin = ToVec(pawn.AbsOrigin!);
        var eye = origin with { Z = origin.Z + pawn.ViewOffset.Z };
        var anchor = new PlayerAnchor(origin, eye, ToAngles(pawn.EyeAngles));
        editor.Draft.SetPlayerAnchor(anchor);
        editor.PlayerAnchor = anchor;
        pawn.TakesDamage = false;
        Teleport(pawn, editor.Draft.EditAnchor.Position, editor.Draft.EditAnchor.Angles);
        command.ReplyToCommand("[AWPER] PlayerAnchor recorded; walk normally to mark the Bot path.");
    }

    private void SetBotPoint(CCSPlayerController? player, CommandInfo command, BotPoint point)
    {
        if (!TryEditingSession(player, command, out var editor, out var pawn)) return;
        if (editor.Draft.PlayerAnchor is null)
        {
            command.ReplyToCommand("[AWPER] Record PlayerAnchor before Bot path points.");
            return;
        }
        var value = ToVec(pawn.AbsOrigin!);
        switch (point)
        {
            case BotPoint.Start: editor.Draft.SetBotStart(value); break;
            case BotPoint.End: editor.Draft.SetBotEnd(value); break;
            case BotPoint.Jiggle: editor.Draft.SetBotJiggle(value); break;
        }
        command.ReplyToCommand($"[AWPER] Bot{point} recorded at {value}.");
        ReplyPointProbe(command, $"bot.{point.ToString().ToLowerInvariant()}", value);
    }

    private void SetBotFacing(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryEditingSession(player, command, out var editor, out var pawn)) return;
        editor.Draft.SetBotFacingYaw(pawn.EyeAngles.Y);
        command.ReplyToCommand($"[AWPER] BotFacingYaw set to {pawn.EyeAngles.Y:0.0}.");
    }

    private void SetMode(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryEditingSession(player, command, out var editor, out _) || command.ArgCount < 2) { command.ReplyToCommand("Usage: !mode 1|2"); return; }
        var argument = command.GetArg(1);
        if (argument is not ("1" or "2")) { command.ReplyToCommand("Usage: !mode 1|2"); return; }
        var mode = argument == "2" ? TrainingMode.JiggleThenPeek : TrainingMode.DirectPeek;
        editor.Draft.SetMode(mode);
        command.ReplyToCommand($"[AWPER] Mode: {mode}.");
    }

    private void SetSpeed(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryEditingSession(player, command, out var editor, out _) || command.ArgCount < 2 ||
            !float.TryParse(command.GetArg(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
        {
            command.ReplyToCommand("Usage: !speed <1-215>");
            return;
        }
        try
        {
            editor.Draft.SetTargetSpeed(speed);
            command.ReplyToCommand($"[AWPER] Target speed: {speed:0.0} units/s.");
        }
        catch (ArgumentOutOfRangeException ex) { command.ReplyToCommand($"[AWPER] {ex.Message}"); }
    }

    private void Validate(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryEditingSession(player, command, out var editor, out _)) return;
        try
        {
            var candidate = editor.Draft.Build(editor.EditProfileName!);
            editor.PendingProfile = candidate;
            ReplyValidation(command, new ProfileValidator(_world).Validate(candidate, Server.MapName));
        }
        catch (Exception ex) { command.ReplyToCommand($"[AWPER] Invalid: {ex.Message}"); }
    }

    private void Save(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryEditingSession(player, command, out var editor, out _) || command.ArgCount != 1)
        {
            command.ReplyToCommand("Usage: !save (the track name was declared by !edit)");
            return;
        }
        try
        {
            var candidate = editor.Draft.Build(editor.EditProfileName!);
            editor.PendingProfile = candidate;
            var validation = new ProfileValidator(_world).Validate(candidate, Server.MapName);
            ReplyValidation(command, validation);
            if (!validation.IsValid)
            {
                command.ReplyToCommand($"[AWPER] Save refused: {_world.CompatibilityMessage}");
                return;
            }
            candidate = candidate with { ValidatedMapFingerprint = _world.CurrentMapFingerprint };
            _profiles!.SaveAsync(candidate).GetAwaiter().GetResult();
            editor.PendingProfile = candidate;
            editor.LoadedProfile = candidate;
            editor.PlayerAnchor = candidate.PlayerAnchor;
            editor.FinishEditing();
            RestoreEditor(editor);
            command.ReplyToCommand($"[AWPER] Saved and loaded '{candidate.ProfileName}'; editing mode ended.");
        }
        catch (Exception ex) { command.ReplyToCommand($"[AWPER] Save failed: {ex.Message}"); }
    }

    private void LoadProfile(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryPlayer(player, command, out _) || !CheckMap(command) || command.ArgCount < 2) { command.ReplyToCommand("Usage: !load <name>"); return; }
        if (_editors.TryGetValue(player!.Slot, out var active) && active.IsEditing)
        {
            command.ReplyToCommand($"[AWPER] Finish or abort editing '{active.EditProfileName}' before loading another track.");
            return;
        }
        try
        {
            var profile = _profiles!.LoadAsync(Server.MapName, command.GetArg(1)).GetAwaiter().GetResult();
            var validation = new ProfileValidator(_world).Validate(profile, Server.MapName);
            ReplyValidation(command, validation);
            if (!validation.IsValid)
            {
                command.ReplyToCommand($"[AWPER] Load refused after live revalidation: {_world.CompatibilityMessage}");
                return;
            }
            var editor = _editors.GetValueOrDefault(player!.Slot) ?? EditorSession.CreateTrainingSession(player.Slot, Server.MapName);
            editor.LoadedProfile = profile;
            editor.PlayerAnchor = profile.PlayerAnchor;
            editor.CameraVerified = false;
            _editors[player.Slot] = editor;
            command.ReplyToCommand($"[AWPER] Loaded '{profile.ProfileName}' after live map validation; click Mouse4 to enter preview, then click again to exit and verify it.");
        }
        catch (Exception ex) { command.ReplyToCommand($"[AWPER] Load failed: {ex.Message}"); }
    }

    private void ListProfiles(CCSPlayerController? player, CommandInfo command)
    {
        if (!CheckMap(command)) return;
        var names = _profiles!.List(Server.MapName);
        command.ReplyToCommand(names.Count == 0 ? "[AWPER] No profiles." : $"[AWPER] {string.Join(", ", names)}");
    }

    private void DeleteProfile(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !HasSetupPermission(player, command) || !CheckMap(command) || command.ArgCount < 2)
        {
            command.ReplyToCommand("Usage: !delete <name>");
            return;
        }
        try
        {
            var deleted = _profiles!.Delete(Server.MapName, command.GetArg(1));
            if (deleted)
            {
                var normalized = ProfileNames.Normalize(command.GetArg(1));
                foreach (var editor in _editors.Values.Where(x => x.LoadedProfile?.ProfileName == normalized))
                {
                    editor.LoadedProfile = null;
                    editor.CameraVerified = false;
                }
            }
            command.ReplyToCommand(deleted ? "[AWPER] Profile deleted." : "[AWPER] Profile was not found.");
        }
        catch (Exception ex) { command.ReplyToCommand($"[AWPER] Delete failed: {ex.Message}"); }
    }

    private void CopyProfile(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !HasSetupPermission(player, command) || !CheckMap(command)) return;
        if (command.ArgCount != 4 || !TryParseTargetSpeed(command.GetArg(3), out var speed))
        {
            command.ReplyToCommand("Usage: !copy <source_name> <new_name> <1-215>");
            return;
        }
        try
        {
            var sourceName = ProfileNames.Normalize(command.GetArg(1));
            var newName = ProfileNames.Normalize(command.GetArg(2));
            if (_profiles!.List(Server.MapName).Contains(newName, StringComparer.OrdinalIgnoreCase))
            {
                command.ReplyToCommand($"[AWPER] Copy refused: destination profile '{newName}' already exists.");
                return;
            }
            var source = _profiles.LoadAsync(Server.MapName, sourceName).GetAwaiter().GetResult();
            var copy = ProfileVariants.CopyWithSpeed(source, newName, speed);
            _profiles.SaveAsync(copy).GetAwaiter().GetResult();
            command.ReplyToCommand($"[AWPER] Copied '{source.ProfileName}' to '{copy.ProfileName}' at {speed:0.0} units/s; the source and current loaded profile are unchanged.");
        }
        catch (Exception ex) { command.ReplyToCommand($"[AWPER] Copy failed: {ex.Message}"); }
    }

    private void StartRound(CCSPlayerController? player, CommandInfo command)
        => StartRoundCore(player, command, null);

    private void StartRoundWithSpeed(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount != 2 || !TryParseTargetSpeed(command.GetArg(1), out var speed))
        {
            command.ReplyToCommand("Usage: !start_speed <1-215>");
            return;
        }
        StartRoundCore(player, command, speed);
    }

    private void StartRoundCore(CCSPlayerController? player, CommandInfo command, float? speedOverride)
    {
        if (!TryPlayer(player, command, out _) || !CheckMap(command) || !_editors.TryGetValue(player!.Slot, out var editor) || editor.LoadedProfile is null)
        { command.ReplyToCommand("[AWPER] Load a verified profile first."); return; }
        if (_runtime is not null) { command.ReplyToCommand("[AWPER] Another round is already active."); return; }
        if (!_bots.IsCompatible) { command.ReplyToCommand($"[AWPER] {_bots.CompatibilityMessage}"); return; }
        if (!editor.CameraVerified) { command.ReplyToCommand("[AWPER] Click Mouse4 to enter preview, then click it again to exit before starting."); return; }
        var runtimeProfile = speedOverride is { } speed
            ? ProfileVariants.WithSpeed(editor.LoadedProfile, speed)
            : editor.LoadedProfile;
        var validation = new ProfileValidator(_world).Validate(runtimeProfile, Server.MapName);
        if (!validation.IsValid)
        {
            ReplyValidation(command, validation);
            command.ReplyToCommand($"[AWPER] Start refused: {_world.CompatibilityMessage}");
            return;
        }

        _menu!.Close(player);
        _cameras.End(player.Slot);
        var expectedBotTeam = player.Team == CsTeam.CounterTerrorist
            ? CsTeam.Terrorist
            : CsTeam.CounterTerrorist;
        var botTeamToken = expectedBotTeam == CsTeam.Terrorist ? "t" : "ct";
        ConfigureTrainingBotEnvironment(botTeamToken);
        var seed = unchecked((ulong)DateTime.UtcNow.Ticks ^ ((ulong)player.Slot << 32) ^ (uint)++_nextGeneration);
        var machine = new TrainingStateMachine();
        machine.Start(runtimeProfile, Server.CurrentTime, seed);
        var runtime = new RuntimeSession(_nextGeneration, player.Slot, expectedBotTeam, runtimeProfile, machine,
            Server.CurrentTime + 4.0);
        if (Config.EnableMotionCsv)
        {
            try
            {
                runtime.Telemetry = MotionTelemetry.Create(_evidenceDirectory!, runtime.Generation, seed, runtime.Profile);
                Logger.LogInformation("Motion telemetry: {Path}", runtime.Telemetry.Path);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Motion CSV was enabled but could not be created.");
                command.ReplyToCommand($"[AWPER] Start refused: motion CSV could not be created ({ex.Message}).");
                return;
            }
        }
        _runtime = runtime;
        Server.ExecuteCommand("bot_quota 1");
        Logger.LogInformation(
            "Training generation {Generation} requested exactly one {Team} Bot via bot_quota normal; seed={Seed}.",
            runtime.Generation,
            expectedBotTeam,
            seed);
        var speedNote = speedOverride is null ? string.Empty : $" one-round-speed={runtimeProfile.Training.TargetSpeed:0.0};";
        command.ReplyToCommand($"[AWPER] Preparing round {runtime.Generation};{speedNote} seed={seed}.");
    }

    private void Status(CCSPlayerController? player, CommandInfo command)
    {
        var editing = player is not null && _editors.TryGetValue(player.Slot, out var editor) && editor.IsEditing
            ? editor.EditProfileName
            : "none";
        command.ReplyToCommand($"[AWPER] bot={_bots.CompatibilityMessage} world={_world.CompatibilityMessage} camera={CameraStatus(player)} editing={editing} runtime={_runtime?.Machine.State.ToString() ?? "none"} native={_bots.GetNativeDiagnostics()}");
    }

    private void ChatHelp(CCSPlayerController? player, CommandInfo command)
    {
        command.ReplyToCommand("[AWPER] F5 / !ui: open the control panel.");
        command.ReplyToCommand("[AWPER] Create: !edit <name> -> !set_edit_anchor -> mark remaining points -> !save.");
        command.ReplyToCommand("[AWPER] !load <name> | !start | !abort | !status");
        command.ReplyToCommand("[AWPER] !start_speed <1-215> | !copy <source> <new> <1-215>");
        command.ReplyToCommand("[AWPER] !maps | !map <dust2|inferno|mirage|anubis|ancient|nuke|cache>");
        command.ReplyToCommand("[AWPER] Setup commands are also available in chat as !set_...; use the F5 panel for the complete workflow.");
    }

    private void ListMaps(CCSPlayerController? player, CommandInfo command)
    {
        var current = MapPolicy.Normalize(Server.MapName);
        var maps = _maps.AllowedMaps.Select(map =>
            string.Equals(map, current, StringComparison.OrdinalIgnoreCase)
                ? $"{map[3..]}(current)"
                : map[3..]);
        command.ReplyToCommand($"[AWPER] Maps: {string.Join(" -> ", maps)}");
    }

    private void ChangeMap(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid || player.IsBot || !HasMapPermission(player, command)) return;
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand("Usage: !map <dust2|inferno|mirage|anubis|ancient|nuke|cache>");
            return;
        }

        var map = MapPolicy.NormalizeAlias(command.GetArg(1));
        if (!_maps.IsAllowed(map))
        {
            command.ReplyToCommand($"[AWPER] '{command.GetArg(1)}' is not in the configured seven-map pool.");
            return;
        }
        if (string.Equals(MapPolicy.Normalize(Server.MapName), map, StringComparison.OrdinalIgnoreCase))
        {
            command.ReplyToCommand($"[AWPER] Already running {map}.");
            return;
        }

        _menu!.CloseAll();
        _cameras.EndAll();
        AbortRuntime(FinishReason.Aborted);
        foreach (var editor in _editors.Values.ToArray()) RestoreEditor(editor);
        _editors.Clear();
        command.ReplyToCommand($"[AWPER] Changing map to {map}...");
        Server.NextFrame(() => Server.ExecuteCommand($"changelevel {map}"));
    }

    private void ToggleHud(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryPlayer(player, command, out _)) return;
        if (_cameras.End(player!.Slot) && _editors.TryGetValue(player.Slot, out var editor))
            editor.CameraVerified = true;
        var opened = _menu!.Toggle(player!);
        command.ReplyToCommand(opened
            ? "[AWPER] Menu opened; use number keys to select, F5 to close."
            : "[AWPER] Menu closed.");
    }

    private void BeginPreview(CCSPlayerController? player, CommandInfo command)
    {
        if (!TrySession(player, command, out var editor, out var pawn) || editor.PlayerAnchor is null)
        { command.ReplyToCommand("[AWPER] Record PlayerAnchor first."); return; }
        var botPosition = editor.LoadedProfile?.BotPath.Start
            ?? editor.Draft.BotStart
            ?? ToVec(pawn.AbsOrigin!);
        var botStance = editor.LoadedProfile?.BotPath.Stance ?? Stance.Standing;
        var botFacingYaw = editor.LoadedProfile?.BotPath.FacingYaw
            ?? editor.Draft.BotFacingYaw
            ?? MotionMath.YawFacing(botPosition, editor.PlayerAnchor.PawnPosition);
        var started = _cameras.Begin(
            player!, editor.PlayerAnchor, botPosition, botStance, botFacingYaw, out var diagnostic);
        command.ReplyToCommand($"[AWPER] Preview {(started ? "started" : "failed")}: {diagnostic}");
    }

    private void EndPreview(CCSPlayerController? player, CommandInfo command)
    {
        var completed = player is not null && _cameras.End(player.Slot);
        if (completed && _editors.TryGetValue(player!.Slot, out var editor)) editor.CameraVerified = true;
        command.ReplyToCommand(completed
            ? "[AWPER] Preview ended; camera verification passed for this session."
            : "[AWPER] No active preview was found.");
    }

    private void TogglePreview(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null && _cameras.IsActive(player.Slot))
        {
            EndPreview(player, command);
            return;
        }
        BeginPreview(player, command);
    }

    private void Abort(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null && _runtime?.PlayerSlot == player.Slot) AbortRuntime(FinishReason.Aborted);
        if (player is not null) _menu!.Close(player);
        if (player is not null) _cameras.End(player.Slot);
        if (player is not null && _editors.Remove(player.Slot, out var editor)) RestoreEditor(editor);
        command.ReplyToCommand("[AWPER] Session aborted and recoverable state restored.");
    }

    private void OnTick()
    {
        foreach (var slot in _cameras.ActiveSlots)
        {
            if (!IsAlive(Utilities.GetPlayerFromSlot(slot)) || !_cameras.Refresh(slot)) _cameras.End(slot);
        }
        var runtime = _runtime;
        if (runtime is null) return;
        try { TickRuntime(runtime); }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Training generation {Generation} failed in OnTick.", runtime.Generation);
            AbortRuntime(FinishReason.RuntimeFailure);
        }
    }

    private void OnMapEnd()
    {
        AbortRuntime(FinishReason.Aborted);
        _menu?.CloseAll();
        _cameras.Shutdown();
        foreach (var editor in _editors.Values.ToArray()) RestoreEditor(editor);
        _editors.Clear();
    }

    private void OnMapStart(string mapName) { }

    private void OnClientDisconnect(int playerSlot)
    {
        _cameras.End(playerSlot);
        if (_runtime?.PlayerSlot == playerSlot) AbortRuntime(FinishReason.Aborted);
        _editors.Remove(playerSlot);
    }

    private void AbortRuntime(FinishReason reason)
    {
        var runtime = _runtime;
        if (runtime is null) return;
        ApplyActions(runtime, runtime.Machine.Abort(Server.CurrentTime, reason));
        CompleteReset(runtime, force: true);
    }

    private void TickRuntime(RuntimeSession runtime)
    {
        if (_runtime != runtime) return;
        if (runtime.ResetRequested)
        {
            CompleteReset(runtime);
            return;
        }
        MaintainTrainingBotRoster(runtime);

        var player = Utilities.GetPlayerFromSlot(runtime.PlayerSlot);
        if (runtime.Machine.State == TrainingState.Prepare)
        {
            TryPrepare(runtime, player);
            return;
        }

        var bot = Utilities.GetPlayerFromSlot(runtime.BotSlot);
        var playerAlive = IsAlive(player);
        var botAlive = IsAlive(bot);
        WriteTelemetry(runtime, bot, botAlive);
        var stuck = false;
        if (botAlive && runtime.Machine.State == TrainingState.BotMoving && bot!.PlayerPawn.Value?.AbsOrigin is { } movingOrigin)
        {
            var position = ToVec(movingOrigin);
            if (position.HorizontalDistanceTo(runtime.LastBotPosition) >= 1f)
            {
                runtime.LastBotPosition = position;
                runtime.LastProgressTime = Server.CurrentTime;
            }
            stuck = Server.CurrentTime - runtime.LastProgressTime >= 1.5;
            if (stuck)
                Logger.LogWarning(
                    "Training generation {Generation} detected zero progress; native {Diagnostics}.",
                    runtime.Generation,
                    _bots.GetNativeDiagnostics());
        }

        ApplyActions(runtime, runtime.Machine.Tick(Server.CurrentTime, playerAlive, botAlive, stuck));
        if (_runtime == runtime && runtime.Machine.State == TrainingState.Countdown)
        {
            var shown = Math.Max(0, (int)Math.Ceiling(runtime.CountdownEndsAt - Server.CurrentTime));
            if (shown != runtime.LastCountdownShown)
            {
                runtime.LastCountdownShown = shown;
                player?.PrintToCenter(shown > 0 ? shown.ToString() : "READY");
            }
        }
        if (_runtime != runtime || runtime.ResetRequested || runtime.Machine.State != TrainingState.BotMoving || !botAlive) return;

        var pawn = bot!.PlayerPawn.Value!;
        var segment = runtime.Machine.CurrentSegment!;
        var current = ToVec(pawn.AbsOrigin!);
        if (current.HorizontalDistanceTo(segment.Target) <= runtime.Profile.Training.CompletionRadius)
        {
            ApplyActions(runtime, runtime.Machine.TargetReached(Server.CurrentTime));
            return;
        }
        if (!MoveBot(runtime, current, segment.Target))
            FailRuntime(runtime, "BotController rejected a movement update.");

    }

    private void TryPrepare(RuntimeSession runtime, CCSPlayerController? player)
    {
        if (!IsAlive(player))
        {
            FailRuntime(runtime, "Training player is no longer alive during Prepare.");
            return;
        }

        var bot = runtime.BotSlot >= 0 ? Utilities.GetPlayerFromSlot(runtime.BotSlot) : null;
        if (bot is { IsValid: true } && (!bot.IsBot || bot.Team != runtime.ExpectedBotTeam)) bot = null;
        if (bot is null || !bot.IsValid)
        {
            bot = Utilities.GetPlayers().FirstOrDefault(x =>
                x is { IsValid: true, IsBot: true }
                && x.Team == runtime.ExpectedBotTeam);
            if (bot is not null)
            {
                runtime.BotSlot = bot.Slot;
                runtime.BotReadyAt = Server.CurrentTime + BotSpawnSettleSeconds;
                MaintainTrainingBotRoster(runtime);
                Logger.LogInformation(
                    "Training generation {Generation} detected Bot slot {BotSlot} on team {Team}; waiting {Delay:0.00}s for Pawn/model initialization.",
                    runtime.Generation,
                    runtime.BotSlot,
                    runtime.ExpectedBotTeam,
                    BotSpawnSettleSeconds);
            }
        }
        if (bot is null || !bot.IsValid)
        {
            if (Server.CurrentTime >= runtime.SpawnDeadline) FailRuntime(runtime, "bot_add did not create the expected training Bot within four seconds.");
            return;
        }
        if (!IsAlive(bot))
        {
            if (!runtime.BotRespawnRequested)
            {
                runtime.BotRespawnRequested = true;
                bot.Respawn();
                runtime.BotReadyAt = Server.CurrentTime + BotSpawnSettleSeconds;
            }
            if (Server.CurrentTime >= runtime.SpawnDeadline) FailRuntime(runtime, "The training Bot did not become alive within four seconds.");
            return;
        }
        if (Server.CurrentTime < runtime.BotReadyAt) return;

        var botPawn = bot.PlayerPawn.Value!;
        if (!_bots.Begin(bot.Slot, botPawn.Handle))
        {
            FailRuntime(runtime, $"BotController could not bind and lock the live Pawn for slot {bot.Slot}; {_bots.CompatibilityMessage}");
            return;
        }
        Logger.LogInformation(
            "Training generation {Generation} bound BotController carrier for slot {BotSlot}; native {Diagnostics}.",
            runtime.Generation,
            bot.Slot,
            _bots.GetNativeDiagnostics());

        EnsureBotVisible(botPawn);
        bot.RemoveWeapons();
        bot.GiveNamedItem("weapon_knife");
        runtime.TrainingWeapon = bot.GiveNamedItem<CBasePlayerWeapon>("weapon_ak47");
        if (runtime.TrainingWeapon is not { IsValid: true })
        {
            FailRuntime(runtime, "The training AK-47 entity could not be created.");
            return;
        }
        if (!_bots.EquipAk47())
        {
            FailRuntime(runtime, "BotController could not equip the training Bot with an AK-47.");
            return;
        }
        botPawn.Health = 100;
        botPawn.TakesDamage = false;
        Teleport(botPawn, runtime.Profile.BotPath.Start, new EulerAngles(0, runtime.Profile.BotPath.FacingYaw, 0));
        runtime.LastBotPosition = runtime.Profile.BotPath.Start;
        runtime.LastProgressTime = Server.CurrentTime;
        ApplyActions(runtime, runtime.Machine.Prepared(Server.CurrentTime));
        Logger.LogInformation(
            "Training generation {Generation} prepared visible Bot slot {BotSlot}; model={Model}.",
            runtime.Generation,
            runtime.BotSlot,
            GetBotModelName(botPawn));
    }

    private void ApplyActions(RuntimeSession runtime, IReadOnlyList<TrainingAction> actions)
    {
        foreach (var action in actions)
        {
            if (_runtime != runtime) return;
            switch (action)
            {
                case BeginCountdownAction countdown:
                    runtime.CountdownEndsAt = Server.CurrentTime + countdown.Seconds;
                    runtime.LastCountdownShown = -1;
                    break;
                case BeginMotionAction motion:
                    if (Utilities.GetPlayerFromSlot(runtime.BotSlot)?.PlayerPawn.Value is { IsValid: true } botPawn)
                    {
                        botPawn.TakesDamage = true;
                        botPawn.Teleport(null, null, Vector.Zero);
                    }
                    runtime.LastProgressTime = Server.CurrentTime;
                    runtime.LastBotPosition = GetBotPosition(runtime);
                    if (!StartBotMovement(runtime, runtime.LastBotPosition, motion.Segment.Target))
                    {
                        FailRuntime(runtime, "BotController rejected movement start.");
                        return;
                    }
                    Logger.LogInformation(
                        "Training generation {Generation} started segment {Segment}; native {Diagnostics}.",
                        runtime.Generation,
                        motion.Segment.Label,
                        _bots.GetNativeDiagnostics());
                    break;
                case StopMotionAction:
                    _bots.StopMovement();
                    if (Utilities.GetPlayerFromSlot(runtime.BotSlot)?.PlayerPawn.Value is { IsValid: true } stoppedPawn)
                        stoppedPawn.Teleport(null, null, Vector.Zero);
                    break;
                case FinishRoundAction finish:
                    Utilities.GetPlayerFromSlot(runtime.PlayerSlot)?.PrintToChat($"[AWPER] Round finished: {finish.Reason}.");
                    Logger.LogInformation("Training generation {Generation} finished: {Reason}; seed={Seed}; jiggles={Jiggles}.",
                        runtime.Generation, finish.Reason, runtime.Machine.Seed, runtime.Machine.JiggleCount);
                    break;
                case ResetRoundAction reset:
                    runtime.ResetReason = reset.Reason;
                    runtime.ResetRequested = true;
                    BeginCleanup(runtime);
                    break;
            }
        }
    }

    private bool MoveBot(RuntimeSession runtime, Vec3 from, Vec3 to)
    {
        var pawn = Utilities.GetPlayerFromSlot(runtime.BotSlot)?.PlayerPawn.Value;
        if (pawn is not { IsValid: true }) return false;
        var move = MotionMath.ProjectWorldDirection(from, to, runtime.Profile.BotPath.FacingYaw);
        var scale = Math.Clamp(runtime.Profile.Training.TargetSpeed / ReplayPathBuilder.Ak47NormalMoveSpeed, 0.05f, 1f);
        runtime.LastCommandForward = move.Forward * scale;
        runtime.LastCommandLeft = move.Left * scale;
        runtime.LastCommandYaw = runtime.Profile.BotPath.FacingYaw;
        return _bots.IsMovementActive;
    }

    private bool StartBotMovement(RuntimeSession runtime, Vec3 from, Vec3 to)
    {
        var pawn = Utilities.GetPlayerFromSlot(runtime.BotSlot)?.PlayerPawn.Value;
        if (pawn is not { IsValid: true }) return false;
        var move = MotionMath.ProjectWorldDirection(from, to, runtime.Profile.BotPath.FacingYaw);
        var scale = Math.Clamp(runtime.Profile.Training.TargetSpeed / ReplayPathBuilder.Ak47NormalMoveSpeed, 0.05f, 1f);
        runtime.LastCommandForward = move.Forward * scale;
        runtime.LastCommandLeft = move.Left * scale;
        runtime.LastCommandYaw = runtime.Profile.BotPath.FacingYaw;
        var replay = ReplayPathBuilder.Build(from, to, runtime.Profile.Training.TargetSpeed,
            runtime.Profile.BotPath.FacingYaw, CurrentGroundMovementSettings());
        return _bots.StartMovement(pawn.Handle, replay);
    }

    private static ReplayPathBuilder.GroundMovementSettings CurrentGroundMovementSettings()
        => new(
            Server.TickInterval,
            ReadMovementConVar("sv_accelerate", ReplayPathBuilder.SourceGroundAcceleration),
            ReadMovementConVar("sv_friction", ReplayPathBuilder.SourceGroundFriction),
            ReadMovementConVar("sv_stopspeed", ReplayPathBuilder.SourceStopSpeed));

    private static float ReadMovementConVar(string name, float fallback)
    {
        try
        {
            return ConVar.Find(name)?.GetPrimitiveValue<float>() is { } value && float.IsFinite(value)
                ? value
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private Vec3 GetBotPosition(RuntimeSession runtime)
        => Utilities.GetPlayerFromSlot(runtime.BotSlot)?.PlayerPawn.Value?.AbsOrigin is { } origin
            ? ToVec(origin)
            : runtime.Profile.BotPath.Start;

    private void FailRuntime(RuntimeSession runtime, string message)
    {
        if (_runtime != runtime || runtime.ResetRequested) return;
        Logger.LogError("Training generation {Generation} failed closed: {Message}", runtime.Generation, message);
        Utilities.GetPlayerFromSlot(runtime.PlayerSlot)?.PrintToChat($"[AWPER] Runtime failure: {message}");
        ApplyActions(runtime, runtime.Machine.Abort(Server.CurrentTime, FinishReason.RuntimeFailure));
    }

    private void BeginCleanup(RuntimeSession runtime)
    {
        if (runtime.CleanupStarted) return;
        runtime.CleanupStarted = true;
        runtime.Telemetry?.Dispose();
        runtime.Telemetry = null;
        _bots.Cleanup();
        _cameras.End(runtime.PlayerSlot);
        RemoveTrainingWeapon(runtime);
        var bot = Utilities.GetPlayerFromSlot(runtime.BotSlot);
        if (bot?.PlayerPawn.Value is { IsValid: true } botPawn) botPawn.TakesDamage = false;
        Server.ExecuteCommand("bot_quota 0");
        Server.ExecuteCommand("bot_kick");
        Server.ExecuteCommand("bot_join_team any");
        Server.ExecuteCommand("mp_respawn_on_death_t 1");
        Server.ExecuteCommand("mp_respawn_on_death_ct 1");
    }

    private void RemoveTrainingWeapon(RuntimeSession runtime)
    {
        var weapon = runtime.TrainingWeapon;
        runtime.TrainingWeapon = null;
        if (weapon is not { IsValid: true }) return;
        try
        {
            var entityIndex = weapon.Index;
            weapon.Remove();
            Logger.LogInformation(
                "Training generation {Generation} removed owned AK-47 entity {EntityIndex} during cleanup.",
                runtime.Generation,
                entityIndex);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "Training generation {Generation} could not remove its owned AK-47 entity during cleanup.",
                runtime.Generation);
        }
    }

    private void CompleteReset(RuntimeSession runtime, bool force = false)
    {
        if (_runtime != runtime) return;
        BeginCleanup(runtime);
        var player = Utilities.GetPlayerFromSlot(runtime.PlayerSlot);
        if (player is null || !player.IsValid)
        {
            _runtime = null;
            return;
        }
        if (!IsAlive(player))
        {
            if (!runtime.PlayerRespawnRequested)
            {
                runtime.PlayerRespawnRequested = true;
                try { player.Respawn(); }
                catch (Exception ex) { Logger.LogWarning(ex, "Could not respawn training player during reset."); }
            }
            if (!force) return;
        }
        if (runtime.Machine.State is TrainingState.Reset or TrainingState.Aborted) runtime.Machine.ResetCompleted();
        _runtime = null;
        player.PrintToChat($"[AWPER] Ready. Previous round: {runtime.ResetReason}.");
    }

    private void MaintainTrainingBotRoster(RuntimeSession runtime)
    {
        if (runtime.BotSlot < 0) return;
        var unexpected = Utilities.GetPlayers().FirstOrDefault(bot =>
            bot is { IsValid: true, IsBot: true }
            && (bot.Slot != runtime.BotSlot || bot.Team != runtime.ExpectedBotTeam));
        if (unexpected is null) return;

        // Never Remove() fake clients one-by-one: the quota manager can replace a
        // removed client before the next tick and exhaust every slot. Freeze quota
        // first, kick the whole roster once, then let the runtime fail closed.
        Logger.LogError(
            "Unexpected Bot slot {BotSlot} team {Team} during generation {Generation}; expected slot {ExpectedSlot} team {ExpectedTeam}. Stopping quota before roster cleanup.",
            unexpected.Slot,
            unexpected.Team,
            runtime.Generation,
            runtime.BotSlot,
            runtime.ExpectedBotTeam);
        Server.ExecuteCommand("bot_quota 0");
        Server.ExecuteCommand("bot_kick");
        FailRuntime(runtime, "More than one Bot entered the controlled roster.");
    }

    private static void ConfigureTrainingBotEnvironment(string botTeamToken)
    {
        Server.ExecuteCommand("bot_quota 0");
        Server.ExecuteCommand("bot_quota_mode normal");
        Server.ExecuteCommand($"bot_join_team {botTeamToken}");
        Server.ExecuteCommand("mp_autoteambalance 0");
        Server.ExecuteCommand("mp_limitteams 0");
        Server.ExecuteCommand("mp_respawn_on_death_t 0");
        Server.ExecuteCommand("mp_respawn_on_death_ct 0");
        Server.ExecuteCommand("bot_kick");
    }

    private static void EnsureBotVisible(CCSPlayerPawn pawn)
    {
        var hiddenEffects = (uint)(EntityEffects_t.EF_NODRAW | EntityEffects_t.EF_NODRAW_BUT_TRANSMIT);
        pawn.Effects &= ~hiddenEffects;
        pawn.RenderMode = RenderMode_t.kRenderNormal;
        pawn.Render = Color.White;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_fEffects");
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_nRenderMode");
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
    }

    private static string GetBotModelName(CCSPlayerPawn pawn)
    {
        try
        {
            var model = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName;
            return string.IsNullOrWhiteSpace(model) ? "<unreported>" : model;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private void WriteTelemetry(RuntimeSession runtime, CCSPlayerController? bot, bool botAlive)
    {
        if (runtime.Telemetry is null || runtime.TickNumber++ % Config.MotionCsvSampleEveryTicks != 0) return;
        var pawn = bot?.PlayerPawn.Value is { IsValid: true } livePawn ? livePawn : null;
        var origin = pawn?.AbsOrigin is { } rawOrigin ? ToVec(rawOrigin) : runtime.LastBotPosition;
        var velocity = pawn is not null ? ToVec(pawn.AbsVelocity) : Vec3.Zero;
        var target = runtime.Machine.CurrentSegment?.Target;
        var distance = target is { } value ? origin.HorizontalDistanceTo(value) : 0;
        runtime.Telemetry.Write(runtime.Generation, runtime.Machine.Seed, Server.CurrentTime, runtime.Machine.State,
            runtime.Machine.CurrentSegment?.Label ?? string.Empty, origin, velocity, runtime.Profile.Training.TargetSpeed,
            distance, botAlive, runtime.LastCommandForward, runtime.LastCommandLeft, runtime.LastCommandYaw);
    }

    private void RestoreEditor(EditorSession editor)
    {
        var player = Utilities.GetPlayerFromSlot(editor.PlayerSlot);
        if (editor.PlayerAnchor is not null && player?.PlayerPawn.Value is { IsValid: true } pawn)
        {
            pawn.TakesDamage = true;
            Teleport(pawn, editor.PlayerAnchor.PawnPosition, editor.PlayerAnchor.EyeAngles);
        }
    }

    private bool CheckMap(CommandInfo command)
    {
        if (_maps.IsAllowed(Server.MapName)) return true;
        command.ReplyToCommand($"[AWPER] Map '{Server.MapName}' is not in the seven-map whitelist.");
        return false;
    }

    private static void ReplyValidation(CommandInfo command, ValidationResult result)
    {
        if (result.Issues.Count == 0)
        {
            command.ReplyToCommand("[AWPER] Validation passed with no issues.");
            return;
        }
        foreach (var issue in result.Issues)
            command.ReplyToCommand($"[AWPER] {issue.Severity}: {issue.Code} - {issue.Message}");
        command.ReplyToCommand($"[AWPER] Validation {(result.IsValid ? "passed with warnings" : "failed")}.");
    }

    private void ReplyPointProbe(CommandInfo command, string name, Vec3 point)
    {
        var fit = _world.CanFitStandingPlayer(point);
        var ground = _world.HasStandableGround(point, out var normalZ);
        command.ReplyToCommand(fit && ground && normalZ >= 0.7f
            ? $"[AWPER] {name} local geometry check passed (ground normal Z={normalZ:0.00})."
            : $"[AWPER] {name} local geometry warning: fit={fit}, ground={ground}, normalZ={normalZ:0.00}; {_world.CompatibilityMessage}");
    }

    private string CameraStatus(CCSPlayerController? player)
        => player is not null && _editors.TryGetValue(player.Slot, out var editor) && editor.CameraVerified
            ? "verified-this-session"
            : "unverified-this-session";

    private static bool TryPlayer(CCSPlayerController? player, CommandInfo command, out CCSPlayerPawn pawn)
    {
        pawn = null!;
        if (player is null || !player.IsValid || player.IsBot || !player.PawnIsAlive || player.PlayerPawn.Value is not { IsValid: true } value)
        { command.ReplyToCommand("[AWPER] A living human player is required."); return false; }
        pawn = value;
        return true;
    }

    private bool TryEditingSession(CCSPlayerController? player, CommandInfo command, out EditorSession editor, out CCSPlayerPawn pawn)
    {
        editor = null!;
        if (!TryPlayer(player, command, out pawn) || !HasSetupPermission(player!, command)) return false;
        if (!_editors.TryGetValue(player!.Slot, out editor!) || !editor.IsEditing)
        {
            command.ReplyToCommand("[AWPER] Enter editing mode first with !edit <track_name>.");
            return false;
        }
        return true;
    }

    private bool TrySession(CCSPlayerController? player, CommandInfo command, out EditorSession editor, out CCSPlayerPawn pawn)
    {
        editor = null!;
        if (!TryPlayer(player, command, out pawn)) return false;
        if (!_editors.TryGetValue(player!.Slot, out editor!))
        { command.ReplyToCommand("[AWPER] Record or load a profile first."); return false; }
        return true;
    }

    private static bool HasSetupPermission(CCSPlayerController player, CommandInfo command)
    {
        if (AdminManager.PlayerHasPermissions(player, "@css/config")) return true;
        command.ReplyToCommand("[AWPER] This setup command requires @css/config.");
        return false;
    }

    private static bool HasMapPermission(CCSPlayerController player, CommandInfo command)
    {
        if (AdminManager.PlayerHasPermissions(player, "@css/changemap")
            || AdminManager.PlayerHasPermissions(player, "@css/config")) return true;
        command.ReplyToCommand("[AWPER] Map switching requires @css/changemap or @css/config.");
        return false;
    }

    private static bool TryParseTargetSpeed(string value, out float speed)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out speed)) return false;
        try
        {
            TrainingSettings.RequireValidTargetSpeed(speed);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool IsAlive(CCSPlayerController? value)
        => value is { IsValid: true, PawnIsAlive: true } && value.PlayerPawn.Value is { IsValid: true };
    private static Vec3 ToVec(Vector value) => new(value.X, value.Y, value.Z);
    private static EulerAngles ToAngles(QAngle value) => new(value.X, value.Y, value.Z);
    private static void Teleport(CCSPlayerPawn pawn, Vec3 position, EulerAngles angles)
        => pawn.Teleport(new Vector(position.X, position.Y, position.Z), new QAngle(angles.Pitch, angles.Yaw, angles.Roll), Vector.Zero);

    private enum BotPoint { Start, End, Jiggle }
    private sealed class RuntimeSession(
        int generation,
        int playerSlot,
        CsTeam expectedBotTeam,
        AwperProfile profile,
        TrainingStateMachine machine,
        double spawnDeadline)
    {
        public int Generation { get; } = generation;
        public int PlayerSlot { get; } = playerSlot;
        public int BotSlot { get; set; } = -1;
        public CsTeam ExpectedBotTeam { get; } = expectedBotTeam;
        public AwperProfile Profile { get; } = profile;
        public TrainingStateMachine Machine { get; } = machine;
        public double SpawnDeadline { get; } = spawnDeadline;
        public bool BotRespawnRequested { get; set; }
        public double BotReadyAt { get; set; }
        public Vec3 LastBotPosition { get; set; }
        public double LastProgressTime { get; set; }
        public double CountdownEndsAt { get; set; }
        public int LastCountdownShown { get; set; } = -1;
        public bool ResetRequested { get; set; }
        public bool CleanupStarted { get; set; }
        public bool PlayerRespawnRequested { get; set; }
        public FinishReason ResetReason { get; set; } = FinishReason.Aborted;
        public MotionTelemetry? Telemetry { get; set; }
        public int TickNumber { get; set; }
        public float LastCommandForward { get; set; }
        public float LastCommandLeft { get; set; }
        public float LastCommandYaw { get; set; }
        public CBasePlayerWeapon? TrainingWeapon { get; set; }
    }
}
