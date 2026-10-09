USF4 ROUND BGM MOD v0.2
=======================

PURPOSE
-------
Adds a separate stage BGM bank for Round 2 and Round 3+ without modifying
SSFIV.exe on disk.

Naming:
  Round 1  -> BGM_<STAGE>.csb
  Round 2  -> BGM_<STAGE>2.csb
  Round 3+ -> BGM_<STAGE>3.csb

Example for JPX:
  Round 1  -> BGM_JPX.csb
  Round 2  -> BGM_JPX2.csb
  Round 3+ -> BGM_JPX3.csb

INSTALL
-------
1. Copy dinput8.dll next to SSFIV.exe.

2. Put the optional Round 2 / Round 3 CSBs in a normal BGM override folder,
   recommended:

   patch_ae2_tu3\battle\sound\bgm\

   Example:
   patch_ae2_tu3\battle\sound\bgm\BGM_JPX2.csb
   patch_ae2_tu3\battle\sound\bgm\BGM_JPX3.csb

3. Start the game normally.

The mod creates this log next to SSFIV.exe:
  USF4_Round_BGM.log

ROUND / FALLBACK RULES
----------------------
Round 1:
  Always uses the original stage BGM bank selected by the game.

Round 2:
  Uses BGM_<STAGE>2.csb when present.
  If it is missing, keeps the original Round 1 bank.

Round 3 and later:
  Uses BGM_<STAGE>3.csb when present.
  If it is missing, keeps BGM_<STAGE>2.csb when Round 2 exists.
  If neither optional bank exists, keeps the original bank.

WHEN THE SWITCH HAPPENS
-----------------------
The DLL watches the game's battle flow message ROUND_START (flow ID 14).
The original ROUND_START handler runs first, then the DLL changes the bank.
The new bank is stopped/started so its music begins from the start instead of
fading into the same timestamp as another layer.

The round counter resets on START_DEMO (flow ID 0).

IMPORTANT: CSB STRUCTURE
------------------------
USF4 stage BGM playback normally creates 3 tracks and starts cues 0, 1 and 2
together. For the safest first test, BGM_<STAGE>2.csb and BGM_<STAGE>3.csb
should therefore be authored like a normal stage BGM CSB with the same 3-cue
structure/order as the original stage bank.

This also means the game's normal per-fight layer logic can continue inside
Round 2 and Round 3 (health / Revenge / low timer transitions).

SUPPORTED GAME BUILD
--------------------
This build is made for the SSFIV.exe supplied for the project:
  SHA-256:
  5d724595a8ab3c6c6d6f4959187f756f5be35bb497e51e5233c4e73b18b0b9eb

The hooks are ASLR-safe (RVA-based), but the internal function layout must
match this executable. The DLL verifies the first 5 bytes of both hooked
functions before patching them in memory. If a signature does not match, it
logs an error instead of blindly writing a jump.

NO EXE PATCH
------------
SSFIV.exe is never modified on disk. dinput8.dll is a proxy loaded because the
game already imports DirectInput8Create from DINPUT8.dll. The proxy installs
runtime hooks, then forwards DirectInput8Create to the real Windows dinput8.dll.

COMPATIBILITY WITH AN EXISTING DINPUT8 PROXY
--------------------------------------------
If you already have another dinput8.dll mod/proxy:
  1. Rename the existing DLL to dinput8_chain.dll
  2. Put this mod's dinput8.dll next to SSFIV.exe

This proxy checks for dinput8_chain.dll first and forwards DirectInput8Create
through it. If no chain DLL exists, it loads the Windows system dinput8.dll.

FILES / ROOTS CHECKED FOR OPTIONAL CSBS
---------------------------------------
The mod checks these roots before asking USF4's own asset loader to load the
optional bank:
  patch_ae2_tu3
  patch_ae2_tu2
  patch_ae2_tu1
  patch_ae2
  resource
  dlc\04_ae2
  dlc\03_character_free

The recommended location for custom Round 2 / Round 3 music remains
patch_ae2_tu3\battle\sound\bgm\.

TECHNICAL HOOKS
---------------
SSFIV.exe default image addresses from the supplied report / binary:
  0x5EFD20  battle BGM loader
  0x591520  Sound Unit::OnMessage
  0x6862C0  stage ID -> stage code
  0x675E40  asset load
  0x675D60  asset start/update
  0x675D70  asset destructor (confirmed as vtable first entry in this build)
  0x5922A0  BgmPlayer::Play
  0x592110  BgmPlayer::Stop

The DLL converts these to RVAs so ASLR does not matter.

CURRENT TEST STATUS
-------------------
The DLL has been built and statically validated as a 32-bit Windows DLL, with
DirectInput8Create exported and no external imports. The hook signatures and
addresses were checked against the supplied SSFIV.exe.

It has NOT been executed inside a running copy of USF4 in this environment.
The first in-game test is therefore important. If anything fails, send
USF4_Round_BGM.log and describe whether the crash/error happens at startup,
battle load, Round 2, or Round 3.


v0.2 FIX
--------
- Fixes silent battle music from v0.1.
- The game loader returns an asset wrapper, while BgmPlayer +0x04 requires the low-level bank pointer stored at asset +0x30 once ready.
- Round 1 is now left completely untouched. The original BgmPlayer bank is captured at START_DEMO.
- Round 2/3 assets are polled for readiness before their internal bank pointer is used.


v0.3 (EX More Stuff)
--------------------
- Round 2 / Round 3 banks follow the bank the game's BGM loader actually loads
  (BGM_<code>.csb), noted at the loader's one call of the asset loader
  (0x5EFED8, "call 0x675E40": the call is pointed at a small function that
  records the path and calls 0x675E40 unchanged). A custom stage played through
  EMBER loads its own BGM_<code>.csb there, so its BGM_<code>2.csb and
  BGM_<code>3.csb are used; a stage-code lookup from inside this DLL would get
  EMBER's stand-in stage. If the call site's 5 bytes differ, or the path isn't
  BGM_<code>.csb, the code comes from the lookup as in v0.2.
- Builds with clang (src\build_clang.bat) or Visual Studio's x86 compiler
  (src\build_msvc.bat).
