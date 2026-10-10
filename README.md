# EX More Stuff

New costumes, stages and music for **Ultra Street Fighter IV** (Steam). Costumes and stages go into new slots, so the game's own are never overwritten; songs replace a stage's or fighter's theme on your PC until you give it back.

## Download

Get the latest `EXMoreStuff-<version>.zip` from [Releases](../../releases), unzip it anywhere and run `EXMoreStuff.exe`. Windows 10 and 11 already have everything it needs (.NET Framework 4.8). Nothing is installed on your PC: to remove the program, delete its folder.

## Using it

1. It finds your Steam copy of USFIV, or asks for its folder.
2. **Costumes:** click a fighter and switch on the costumes you want.
3. **Stages:** switch on custom stages. You can also click one of the game's stages to replace it with another one, on your PC only.
4. **Browse Mods:** USF4's skins and stages on GameBanana, made and shared for free by its modders, with their pictures; each modder's name, in gold, opens the mod's page (thank them there). Search, **Stages**, **Character** (every fighter with their portrait) and the sorts (**Newest** / **Oldest**, **Most** / **Least Downloaded**) narrow and order them.
   - **Install** downloads one and puts it in as a costume or stage of its own, built with your own game's files, so the game's own stay as they are. Each skin goes in the same code for everyone who has it (shown on its card), so players see it on each other; a skin's versions become its colors.
   - Only files that passed GameBanana's virus scan are offered, each download is checked against GameBanana's MD5, and only the game's costume and stage files and pictures are taken out of it: nothing in a mod is ever run.
   - When a modder uploads a new version, its card says **Update** (otherwise **Installed**), a purple dot shows on the tab, and **Updates** lists them with **Update all**, one at a time.
   - An installed mod's card on the Costumes or Stages tab has a **Browse Mods** link back to it.
