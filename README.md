# District Planner

A BepInEx mod for ENDLESS Legend 2 that makes the recommended tiles for placing a district worth following. It also shows where each tile's yields come from.

When you place a district, the game only recommends the tiles tied for the single best raw score. It ignores queued districts and never suggests tiles you could buy a Foundation on. This mod replaces those recommendations:

- **Recommends by fit, not raw yield.** Adjacency bonuses, on-tile bonuses and neighbour level-ups are weighed so a district goes where it pairs with its surroundings. The best tile of each territory is always recommended.
- **Avoids penalties.** A tile where the district takes or gives a negative bonus is only recommended when there's no clean one.
- **Counts queued districts** as if they were built, so a queue of districts can be planned around each other.
- **Offers buyable tiles.** Tiles that need a Foundation bought with Influence first can be clicked directly: you're asked to confirm the price, then the Foundation is bought and the district placed. Tiles away from the city (buyable only through another city's district) are ranked down by how much more they cost.
- **Explains the hovered tile:**
  - Its district tooltip highlights which bonuses and penalties apply there, and lists what changes for each neighbour.
  - Neighbours are labelled with what they give and gain.
  - Tiles that give synergy are highlighted.

Everything uses the game's own names and wording, so it works in any language the game supports.

## Installation

Each [release](../../releases/latest) has two downloads:

- **`DistrictPlanner-<version>-with-BepInEx.zip`**: use this if you've never installed a BepInEx mod for ENDLESS Legend 2. It includes [BepInEx](https://github.com/BepInEx/BepInEx) 5.4.23.5, the mod loader.
- **`DistrictPlanner-<version>.zip`**: the mod on its own, for when BepInEx is already installed (5.4.23.5 or newer, x64, Mono). Older 5.x releases don't write their log under Unity 6.

1. Open the game folder: in Steam, right-click ENDLESS Legend 2 > Manage > Browse local files. It's the folder with `Endless Legend 2.exe`.
2. Extract the zip straight into that folder, not into a new subfolder. The mod ends up in `BepInEx/plugins/DistrictPlanner/`, and with the bundle, `winhttp.dll` sits next to `Endless Legend 2.exe`.
3. **Linux / Steam Deck only:** in Steam, set the game's launch options (Properties > General) to

   ```
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```
4. Start the game. When you place a district, the recommended tiles now come from the mod. If nothing changes, look in `BepInEx/LogOutput.log` for `District Planner loaded`. If there's no log file at all, BepInEx isn't running: check step 2, or step 3 on Linux.

To update, extract the new mod-only zip over the old files. To uninstall, delete `BepInEx/plugins/DistrictPlanner`. To remove BepInEx as well, also delete the `BepInEx` folder, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and `changelog.txt`.

Optional: [BepInEx Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager) (BepInEx 5 build) adds an in-game settings window, see below.

## Settings

Change settings in game with the Configuration Manager (press **F1**), or in `BepInEx/config/nz.digistruct.el2.districtplanner.cfg` (created on first start). Changes apply straight away, including to an open placement. The Configuration Manager hides fine-tuning, text and debug options unless *Advanced settings* is ticked.

| Section | Setting | Default | What it does |
|---|---|---|---|
| Recommendations | Enabled | on | Use this mod's recommendations; off restores the game's |
| | SynergyWeight | 3 | How much more adjacency and on-tile bonuses count than other yields |
| | NeighbourLevelUpWeight | 0.5 | How much neighbours levelling up counts |
| | NeighbourFoundationWeight | 0.5 | How much the yields of tiles a district raises a Foundation on count (e.g. a Keep) |
| | NegativeSynergyPenalty | 1000 | Ranking penalty for a tile with a negative bonus; 0 disables |
| | CountQueuedConstructions | on | Treat queued districts as built |
| | BestPerTerritory | on | Always recommend each territory's best tile |
| Buyable tiles | Enabled | on | Allow placing on tiles that need a Foundation bought first |
| | MaxRecommended | 3 | How many buyable tiles can be recommended |
| | PaybackTurns | 30 | How strongly expensive tiles away from the city are avoided; lower avoids them more |
| | ShowCostOnTiles | on | Show the Foundation cost on recommended buyable tiles |
| Display | Details | on | Tooltip highlights, neighbour sections and on-tile bonuses on the hovered pin |
| | NeighbourLabels | on | Label every neighbour that gives or gains yields |
| | SplitHoverEffects | off | Show the hovered tile's own yields on its pin and each neighbour's change on that neighbour |
| | HideNearbyPins | on | Hide other candidate pins near the hovered tile |

## Notes

- Buying a Foundation is instant and isn't refunded if you then cancel the district.
- On-tile bonuses that depend on the tile (e.g. Science on a tile with two or more yield types) are highlighted from the tile's yields. Unusual conditions are left unhighlighted.
- Only tested in single player.
- Game updates can break the mod. If placement looks wrong after an update, check `BepInEx/LogOutput.log` for errors from District Planner and [open an issue](../../issues) with the log attached.

## Building from source

Needs the .NET SDK and the game with BepInEx installed: the build compiles against the game's own assemblies, which aren't included here.

```
dotnet build -c Release -p:GameDir="<path to ENDLESS Legend 2>"
```

`GameDir` defaults to the usual Steam folder. The release build writes `dist/DistrictPlanner-<version>.zip`, laid out to extract into the game folder. A Debug build is copied to the game's `BepInEx/scripts` for [ScriptEngine](https://github.com/BepInEx/BepInEx.Debug) to hot-reload.

## License

MIT, see [LICENSE](LICENSE).
