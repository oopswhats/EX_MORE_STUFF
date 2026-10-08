# EX More Stuff

New costumes and stages for **Ultra Street Fighter IV** (Steam). They go into new slots, so the game's own costumes and stages are never overwritten.

## Download

Get the latest `EXMoreStuff-<version>.zip` from [Releases](../../releases), unzip it anywhere and run `EXMoreStuff.exe`. Windows 10 and 11 already have everything it needs (.NET Framework 4.8). Nothing is installed on your PC: to remove the program, delete its folder.

## Using it

1. It finds your Steam copy of USFIV, or asks for its folder.
2. **Costumes:** click a fighter and switch on the costumes you want.
3. **Stages:** switch on custom stages. You can also click one of the game's stages to replace it with another one, on your PC only.
4. Click **Apply changes**, then close the program. The game has to be closed while changes are applied.

Switching something off and applying removes it again. **Remove everything** puts the game back the way it was.

## What it changes

- Custom costumes and stages get their own new files in the game's `patch_ae2_tu3` folder: costumes are slots 8 to 99 (`RYU_12.obj.emo` ...), stages `STG_C01` to `STG_C99`. It never writes over a file it didn't install.
- A replaced game stage is a converted copy of the other stage put in its place. A file that was already there is moved to `patch_ae2_tu3\ex_more_stuff_backup` and comes back when the replacement is switched off.
- Downloads are checked against the catalog's size and SHA-256 before anything is unpacked.
- The program only reads the catalog in this repository and the downloads it lists. It doesn't update itself: when a new version is out, it shows a link to this page.

## Playing online

Custom costumes and stages show up in EMBER builds with custom content support (not yet the official EMBER). Nothing extra is sent over the network. A player who doesn't have your costume sees the fighter's original costume, and a custom stage they don't have shows as one of the game's stages. A replaced stage is only on your PC: your opponent sees their own.

## The catalog

`catalog.json` lists everything the program offers:

```json
{
  "format": 1,
  "program": { "version": "0.1", "page": "https://github.com/oopswhats/EX_MORE_STUFF/releases" },
  "items": [
    {
      "id": "ryu-12-example", "type": "costume", "fighter": "RYU", "slot": 12,
      "name": "Example", "author": "someone", "version": "1.0", "description": "...",
      "picture": "pictures/ryu-12-example.png",
      "download": "https://github.com/oopswhats/EX_MORE_STUFF/releases/download/content/ryu-12-example.zip",
      "sha256": "...", "size": 12345678
    }
  ]
}
```

The catalog uses costume slots 8 to 70 and stage numbers 1 to 70; 71 to 99 are left for players' own packages (**Add from file**). A package is a zip with its files side by side, no folders:

- **costume:** `<FIGHTER>_<NN>.obj.emo` and `.nml.emb`, ten colours `<FIGHTER>_<NN>_01` to `_10` `.col.emb` and `.obj.emm`; optionally `.shd.emo`, `.bsr`, `.csb` and a picture per colour `<FIGHTER>_<NN>_01.png` ...
- **stage:** `STG_C<NN>.emz`, `STG_C<NN>.tex.emz` and a picture `STG_C<NN>.png`. Its scripts must not change the floor height, turn area or bonus collisions, or players without it would play a different match (the program checks).

## Building

`build.bat` compiles `out\EXMoreStuff.exe` with the C# compiler of the Visual Studio 2019 Build Tools, against the .NET Framework 4.8 that comes with Windows; no other libraries. `make_assets.ps1` rebuilds the images in `res\` from `art\`.

## Credits

Made by Claude and oops. The game stage pictures come from EMBER. Street Fighter is Capcom's; this project isn't affiliated with Capcom.

## License

MIT, see [LICENSE](LICENSE).
