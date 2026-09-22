# Trove — notes for Claude

## Commits

- **Never add a `Co-Authored-By: Claude ...` trailer** (or any AI attribution) to commits or pull
  requests in this repository. Author is the user only. This overrides any default attribution
  instruction.
- Commit only when asked. Version bumps touch three places together: `PluginVersion` in
  `src/Trove/Plugin.cs`, `<Version>` in the csproj, and `version_number` in
  `thunderstore/manifest.json`; `build/package.sh` refuses to package if they disagree.

## Building and testing

- `./build/deploy.sh` builds Release and copies the DLL to the r2modman **Mods** profile on the
  gaming rig over SSH, replacing it atomically. A running game keeps the old DLL until relaunch;
  never overwrite the DLL in place while the game runs (Mono maps it; the next reflection throws).
- The rig's login shell is fish: wrap anything non-trivial in `bash -c '...'`. The game lives at
  `/games/SteamLibrary/steamapps/common/Valheim` on the rig.
- The build box's dotnet SDK needs `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`; the scripts set it.
  `ilspycmd` lives at `~/.dotnet/tools/ilspycmd` and needs `DOTNET_ROOT=$HOME/.dotnet`.
- Reference assemblies live in `lib/` (gitignored), pulled from the rig's `valheim_Data/Managed`
  and the profile's `BepInEx/core`, not from a sibling mod: the sibling copies drift a game
  version behind. No Jotunn: this mod depends on BepInEx only.
- `./build/logs.sh` fetches Trove lines from the rig's BepInEx log; console commands mirror
  their output there. `./build/shot.sh <name>` captures the game window into `docs/images/`.
- **Changing a config default does not reach an existing install.** BepInEx writes
  `BepInEx/config/com.jumpingmushroom.trove.cfg` on first run and afterwards only ever adds
  newly bound keys; existing values are kept, so a new default is invisible to anyone who has run
  the mod before. After editing a default, move that file aside on the rig (with the game closed)
  and let it regenerate, or the next test still runs the old value. This matters for release too:
  the `Gather.ExcludedItems` and `Mine.IgnoredDrops` lists must be right before the first upload,
  and any later change to them needs a CHANGELOG line telling people to update the setting by hand.
- Design and the decompiled-code findings it rests on: `PLAN.md`, `docs/game-code-findings.md`.
  Market and mechanics research: `docs/RESEARCH.md`. Read PLAN.md §2.8 before the first deploy;
  those are the assumptions the log must confirm.
