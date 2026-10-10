@echo off
rem Builds EXMoreStuff.exe: C# with the Visual Studio 2019 Build Tools' compiler, against the .NET Framework 4.8 that
rem Windows 10/11 already have (nothing to install, no extra libraries, no packer). The game stages' pictures
rem (res\stages, from EMBER), the background, logo and wordmark are built in; fighter portraits are read from the
rem player's game at runtime. Optional argument: another output name (for when EXMoreStuff.exe is open).
setlocal
set CSC=C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set OUT=%~1
if "%OUT%"=="" set OUT=EXMoreStuff.exe
cd /d "%~dp0"
if not exist out mkdir out
(for %%f in (res\stages\*.jpg) do @echo -resource:res\stages\%%~nxf,stages.%%~nxf) > out\resources.rsp
echo -resource:res\background.jpg,background.jpg>> out\resources.rsp
echo -resource:res\logo.png,logo.png>> out\resources.rsp
echo -resource:res\wordmark.png,wordmark.png>> out\resources.rsp
rem Tom's Round BGM mod (dinput8.dll), put next to SSFIV.exe with round 2/3 music (src\RoundMod.cs)
echo -resource:res\roundbgm\dinput8.dll,roundbgm.dll>> out\resources.rsp
rem GameBanana's list as of this version, for when GameBanana can't be reached (test\GameBananaSnapshot.cs refreshes it)
echo -resource:res\gamebanana.json,gamebanana.json>> out\resources.rsp
rem GameBanana's codes kept for good as of this version (the codes branch's codes.json), for when GitHub can't be reached
echo -resource:res\codes.json,codes.json>> out\resources.rsp
rem the game's costumes in every color (SF4 Ember's selection art, made small by test\EmberArt.cs): a sheet per costume
(for %%f in (res\costumes\*.jpg) do @echo -resource:res\costumes\%%~nxf,costumes.%%~nxf) >> out\resources.rsp
echo -resource:res\costumes\index.txt,costumes.index.txt>> out\resources.rsp
rem the game's official update files (paths and sizes), so Advanced > Old mods can tell mods apart (test\OfficialManifest.cs)
echo -resource:res\official.json,official.json>> out\resources.rsp
"%CSC%" -nologo -target:winexe -platform:anycpu -optimize+ -langversion:7.3 -out:out\%OUT% ^
  -win32icon:src\app.ico -win32manifest:src\app.manifest ^
  -r:"%FW%\System.dll" -r:"%FW%\System.Core.dll" -r:"%FW%\System.Drawing.dll" -r:"%FW%\System.Windows.Forms.dll" ^
  -r:"%FW%\System.IO.Compression.dll" -r:"%FW%\System.IO.Compression.FileSystem.dll" -r:"%FW%\System.Net.Http.dll" ^
  -r:"%FW%\System.Web.Extensions.dll" -r:"%FW%\Microsoft.VisualBasic.dll" ^
  @out\resources.rsp src\*.cs
set RESULT=%ERRORLEVEL%
del out\resources.rsp
exit /b %RESULT%
