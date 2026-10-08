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
"%CSC%" -nologo -target:winexe -platform:anycpu -optimize+ -langversion:7.3 -out:out\%OUT% ^
  -win32icon:src\app.ico -win32manifest:src\app.manifest ^
  -r:"%FW%\System.dll" -r:"%FW%\System.Core.dll" -r:"%FW%\System.Drawing.dll" -r:"%FW%\System.Windows.Forms.dll" ^
  -r:"%FW%\System.IO.Compression.dll" -r:"%FW%\System.IO.Compression.FileSystem.dll" -r:"%FW%\System.Net.Http.dll" ^
  -r:"%FW%\System.Web.Extensions.dll" ^
  @out\resources.rsp src\*.cs
set RESULT=%ERRORLEVEL%
del out\resources.rsp
exit /b %RESULT%
