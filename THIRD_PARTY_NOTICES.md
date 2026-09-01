# Third-party notices

## CounterStrikeSharp

Project: [roflmuffin/CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)

This project references `CounterStrikeSharp.API` 1.0.373 as a build-only NuGet dependency (`Private=false`). CounterStrikeSharp documents a GPL-3.0 license with a plugin exception; consult its repository license files for the exact terms that apply to distributed plugins.

## CS2-Bot-Controller

Project: [XBribo/CS2-Bot-Controller](https://github.com/XBribo/CS2-Bot-Controller)

The runtime integration uses the public capability key and `IBotControllerApi` contract. No upstream native binary or managed API binary is vendored here. The compile-time contract subset preserves the upstream namespace, assembly name, method signatures and enum values needed for runtime type identity.

CS2-Bot-Controller is AGPL-3.0. This repository therefore selects AGPL-3.0 for the open-source path and includes the license text in `LICENSE`. Closed-source distribution or hosted-service use must be reviewed with the upstream author before release.

## Ray-Trace

Project: [FUNPLAY-pro-CS2/Ray-Trace](https://github.com/FUNPLAY-pro-CS2/Ray-Trace)

The runtime geometry validation uses capability `raytrace:craytraceinterface`. The repository contains a compile-time copy of the public managed contract at upstream commit `616e169a2cc65cd8dcdcc4c5569b5e887f36cd52`; no upstream native or managed binary is included in the release archive. Server operators install the upstream RayTrace, RayTraceImpl, and shared RayTraceApi components separately.

Ray-Trace is GPL-3.0. Its use is compatible with this repository's AGPL-3.0 open-source distribution path; operators remain responsible for the upstream component's license and source-offer obligations.

## Valve / Counter-Strike 2

Counter-Strike and Counter-Strike 2 are trademarks of Valve Corporation. This community server plugin is not affiliated with or endorsed by Valve.
