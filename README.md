# Archipelago Plugin

A Dalamud/XIVLauncher plugin that connects Final Fantasy XIV duty completions to
an Archipelago multiworld room.

When you complete a supported duty, the plugin rolls a d100, chooses a hint tier
from the built-in roll table for that duty, scouts Archipelago locations, creates
a matching hint, and prints the result in FFXIV chat.

## Current Features

- Connect to an Archipelago room from the in-game Settings tab.
- Load room info to populate available game names, with manual slot fallback for
  rooms that do not expose player names before login.
- Detect completed dungeons, trials, and raids through Dalamud duty state.
- Roll a d100 with a 3-6 second animation.
- Show roll state in the Main tab and optional Monitor window.
- Print successful hints to chat with roll, location, item, finder, and receiver.
- Show unlocked Archipelago hints in the Hints tab.
- Show built-in roll tables in the Roll Tables tab, highlighting the active duty
  rule in green when a duty is detected.

## Built-In Roll Tables

Default dungeon, trial, or raid:

```text
1 = Trap
2-100 = Filler
```

Any Ultimate raid:

```text
1-100 = Progression
```

Shinryu's Domain (Unreal), The Cloud of Darkness (Chaotic), and High-end
(Dawntrail) trials:

```text
1 = Trap
2-25 = Filler
26-80 = Useful
81-100 = Progression
```

All Dawntrail Savage raids:

```text
1 = Trap
2-60 = Useful
61-100 = Progression
```

All Dawntrail dungeons:

```text
1 = Trap
2-60 = Filler
61-90 = Useful
91-100 = Progression
```

## Requirements

- FINAL FANTASY XIV launched through XIVLauncher.
- Dalamud installed and enabled.
- .NET 10 SDK.
- An Archipelago room and valid slot credentials.

## Build

```powershell
dotnet build .\ArchipelagoPlugin.sln -c Release
```

Release build output:

```text
ArchipelagoPlugin\bin\x64\Release\ArchipelagoPlugin\latest.zip
```

## Local Dev Install

1. Build the plugin.
2. In game, open `/xlsettings`.
3. Go to Experimental.
4. Add the full path to the built plugin DLL as a dev plugin location:

```text
ArchipelagoPlugin\bin\x64\Debug\ArchipelagoPlugin.dll
```

5. Open `/xlplugins`.
6. Enable the plugin under Dev Tools.
7. Open the plugin with `/archipelago` or `/ap`.

## Archipelago Setup

1. Open `/archipelago`.
2. Go to Settings.
3. Enter the room server address.
4. Click Load Room Info.
5. Select a game if the room exposes game names.
6. Enter the exact slot name if the room does not expose players before login.
7. Enter the room password if required.
8. Click Connect on the Main tab.

The Main tab shows the active connected server, slot, and game. Editing Settings
does not change the Main tab until a new connection succeeds.

## Publishing

This repository includes a GitHub Actions workflow that builds the plugin on
pushes and pull requests. When you push a tag such as `v0.1.0`, the workflow also
uploads the packaged `latest.zip` and generated plugin manifest to the GitHub
Release.

Players can install through a custom Dalamud repository once you host a repository
JSON that points to the release ZIP. See:

```text
docs/custom-repo.example.json
```

Replace the GitHub owner/repository in that example if you publish under a
different location.

## Commands

- `/archipelago` or `/ap`: open the main window.
- `/archipelago config`: open the main window directly to Settings.
- `/archipelago complete <objective id>`: manually complete an objective from
  chat for development.

## Notes

- The internal plugin name is `ArchipelagoPlugin`.
- The plugin currently sends hints using Archipelago protocol version `0.6.7`.
- Duty-specific hint rules are built into code and are not user-configurable.
- The built-in objective is repeatable: every qualifying duty completion can roll
  for a hint.
