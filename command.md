# AWPER 1.1.0 Command Manual

[English](command.md) · [Русский](command.ru.md) · [中文](command.zh-CN.md)

This document lists every AWPER command available to players in the current version, including keybindings, point placement, configuration management, camera, training control, and map switching.

Starting from this version, all plugin commands have dropped the `awper` prefix. For example, the old `!awper_start` / `css_awper_start` is now `!start` / `css_start`; the old commands are no longer registered. The `awper_bindings` in `exec awper_bindings` is a config file name, so it is not affected by this change.

In chat, commands start with `!` or `/`; in the console, use the full `css_` prefix. For example, the following three are equivalent:

```text
!start
/start
css_start
```

## 1. Common keybindings

| Key | Purpose | Command |
|---|---|---|
| `F5` | Open or close the AWPER central menu | `css_ui` |
| `Mouse4` | Click to toggle the camera preview | `css_preview_toggle` |
| `Mouse5` | Start a training round | `css_start` |

If the bindings are not loaded, run in the console:

```text
exec awper_bindings
```

You can also bind manually:

```text
bind "F5" "css_ui"
bind "MOUSE4" "css_preview_toggle"
bind "MOUSE5" "css_start"
```

## 2. Help and the front-end menu

| Chat command | Console command | Purpose |
|---|---|---|
| `!help` | `css_help` | Show brief help |
| `!ui` | `css_ui` | Open or close the AWPER central menu |

After opening the menu, select with the corresponding number keys on screen. Press `F5` again to close.

The F5 main menu currently contains:

1. Start a training round
2. Toggle the camera preview
3. Load the current map's configuration
4. Show the current status
5. Set points, mode, and speed
6. Switch training maps
7. Abort training and restore

The F5 menu invokes the same set of commands listed in this document, so it does not bypass permission or configuration validation.

## 3. Placing training points

Point-editing commands require the `@css/config` admin permission, and the player must be alive.

### 3.1 Enter edit mode and declare the track name

```text
!edit <track name>
```

For example:

```text
!edit mirage_awp_1
```

Effects:

- Creates a named editing session; without this command, all point, mode, speed, validate, and save commands are rejected.
- The name is 1–64 characters long and may only contain English letters, digits, underscores `_`, and hyphens `-`.
- If the name already exists, the plugin warns that saving will overwrite the existing track.
- A player cannot hold two configuration sessions at once; you must run `!abort` before re-entering edit mode.

### 3.2 Record the edit entry point — EditAnchor

```text
!set_edit_anchor
```

Console form:

```text
css_set_edit_anchor
```

Effects:

- Records the player's current feet position and view direction.
- This is the entry point for point editing.
- After recording PlayerAnchor, the plugin teleports the player back here so they can continue walking to place the Bot path.
- You must first enter edit mode with `!edit <track name>`.

Choose a safe position from which it is convenient to reach the Bot path.

### 3.3 Record the player training point — PlayerAnchor

```text
!set_player_anchor
```

Effects:

- Records the position where the player stands during training.
- Records the camera eye position.
- Records the player's view direction at that moment.
- The camera preview is later fixed here.
- After recording, the player is automatically teleported back to EditAnchor.

Correct procedure:

1. Run `!edit <track name>` first.
2. Run `!set_edit_anchor`.
3. Walk to where an AWP player should stand in a real match.
4. Aim the crosshair toward where the Bot is expected to appear.
5. Run `!set_player_anchor`.

### 3.4 Record the Bot start point — BotStart

```text
!set_bot_start
```

Effects:

- Records the player's current feet position as the Bot start point.
- The Bot is placed here at the start of each round.
- The plugin immediately probes standing space, ground, and flatness.

Stand where the Bot should be before the peek begins, on the inner side of cover.

### 3.5 Record the Bot end point — BotEnd

```text
!set_bot_end
```

Effects:

- Records the player's current feet position as the Bot's final movement target.
- The main path of the direct-peek mode is:

```text
BotStart ─────────────→ BotEnd
```

After the Bot reaches the vicinity of the end point, the round ends.

### 3.6 Record the jiggle / shoulder point — BotJiggle

```text
!set_bot_jiggle
```

Effects:

- Records the intermediate position used by the jiggle mode.
- Only mode `2` requires this point.
- It is not needed for direct-peek mode.

Jiggle mode roughly works as:

```text
BotStart ⇄ BotJiggle
    repeated 1–4 times
BotStart ─────────→ BotEnd
```

### 3.7 Record the Bot's facing direction

```text
!set_bot_facing
```

Effects:

- Reads the player's current horizontal view angle.
- Saves that angle as the Bot's facing while moving.
- Only the Yaw is recorded, not the player's standing position.

Turn your view toward the direction you want the Bot to face, then run this command. This field is not mandatory; if omitted, the plugin automatically makes the Bot face PlayerAnchor from its start point.

## 4. Training modes

### Direct-peek mode

```text
!mode 1
```

Internal name `DirectPeek`, path:

```text
BotStart ─────────→ BotEnd
```

