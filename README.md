# SpriggullHats

A client mod for A Township Tale that puts a tiny top hat on every Spriggull. The hat is the
game's own Villager Top Hat, shrunk down, and it sits on the head so it bobs and tilts with
the bird. Only you see the hats.

If the game can't load the cosmetic, the mod uses a plain black top hat of its own instead
and says why in the log.

## Install

1. Close the game.
2. Put `SpriggullHats.dll` in the game's `Mods` folder.

Needs MelonLoader, which TheModdingTavern launcher installs. Client only, servers need
nothing.

To remove it, delete `SpriggullHats.dll` from `Mods`.

## Settings

`UserData\SpriggullHats\settings.cfg` is made the first time the game starts. Save it while
the game runs and every hat updates within two seconds.

| Key | Does |
|---|---|
| `enabled` | `1` for hats, `0` for none |
| `size` | `1` is tiny, `2` is twice as big |
| `lift` | Metres to raise the hat, negative to sink it |
| `forward` | Metres to move the hat towards the beak, negative to move it back |
| `tilt` | Degrees the hat leans to one side |
| `cosmetic` | `64982` Villager Top Hat, `26131` Battered Top Hat, `0` for the plain hat |
| `hat`, `band` | Colours of the plain hat, as hex `RRGGBB` |
| `bone` | Leave empty to find the head by itself, or name a bone from `rig.txt` |

The first hat of each session writes `UserData\SpriggullHats\rig.txt`: the bone it picked,
how it found the top of the head, and every bone in the Spriggull. If a hat sits in the
wrong place, that file says why.

## Build

`build.ps1` compiles `SpriggullHats.cs` against your game install (set `$game` at the top)
with the .NET Framework's `csc`, and writes `build\SpriggullHats.dll`.

## Licence

MIT. See `LICENSE`.
