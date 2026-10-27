# Auto Utilities for Project Highrise

Stop clicking pipes floor by floor. Auto Utilities wires power, phone, cable TV, water, gas and HVAC across every floor of your tower, and copies your utility closets up and down the building for you.

A Side Order Games mod.

## Features

- **Auto-wiring.** Every floor that can reach a working line gets fully wired, for every utility you have. Runs on its own while the clock is running.
- **Extend to all floors.** Click any wiring, plumbing or HVAC closet and press the new **Extend to all floors** button in its info panel. The closet is copied up to the top of the tower and down through every basement, stopping where a room is in the way.
- **Spends like you would.** Everything is paid for at normal cost and built by your workers. It never spends below a cash reserve you choose (default $1,000).
- **Tells you what it did.** A status box at the top of the screen shows what was wired, what was built, and how much more cash it needs if it is waiting.

You still buy your own meters (water meter, electric meter, and so on). Those are what supply the building; this mod only handles getting the supply to every floor.

## Install

1. Download `AutoUtilities-vX.Y.Z.zip` from the [Releases page](../../releases).
2. Open your game folder. In Steam: right-click Project Highrise, **Manage**, **Browse local files**.
3. Unzip everything into that folder, so `winhttp.dll` and the `BepInEx` folder sit next to `Game.exe`.
4. Start the game.

The zip includes [BepInEx](https://github.com/BepInEx/BepInEx), the free mod loader most Unity games use. It changes nothing on its own; it just lets the mod run.

## Controls

| Key | What it does |
| --- | --- |
| **F7** | Run a pass right now (works while paused) |
| **Shift+F7** | Turn automatic passes on or off |
| **Extend to all floors** button | In a closet's info panel: copy that closet up and down the tower |

## Settings

After the first launch, edit `BepInEx\config\com.thehashbrown.projecthighrise.autoutilities.cfg` in your game folder:

- `CashReserve`: never spend below this (default 1000)
- `IntervalGameMinutes`: how often the automatic pass runs (default 30)
- `ExtendRisers`: let automatic passes copy closets floor to floor (default on)
- `[Utilities]`: turn each utility on or off

## Uninstall

- Remove just this mod: delete `BepInEx\plugins\HashbrownAutoUtilities.dll`.
- Remove all mods: delete `winhttp.dll` from the game folder.

Steam's "Verify integrity of game files" does not remove BepInEx; delete the files above.

## Building from source

Windows, no SDK needed:

```
powershell -ExecutionPolicy Bypass -File build.ps1 -Install
```

`build.ps1` compiles with the .NET Framework `csc.exe` against the game's own assemblies and a BepInEx 5 (x86) install in the game folder. `package.ps1 -Version x.y.z` builds the release zip.

## Credits

- Inspired by Uuugggg's [TD Enhancement Pack](https://github.com/alextd/ProjectHighrise-TDEnhancementPack).
- Built on [BepInEx](https://github.com/BepInEx/BepInEx) and [HarmonyX](https://github.com/BepInEx/HarmonyX).
- Not affiliated with SomaSim or Kasedo Games. Project Highrise is their game.

## License

MIT. Copyright (c) 2026 TheHashbrown LLC. See [LICENSE](LICENSE).