This is the default mode and does not require BotJiggle.

### Jiggle-then-peek mode

```text
!mode 2
```

Internal name `JiggleThenPeek`. Runtime logic:

1. The Bot randomly shuffles between BotStart and BotJiggle.
2. By default it repeats 1–4 times at random.
3. Each endpoint pause is about 0.05–0.20 seconds.
4. Finally it moves from BotStart to BotEnd.

Mode 2 requires recording first:

```text
!set_bot_jiggle
```

## 5. Bot movement speed

Format:

```text
!speed <1-215>
```

The allowed range is `1–215 units/s`.

| Command | Speed | Purpose |
|---|---:|---|
| `!speed 215` | 215 | AK-47 normal full-speed movement, also the default |
| `!speed 180` | 180 | Medium speed |
| `!speed 150` | 150 | Slow practice |

This value is the plugin's target ground-speed ceiling, not a direct modification of the Bot AI speed. The Bot starts from rest and accelerates frame by frame according to the server's current `sv_accelerate`, `sv_friction`, `sv_stopspeed`, and tick interval; therefore a very short track may reach its end before reaching the target speed.

## 6. Validating and saving a configuration

### Validate the current configuration

```text
!validate
```

Effects:

- Checks that the required points are complete.
- Checks that the current map matches.
- Checks standing space and ground for BotStart, BotEnd, and BotJiggle.
- Checks that the target speed is legal.
- Checks that jiggle mode has a BotJiggle.
- Runs live RayTrace map-geometry validation.

A line of sight that is not directly visible only produces a `los.start` or `los.end` warning and does not block saving, because the Bot start point may be behind cover.

### Save the configuration

```text
!save
```

Save uses the name declared when entering edit mode with `!edit <track name>`; it no longer accepts a name argument.

On save, the plugin automatically:

1. Builds the complete configuration.
2. Runs live map validation.
3. Writes to the current map's configuration directory.
4. Auto-loads the just-saved configuration.
5. Restores the player to PlayerAnchor.
6. Exits edit mode.

Required fields:

- EditAnchor
- PlayerAnchor
- BotStart
- BotEnd

Mode 2 additionally requires BotJiggle. BotFacing may be omitted.

## 7. Configuration file management

### List all configurations for the current map

```text
!list
```

Shows only the current map's configurations. For example, on `de_mirage` it will not show `de_dust2` configurations.

### Load a configuration

```text
!load <config name>
```

For example:

```text
!load mirage_awp_1
```

On load, the plugin re-runs live validation for the current map. Loading alone does not let you start immediately; you must complete one camera verification:

1. Click `Mouse4` to enter preview.
2. Check the camera and the Bot ghost position.
3. Click `Mouse4` again to exit preview.
4. Click `Mouse5` or run `!start`.

### Delete a configuration

```text
!delete <config name>
```

For example:

```text
!delete mirage_awp_1
```

Requirements:

- `@css/config` permission.
- You can only delete configurations of the current map.
- If the deleted configuration is in use by an editing session, the plugin clears its loaded state.

### Copy a configuration and change its speed

```text
!copy <original> <new> <1-215>
```

For example:

```text
!copy mirage_awp_1 mirage_awp_slow 150
```

This operation changes only the copy's track name and Bot target speed. The map, all anchors, Bot path, facing, movement mode, countdown, random delay, and other training parameters remain unchanged.

Requirements:

- `@css/config` permission.
- The original track belongs to the current map and actually exists.
- The new name must follow the track naming rules and must not collide with an existing track; the command will not overwrite the target track.
- The speed range is `1–215 units/s`.
- After copying, the copy is not auto-loaded, and the currently loaded track is not changed.

## 8. Camera preview

### Click to toggle preview

```text
!preview_toggle
```

Or click `Mouse4`.

Logic:

- First execution: enter the fixed-camera view.
- No second execution: the camera view is maintained.
- Second execution: exit the camera and complete this session's verification.

Preview screen:

- The camera is fixed at PlayerAnchor.
- The camera faces the Bot point.
- A ghost model marks the Bot's expected position.

### Force-enter preview

```text
!preview_on
```

### Force-exit preview

```text
!preview_off
```

After a successful exit, the current session is marked:

```text
camera=verified-this-session
```

### Legacy hold-to-preview commands

The console or a binding can still use:

```text
+preview
-preview
```

For example:

```text
bind "MOUSE4" "+preview"
```

However, the current default binding is now click-to-toggle:

```text
bind "MOUSE4" "css_preview_toggle"
```

Preview requires at least a recorded or loaded PlayerAnchor. When creating a new configuration, as long as EditAnchor and PlayerAnchor exist, you can preview the camera early without waiting for BotStart and BotEnd to be fully set.

When preview is on, the camera is fixed at PlayerAnchor and faces BotStart; the player Pawn's facing is frozen at the facing it had the moment Mouse4 was pressed, and is not overwritten by the camera angle or the PlayerAnchor's saved view. It is restored after exiting preview.

## 9. Starting and stopping training

### Start a training round