5. **Advanced** (tools most players never need):
   - **Add Mod From File:** one or more `.zip`, `.rar` or `.7z` files you have (pick several at once). A file added before, even under another name, is left as it is; a list at the end says what went in. A package made for EX More Stuff goes in as it is; a skin or stage made over one of the game's own (downloaded from anywhere) is built into a seat of its own with your game's files, which only you see; a new color (`<FIGHTER>_<NN>_<CC>`, 30-99) goes in beside its costume, in its own number or the next free one.
   - **Old mods:** mods you put in the game's folders by hand (`patch` ... `patch_ae2_tu3`), found by comparing them with the game's own update files. **Import old mods** brings the stages and costumes among them into EX More Stuff: each becomes a package of yours in one of your own seats (stages U01 up, costumes 84 down), kept in the `Mods` folder, and its loose files go to the Recycle Bin. Say **Yes** to keep them showing where they are now (as stage and costume replacements, which you can change any time), **No** to get the game's own back. Other files (moves, effects, menus ...) are only listed.
   - **Music:** pick a stage or fighter on the left, open a song (WAV, MP3, FLAC, M4A, WMA, or a game theme's `.csb`) and set where it loops. The program suggests a loop when the song opens (**Find the loop** asks again). Drag the green loop start and orange loop end on the waveform, zoom with the mouse wheel (down to single samples), scroll with a right-button drag. **Test the loop** plays the last seconds before the loop end and the jump back, over and over. **Hear it as in game** (on by default) plays at the level the game mixes that music at (about 9 dB down for a stage's main layer), so the editor sounds like a match. **Volume** sets it louder or quieter on top of matching the game's volume. **Put in the game** writes it right away (no Apply needed); **Give back the game's music** takes it off. Every song put in the game, or kept with **Save** without putting it in, goes in the `Music` folder beside the program (the folder button next to **Open a song...** opens it), named for where it goes, like `TRN_3_MAIN_Jackson`: the song file as you opened it (`.mp3` ...), its loop and settings (`.json`; opening that song again brings them back) and the game file it made (`.csb`). Custom stages you've installed are listed after the yellow line. MP3, FLAC, M4A and WMA use Windows' own decoders (the "N" editions of Windows need Microsoft's free Media Feature Pack for them); OGG isn't supported: convert it first.
   - **Costume replacement:** on a fighter's page, under **The game's costumes**, click one of the game's costumes to have it play as the **Original** costume or a costume you installed (only you see it, online too; applied with **Apply changes**). The game's alternates and DLC costumes are never used in another's place: they're paid content.
     - A stage's music has three layers the game fades between: **Main**, **Ultra** (both players have half their Ultra gauge) and **Low health** (the timer is at 15 or someone's health is low). By default all three play your song; low health can instead play it with the bass cut (in step with the song), its own song, or the game's.
     - **Round** 2 and 3 songs go in `BGM_<stage>2.csb` and `BGM_<stage>3.csb`, which play with Tom's Round BGM mod: EX More Stuff puts it in for you (`dinput8.dll` next to `SSFIV.exe`) with the first round 2 or 3 music and takes it out when none is left. It never replaces another mod's `dinput8.dll`. Fighter themes have one layer and play every round.
   - **Package codes:** your own packages, in the game or switched off. **Delete** takes one out of the game and off the lists for good (a stage's Music-page songs too) and puts its zip in the Recycle Bin, unless another package uses that zip. Change a stage's code, a costume's slot or a new color's number there and its zip is rewritten with it (a stage's files are re-coded inside), ready to share; if it's in the game it goes in again under the new code, keeping its name, and songs you put on that stage move with it.
   - **Disk space:** how much room your costumes, stages and songs take altogether, and each one, biggest first.
   - **Free codes:** making a costume or stage to share on GameBanana? Pick a fighter to see their seats 8-99 at a glance (free, taken on GameBanana, not free), or check a slot or stage code. A code the game has, one a GameBanana mod already uses, or one that isn't free gets a warning (also when you change a code in Package codes or add a package): you can still use it, but others may have something there.
6. Click **Apply changes**, then close the program. The game has to be closed while changes are applied.

Switching something off and applying removes its files again; it stays listed. **Refresh** looks at the game and the `Mods` folder again, as when the program starts. **Disable everything** switches all costumes and stages off and puts back the game stages you replaced. Songs are given back one by one on the Music page (**Give back the game's music**). If you delete packages' zips from the `Mods` folder yourself, the program asks when it next starts whether to take them out of the game too (as the trash can on their cards does); if you say **No**, they stay and it doesn't ask about them again.

## What it changes

- Custom costumes and stages get their own new files in the game's `patch_ae2_tu3` folder: costumes are slots 8 to 99 (`RYU_12.obj.emo` ...), stages have a three-character code of their own (`STG_C12.emz`, `STG_D05.emz` ...). It never writes over a file it didn't install.
- A replaced game stage is a converted copy of the other stage put in its place. A file that was already there is moved to `patch_ae2_tu3\ex_more_stuff_backup` and comes back when the replacement is switched off.
- A song becomes the game's own sound format (CRI ADX in a `.csb` bank, 44.1 kHz stereo), built on the slot's game bank so its other sounds and volume settings stay: stage themes are `BGM_<stage>.csb` (all three layers, see above), fighter themes `BGM_<fighter>_2CH.csb`. It goes into `patch_ae2_tu3\battle\sound\bgm`; a file already there is moved to `ex_more_stuff_backup` and comes back when the song is taken off. `patch_ae2_tu3\ex_more_stuff_music.json` lists the songs put in.
- Downloads are checked against the catalog's size and SHA-256 before anything is unpacked.
- Every package's zip is kept in the `Mods` folder beside the program: `Mods\Stages`, and `Mods\Characters\<fighter>` (`Mods\Characters\M. Bison` ...) for costumes and colors. Skins and stages from the Browse Mods tab, and mods added from a file, are built into a zip of your own there and installed like any package of yours (rename, Package codes, Delete). Zips already in `Mods` are sorted into these folders when the program starts.
- The program only reads the catalog in this repository, the downloads it lists, and GameBanana's USF4 mods (Browse Mods, Free codes). GameBanana's list (names, files, their links and checksums, never the mods) is kept in `gamebanana.json` beside the program each time it's read, and a copy comes built into the program, so Browse Mods and Free codes still work when GameBanana can't be reached (installing still needs it). It doesn't update itself: when a new version is out, it shows a link to this page.

## Playing online

Custom costumes and stages show up in EMBER builds with custom content support (not yet the official EMBER). Nothing extra is sent over the network. A player who doesn't have your costume sees the fighter's original costume, and a custom stage they don't have shows as one of the game's stages. A replaced stage is only on your PC: your opponent sees their own.

Codes are what players share. GameBanana itself is the list of who has which: the skins from before EX More Stuff (each replaces one of the game's costumes) were each given one code, once (slot 8 up per fighter, oldest first); a newer mod made in a code of its own (`BLK_23.obj.emo` ...) has that code, read from GameBanana's list of what's in its files, and if two use the same one, the first uploaded keeps it. Making a skin or stage to share? Use a code nobody uses yet: a costume slot from 8 to 79 (80 to 84 are kept for players' own mods, 85 to 99 aren't free), a stage code that isn't one A or T with two digits (A12, 1A2, 12A, T12, 1T2, 12T aren't free) or one U with two digits (U12, 1U2, 12U: players' own). A skin without a shared code goes in one of your own seats, 84 down to 80 (then a free one from 79 down), which only you see. Stages work the same way: the GameBanana stages from before EX More Stuff were each given a `B##` code once (The Retrowave Zone is `B01`), a newer one made in a code of its own has that code, and a stage without a shared code gets one of your own codes on your PC (`U01` up). **Advanced > Free codes** shows what's free.

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
    },
    {
      "id": "stage-c12-example", "type": "stage", "code": "C12", "name": "Example stage", "...": "..."
    }
  ]
}
```

**Costumes** are numbered: the game names their files with two digits, so slots 8 to 99 are all there is. Shared mods (GameBanana's, this catalog's) use 8 to 79; 80 to 84 are kept for each player's own mods (5 per fighter), so a mod you add from a file never meets a GameBanana upload there; 85 to 99 aren't free. **Advanced > Add Mod From File** takes any slot, but stops at files of that slot it didn't install itself.

**Stages** have a code: any three capital letters or digits that aren't one of the game's own codes. EMBER sends the code itself as the stage's number, so every catalog can use letters of its own. This catalog owns `C01` to `C99`; any other code (`D05`, `X99`, `ZZZ` ...) is free for players' own packages and other people's catalogs, except codes of one A or T with two digits (`A12`, `1A2`, `12A`, `T12`, `1T2`, `12T`), which aren't free, and codes of one U with two digits (`U12`, `1U2`, `12U`), kept for players' own stages (Add Mod From File takes any code, catalog ones included). A player who doesn't have a stage sees a game stage chosen by rule: by the code's number (`C12` and `D12` alike), or for other codes by their letters.

EX More Stuff keeps its own things beside the program: `Mods` (every package it installs, as a zip; one you add from a file is copied in, so your own file can move), `Music` (your songs), `settings.json` and `gamebanana.json` (in `%APPDATA%\EX More Stuff` instead when the program's folder can't be written to). The game folder only gets the files the game loads, and the list of what's installed there.

A package is a zip with its files side by side, no folders:

- **costume:** `<FIGHTER>_<NN>.obj.emo` and `.nml.emb`, ten colors `<FIGHTER>_<NN>_01` to `_10` `.col.emb` and `.obj.emm`; optionally `.shd.emo`, `.bsr`, `.csb` and a picture per color `<FIGHTER>_<NN>_01.png` ...
- **new color** of any costume, the game's (`01`-`07`) or a custom one: `<FIGHTER>_<NN>_<CC>.col.emb` and `.obj.emm`, `CC` 30 to 99, and a picture `<FIGHTER>_<NN>_<CC>.png` (Ember shows it on the color's card; 256 x 384 like Ember's own). Ember lists it after the costume's own colors; a player without it sees the costume's color 1. New colors aren't shared, so any free number will do.
- **stage:** `STG_<code>.emz`, `STG_<code>.tex.emz` and a picture `STG_<code>.png`; optionally its own music `BGM_<code>.csb`, and `BGM_<code>2.csb` / `BGM_<code>3.csb` for rounds 2 and 3 (a CRI sound bank laid out like the game's stage themes, with 3 cues; without it the stage plays its stand-in stage's music).
- **music:** game files (`.csb`) named `<CODE>_<ROUND>_<LAYER>_<song>.csb`, as the Music page keeps them: `CODE` a stage's (`TRN`, a custom stage's `C71`) or a fighter's (`RYU`, round 1 only), `ROUND` 1 to 3, `LAYER` `MAIN`, `ULTRA` or `LOWHP`; for example `TRN_3_MAIN_Jackson.csb`. Each goes in where its name says (round 2 and 3 music brings Tom's Round BGM mod) and shows on the Music page, to give back there. The song (`TRN_3_MAIN_Jackson.mp3`) and its loop and settings (`.json`) may come along for others to open and change.

## Building

`build.bat` compiles `out\EXMoreStuff.exe` with the C# compiler of the Visual Studio 2019 Build Tools, against the .NET Framework 4.8 that comes with Windows; no other libraries. `make_assets.ps1` rebuilds the images in `res\` from `art\`. The tests in `test\` build with `test\build_test.bat <Name>` (each file's first lines say how to run it). Before a release, refresh GameBanana's built-in list with `test\GameBananaSnapshot.cs` (`res\gamebanana.json`).

## Credits

Made by Claude and oops. The game stage pictures come from EMBER. The mods on Browse Mods are their modders' own, shared on GameBanana. Street Fighter is Capcom's; this project isn't affiliated with Capcom.

## License

MIT, see [LICENSE](LICENSE).
