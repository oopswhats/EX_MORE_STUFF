# Gets a release of EX More Stuff ready to upload to GitHub, in publish\:
#   publish\repo\                      what goes in the repository (source, images, README, license, build scripts;
#                                      never the test content in catalog_test, which holds the game's own files)
#   publish\EXMoreStuff-<version>.zip  the download for the Releases page: the program and a short how-to
# The program is built first; the catalog address in src\Program.cs must already name the repository.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$program = Get-Content "$root\src\Program.cs" -Raw
if ($program -match "OWNER/REPO") { throw "src\Program.cs still has the placeholder catalog address" }
$version = [regex]::Match((Get-Content "$root\src\AssemblyInfo.cs" -Raw), 'AssemblyVersion\("(\d+)\.(\d+)').Groups | Select-Object -Skip 1 | ForEach-Object Value
$version = $version -join "."

& "$root\build.bat"
if ($LASTEXITCODE -ne 0) { throw "the build failed" }

$publish = Join-Path $root "publish"
if (Test-Path $publish) { [IO.Directory]::Delete($publish, $true) }
$repo = Join-Path $publish "repo"
New-Item -ItemType Directory -Force $repo | Out-Null
foreach ($file in "README.md", "LICENSE", ".gitignore", "catalog.json", "build.bat", "make_assets.ps1", "make_publish.ps1") { Copy-Item "$root\$file" "$repo\$file" }
foreach ($folder in "src", "res", "art", "tools", ".github") { Copy-Item "$root\$folder" "$repo\$folder" -Recurse }   # .github: the hourly GameBanana codes job (codes.json lives on the "codes" branch, never here)
New-Item -ItemType Directory -Force "$repo\test" | Out-Null
Copy-Item "$root\test\*.cs" "$repo\test\"

$zip = Join-Path $publish "EXMoreStuff-$version.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, "Create")
[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, "$root\out\EXMoreStuff.exe", "EXMoreStuff.exe")
$howTo = $archive.CreateEntry("How to use.txt")
$writer = New-Object IO.StreamWriter($howTo.Open())
$writer.Write(@"
EX More Stuff $version
New costumes and stages for Ultra Street Fighter IV, in new slots: the game's own are never overwritten.

1. Unzip this folder anywhere and run EXMoreStuff.exe (Windows 10 and 11 have everything it needs).
2. Switch on the costumes and stages you want, click "Apply changes", close it, play.
   The game has to be closed while changes are applied.
3. To undo: switch things off and apply again, or click "Disable everything".

Nothing is installed on your PC: to remove the program, delete this folder.
Online, custom costumes and stages need an EMBER build with custom content support.
More: the GitHub page this came from.
"@)
$writer.Dispose()
$archive.Dispose()
"publish\repo: {0} files; {1}: {2:N1} MB" -f (Get-ChildItem $repo -Recurse -File).Count, (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB)
