# Builds Better Workbench Defaults against the installed RimWorld + Harmony assemblies.
# Uses the Visual Studio BuildTools Roslyn compiler directly (no .NET SDK required).
$ErrorActionPreference = "Stop"

$csc      = "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
$managed  = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed"
$harmony  = "C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\Current\Assemblies\0Harmony.dll"
$modRoot  = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\BetterWorkbenchDefaults"
$srcDir   = Join-Path $modRoot "Source"
$outDir   = Join-Path $modRoot "Assemblies"
$outDll   = Join-Path $outDir "BetterWorkbenchDefaults.dll"

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$refs = @(
  (Join-Path $managed "mscorlib.dll"),
  (Join-Path $managed "System.dll"),
  (Join-Path $managed "System.Core.dll"),
  (Join-Path $managed "netstandard.dll"),
  (Join-Path $managed "Assembly-CSharp.dll"),
  (Join-Path $managed "UnityEngine.CoreModule.dll"),
  (Join-Path $managed "UnityEngine.IMGUIModule.dll"),
  (Join-Path $managed "UnityEngine.TextRenderingModule.dll"),
  $harmony
)

foreach ($r in $refs) { if (-not (Test-Path $r)) { throw "Missing reference: $r" } }

$sources = Get-ChildItem $srcDir -Filter *.cs -File | ForEach-Object { $_.FullName }

$cscArgs = @(
  "/nologo", "/target:library", "/optimize+", "/nostdlib+", "/noconfig",
  "/langversion:latest", "/warnaserror-", "/nowarn:1701,1702",
  "/out:`"$outDll`""
)
foreach ($r in $refs)    { $cscArgs += "/reference:`"$r`"" }
foreach ($s in $sources) { $cscArgs += "`"$s`"" }

Write-Host "Compiling $($sources.Count) source files -> $outDll"
& $csc $cscArgs
Write-Host "csc exit code: $LASTEXITCODE"
if ($LASTEXITCODE -eq 0) { Write-Host "OK: $((Get-Item $outDll).Length) bytes" }
exit $LASTEXITCODE
