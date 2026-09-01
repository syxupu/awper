# AWPER

[English](README.md) · [Русский](README.ru.md) · [中文](README.zh-CN.md)

AWPER is an AWP horizontal-peek training plugin for **Counter-Strike 2 dedicated servers**. Administrators can record player positions, Bot start and end points directly in official maps, and save them as reusable training tracks. Players load a track, confirm their view through a fixed camera, and repeatedly practice a Bot's two-point lateral movement.

Current version: **1.1.0**

> This is a community plugin with no affiliation or endorsement from Valve. It is not a Workshop map and must run on a CS2 dedicated server with CounterStrikeSharp installed.

## Demo

A rough, unpolished demo video is available: [demo.mp4](demo.mp4).

## Main features

- Create, validate, save, and load training tracks inside official maps.
- Enter an isolated editing mode with `!edit <name>` to prevent ordinary players from accidentally modifying points.
- Record `EditAnchor`, `PlayerAnchor`, `BotStart`, `BotEnd`, and an optional `BotJiggle`.
- Mouse4 single-click toggles the fixed-camera preview; the camera sits at `PlayerAnchor` and the player model keeps the facing it had at the moment the key was pressed.
- The Bot replays movement using a real Pawn and CS2-Bot-Controller native input, not per-tick teleportation.
- The Bot accelerates from a standstill according to the server's live `sv_accelerate`, `sv_friction`, `sv_stopspeed`, and tick interval.
- Start a single round at a specified speed without modifying the track itself: `!start_speed <1-215>`.
- Copy a track and change only its name and speed: `!copy <original> <new> <1-215>`.
- F5 opens the native CounterStrikeSharp central menu; chat commands always remain available as a fallback.
- Standing space, ground, path sweep, and line-of-sight checks run before save, load, and start; a failing validation refuses to run.
- Idempotent cleanup of Bots and dropped weapons after each round, supporting consecutive rounds.

## Runtime logic

```text
Admin creates a track
  !edit <name>
        │
        ├─ EditAnchor       Editing entry point
        ├─ PlayerAnchor     Player training / camera position
        ├─ BotStart         Bot start point
        ├─ BotEnd           Bot end point
        └─ BotJiggle        Optional jiggle point
        │
        ▼
  Live Ray-Trace validation ──fails──> Refuse to save and explain why
        │ passes
        ▼
  Save as profiles/<map>/<name>.json

Player trains
  !load <name> → Mouse4 preview → Mouse5 / !start
        │
        ▼
  Countdown → random delay → spawn Bot → native-input replay
        │
        ├─ Bot dies
        ├─ Reaches the end
        ├─ Gets stuck / times out
        └─ Player aborts
        │
        ▼
  Clean up Bot and weapons → restore player → next round
```

See [command.md](command.md) for the complete command parameters and editing order.

## Requirements