```text
!start
```

Or click `Mouse5`.

Start conditions:

- The player is alive.
- The current map is on the whitelist.
- A validated configuration is loaded.
- BotController ABI is compatible.
- RayTrace validation passes.
- This session has completed the camera preview verification.
- No other training round is currently running.

After starting:

1. Close the F5 menu and camera.
2. Clean up existing Bots.
3. Create an enemy Bot.
4. Give the Bot an AK-47.
5. Wait for the Bot Pawn and model to stabilize.
6. Enter a 3-second countdown.
7. Wait another ~0.5–3.0 second random delay.
8. The Bot moves along the configured track.
9. The round completes when the Bot is killed or reaches the end.
10. The plugin cleans up this round's Bot, dropped AK, and related entities.

### Force-start a round at a specified speed

```text
!start_speed <1-215>
```

For example:

```text
!start_speed 150
```

This command uses the same loaded track and start conditions as `!start`, but replaces the Bot target speed with the specified value for this round only. It does not modify the track on disk, nor the originally loaded track in the current session; running `!start` again after this round ends still uses the track's originally saved speed.

### Abort training or editing

```text
!abort
```

Effects:

- Aborts the current training round.
- Closes the F5 menu.
- Exits the camera preview.
- Clears the current player's point-editing session.
- Restores the player to a recoverable position.
- Cleans up the training Bot and the plugin's held AK.

Run this command first if you want to place points from scratch again.

## 10. Viewing the running status

```text
!status
```

Displays:

- `bot=`: BotController compatibility status.
- `world=`: RayTrace map-validation status.
- `camera=`: whether this session completed camera verification.
- `editing=`: the named track currently being edited; `none` when not editing.
- `runtime=`: the current training state.
- `native=`: BotController native-call diagnostics.

Common states:

```text
camera=unverified-this-session
camera=verified-this-session
editing=mirage_awp_1
editing=none
runtime=none
runtime=Prepare
runtime=Countdown
runtime=Running
```

## 11. Map commands

### List available maps

```text
!maps
```

Current default map pool:

```text
dust2
inferno
mirage
anubis
ancient
nuke
cache
```

The output marks the current map.

### Switch maps

Format:

```text
!map <map>
```

The `de_` prefix can be omitted:

```text
!map mirage
!map dust2
!map cache
```

Or use the full name:

```text
!map de_mirage
!map de_dust2
```

All available commands:

```text
!map dust2
!map inferno
!map mirage
!map anubis
!map ancient
!map nuke
!map cache
```

Map-switch permission:

- `@css/changemap`, or
- `@css/config`.

When switching maps, the plugin:

1. Closes every player's AWPER menu.
2. Closes every camera.
3. Aborts the current training.
4. Restores and clears all editing sessions.
5. Runs `changelevel de_<map>`.

## 12. Permission and usage-condition summary

Commands requiring the `@css/config` permission:

```text
!edit <name>
!set_edit_anchor
!set_player_anchor
!set_bot_start
!set_bot_end
!set_bot_jiggle
!set_bot_facing
!mode
!speed
!validate
!save
!copy <original> <new> <1-215>
!delete
```

Switching maps requires `@css/changemap` or `@css/config`:

```text
!map <map>
```

The following operations require the player to be alive:

- Opening the F5 menu.
- Placing and modifying points.
- Loading a configuration.
- Camera preview.
- Starting training.

## 13. Complete new-point workflow

Using `mirage_awp_1` as an example.

First clear any old session:

```text
!abort
```

Declare the track name and enter edit mode:

```text
!edit mirage_awp_1
```

Stand at a position convenient for editing and walking:

```text
!set_edit_anchor
```

Walk to the actual AWP holding position and adjust your view:

```text
!set_player_anchor
```

After the plugin teleports you back to the edit entry point, walk to the Bot start:

```text
!set_bot_start
```

Walk to the Bot end:

```text
!set_bot_end
```

If you need jiggle mode, walk to the jiggle position:

```text
!set_bot_jiggle
!mode 2
```

If you only need two-point peeking:

```text
!mode 1
```

Adjust your view and record the Bot facing:

```text
!set_bot_facing
```

Set the normal AK speed:

```text
!speed 215
```

Validate and save:

```text
!validate
!save
```

Camera verification:

```text
!preview_toggle
!preview_toggle
```

Start training:

```text
!start
```

Run again directly for the next round:

```text
!start
```

## 14. Complete command quick-reference

```text
!help
!ui
!maps
!map <dust2|inferno|mirage|anubis|ancient|nuke|cache>

!edit <name>
!set_edit_anchor
!set_player_anchor
!set_bot_start
!set_bot_end
!set_bot_jiggle
!set_bot_facing

!mode <1|2>
!speed <1-215>
!validate
!save

!list
!load <name>
!copy <original> <new> <1-215>
!delete <name>

!preview_on
!preview_off
!preview_toggle

!start
!start_speed <1-215>
!abort
!status
```

Console or keybinding-compatible commands:

```text
+preview
-preview
exec awper_bindings
```