- A CS2 dedicated server.
- [Metamod:Source](https://www.sourcemm.net/).
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) API 1.0.373 or higher.
- [CS2-Bot-Controller](https://github.com/XBribo/CS2-Bot-Controller), current interface ABI 19.
- [FUNPLAY Ray-Trace](https://github.com/FUNPLAY-pro-CS2/Ray-Trace), including the native, RayTraceImpl, and the shared RayTraceApi.
- The target CS2 version must support `cs_player_camera`.

The release package does not redistribute the binaries of the dependencies above; install each one following its upstream instructions.

## Installation

1. Download `AwperTrainer-1.1.0.zip` from GitHub Releases and extract it.
2. Place the runtime files on the server:

```text
game/csgo/addons/counterstrikesharp/plugins/AwperTrainer/
├─ AwperTrainer.dll
├─ AwperTrainer.Core.dll
├─ AwperTrainer.deps.json
└─ AwperTrainer.runtimeconfig.json
```

3. Copy the bridge assets from the release package to the matching server directories:

```text
resources/awper_camera.vjs_c  -> game/csgo/scripts/awper/awper_camera.vjs_c
resources/awper_hud.vjs_c     -> game/csgo/scripts/awper/awper_hud.vjs_c
resources/awper_hud.vxml_c    -> game/csgo/panorama/layout/custom_game/awper_hud.vxml_c
resources/awper_hud.vcss_c    -> game/csgo/panorama/styles/custom_game/awper_hud.vcss_c
```

4. Confirm that the BotController and RayTrace plugins, shared API, and native modules are installed, then restart the server.
5. Join the server and type `!status` to confirm that BotController, RayTrace, and camera are all available.

The plugin configuration is located at:

```text
game/csgo/addons/counterstrikesharp/configs/plugins/AwperTrainer/AwperTrainer.json
```

Training tracks are saved per map:

```text
game/csgo/addons/counterstrikesharp/configs/plugins/AwperTrainer/profiles/<map>/<name>.json
```

## Player keybindings

You can set these directly in the client console:

```cfg
bind "F5" "css_ui"
bind "MOUSE4" "css_preview_toggle"
bind "MOUSE5" "css_start"
```

Alternatively, place `awper_bindings.cfg` from the release package into the client's `game/csgo/cfg/`, then run:

```text
exec awper_bindings
```

F5 uses the CounterStrikeSharp central menu provided by the server. Even without the F5 binding, you can open the menu by typing `!ui` in chat.

## Quick start

Load an existing track:

```text
!list
!load mirage_awp_1
```

After loading, press Mouse4 first to check the camera, then Mouse5 to start training. You can also use chat commands:

```text
!preview_toggle
!start
```

Start only this round at 180 units/s without changing the saved track:

```text
!start_speed 180
```

## Creating a track

```text
!edit mirage_awp_1
!set_edit_anchor
!set_player_anchor
!set_bot_start
!set_bot_end
!set_bot_facing
!mode 1
!speed 215
!validate
!save
```

Recommended order:

1. Enter edit mode in a safe position and record `EditAnchor`.
2. Walk to the actual training position and record `PlayerAnchor`.
3. Walk to where the Bot should appear and record `BotStart`.
4. Walk to where the peek ends and record `BotEnd`.
5. Record the Bot's facing and jiggle point as needed.
6. Preview with Mouse4, then run `!validate` and `!save`.

Track names may only contain English letters, digits, underscores, and hyphens, from 1 to 64 characters long.

## Common commands

| Command | Purpose |
|---|---|
| `!help` | Show brief help |
| `!ui` | Open or close the F5 central menu |
| `!status` | Show plugin, dependency, and current training state |
| `!list` | List tracks for the current map |
| `!load <name>` | Load a track |
| `!start` | Start using the track's saved speed |
| `!start_speed <1-215>` | Override the Bot speed for this round only |
| `!copy <original> <new> <1-215>` | Copy a track, changing only name and speed |
| `!abort` | Abort training or editing and restore the player |
| `!maps` | List maps you are allowed to switch to |

Every chat command's `!` can be replaced with `/`; the console form adds a `css_` prefix to the name, e.g. `!start` corresponds to `css_start`. AWPER commands no longer use the old `awper_` prefix.

## Bot movement model

The configured speed is the target ground-speed ceiling. Each tick the plugin first applies ground friction, then accelerates along the path direction using the Source ground-acceleration model, so the Bot does not reach 215 units/s on the very first frame. A short path may end before the target speed is reached — that is expected.

The plugin does not simulate movement through per-tick teleportation; if the BotController interface or native capability is unavailable, training refuses to start.

## Build & test

PowerShell 7 and the `.NET SDK 10.0.201` are required:

```powershell
pwsh.exe -NoLogo -NoProfile -NonInteractive -File .\build.ps1
```

This runs a Release build, automated tests, formatting checks, and PowerShell syntax checks, and produces:

```text
artifacts/AwperTrainer.zip
```

## Project structure

```text
src/AwperTrainer.Core/        Tracks, validation strategies, and the training state machine
src/AwperTrainer.Plugin/      CounterStrikeSharp plugin and game-entity control
tests/                        Automated tests
assets/                       Camera and HUD bridge assets
config/                       Example configuration and keybindings
tools/                        Deployment and installation verification tools
command.md                    Complete command manual
```

## Known limitations

- The repository provides only the plugin logic; it does not ship official map content or pre-made tracks for the seven maps.
- Each map and each training position still requires an administrator to record and validate the track.
- After CS2, CounterStrikeSharp, BotController, or RayTrace updates, re-run on-server validation.
- The current design runs one training session at a time, suited for personal or turn-based training servers.

## License

This project is released under [AGPL-3.0](LICENSE) to remain compatible with the AGPL-3.0 dependency path of CS2-Bot-Controller. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party components and trademark notices.
