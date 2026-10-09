<#
  Captures every screen of the game with the Unity editor installed on this machine, and leaves the project
  files exactly as it found them.

  Usage (from the repository root):
    powershell -ExecutionPolicy Bypass -File tools/capture.ps1
    powershell -ExecutionPolicy Bypass -File tools/capture.ps1 -Output C:\somewhere\shots -SkipSync

  Why it exists: the editor on a developer machine is rarely the version the CI builds with, and opening the
  project rewrites ProjectVersion.txt, Packages/manifest.json and ProjectSettings.asset to that newer editor,
  with package versions the CI cannot resolve. Committing any of it breaks the APK. Those three files are
  therefore restored after every run, and only when they were clean before it, so a deliberate edit survives.
#>
param(
    [string]$Output = "",
    [string]$EditorVersion = "",
    [switch]$SkipSync
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "unity/CrushRoyale"

if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $root "screenshots" }

# ------------------------------------------------------------------ find the editor

function Find-Editor([string]$wanted) {
    $roots = @("C:\Program Files\Unity\Hub\Editor", "$env:LOCALAPPDATA\Unity\Hub\Editor", "C:\Program Files\Unity\Editor")
    $found = @()
    foreach ($r in $roots) {
        if (-not (Test-Path $r)) { continue }
        foreach ($d in Get-ChildItem $r -Directory -ErrorAction SilentlyContinue) {
            $exe = Join-Path $d.FullName "Editor\Unity.exe"
            if (Test-Path $exe) { $found += [pscustomobject]@{ Version = $d.Name; Exe = $exe } }
        }
        $exe = Join-Path $r "Unity.exe"
        if (Test-Path $exe) { $found += [pscustomobject]@{ Version = "unknown"; Exe = $exe } }
    }
    if ($found.Count -eq 0) { throw "No Unity editor found. Install one through Unity Hub." }
    if ($wanted) {
        $match = $found | Where-Object { $_.Version -eq $wanted } | Select-Object -First 1
        if ($match) { return $match }
        throw "Unity $wanted is not installed. Available: $(($found.Version) -join ', ')"
    }
    # The version the project was authored with, when it happens to be installed: same editor as the CI, so the
    # captures show what the build will show.
    $authored = (Get-Content (Join-Path $project "ProjectSettings/ProjectVersion.txt") -ErrorAction SilentlyContinue |
        Select-String "^m_EditorVersion:" | ForEach-Object { $_.Line.Split(":")[1].Trim() })
    $match = $found | Where-Object { $_.Version -eq $authored } | Select-Object -First 1
    if ($match) { return $match }
    $newest = $found | Sort-Object Version -Descending | Select-Object -First 1
    Write-Host "Project authored with $authored, which is not installed: capturing with $($newest.Version)." -ForegroundColor Yellow
    Write-Host "Layout differences between the two editors will show up here and not in the build, or the reverse." -ForegroundColor Yellow
    return $newest
}

$editor = Find-Editor $EditorVersion
Write-Host "Editor: $($editor.Version)"

# ------------------------------------------------------------------ shared assemblies

if (-not $SkipSync) {
    # Assets/Plugins/CrushRoyale/*.dll is ignored by git, so whatever sits there is as old as the last run on this
    # machine. Out of date assemblies fail the capture with twenty "type or namespace not found" errors.
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root "tools/sync-unity.ps1")
    if ($LASTEXITCODE -ne 0) { throw "sync-unity.ps1 failed" }
}

# ------------------------------------------------------------------ remember the project files

$guarded = @(
    "ProjectSettings/ProjectVersion.txt",
    "ProjectSettings/ProjectSettings.asset",
    "Packages/manifest.json"
)

# git writes its line-ending advice to stderr, and under "stop on error" PowerShell turns any stderr from a
# native command into a terminating error. Swallowing both streams keeps the exit code, which is what we read.
function Invoke-Git {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $null = & git @args 2>&1
    $ErrorActionPreference = $previous
    return $LASTEXITCODE
}

$wasClean = @{}
foreach ($rel in $guarded) {
    $wasClean[$rel] = (Invoke-Git -C $root diff --quiet -- "unity/CrushRoyale/$rel") -eq 0
}

# ------------------------------------------------------------------ capture

New-Item -ItemType Directory -Force $Output | Out-Null
Get-ChildItem $Output -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force
$log = Join-Path $Output "unity.log"

# -force-graphics because batchmode alone leaves Unity without a graphics device, and every PNG comes out empty.
#
# Start-Process -Wait and not the call operator: Unity hands its work to a second process and the first one
# returns at once, so the call operator reported success in seconds, with nothing captured and the project
# files restored while the editor was still writing them.
$unityArgs = @(
    "-quit", "-batchmode", "-force-graphics",
    "-projectPath", $project,
    "-executeMethod", "CrushRoyale.EditorTools.ScreenshotTool.CaptureAll",
    "-screenshotOutput", $Output,
    "-logFile", $log
)
Write-Host "Capturing (first run on a machine imports the whole project and takes a few minutes)..."
$process = Start-Process -FilePath $editor.Exe -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
$code = $process.ExitCode

# The editor itself may still be flushing: wait for every Unity.exe holding this project to be gone.
while (Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $editor.Exe }) {
    Start-Sleep -Seconds 2
}

# ------------------------------------------------------------------ put the project files back

foreach ($rel in $guarded) {
    if (-not $wasClean[$rel]) { continue }
    $path = "unity/CrushRoyale/$rel"
    if ((Invoke-Git -C $root diff --quiet -- $path) -ne 0) {
        $null = Invoke-Git -C $root checkout -- $path
        Write-Host "Restored $rel (the editor had upgraded it)."
    }
}

# ------------------------------------------------------------------ report

$shots = @(Get-ChildItem $Output -Filter *.png -ErrorAction SilentlyContinue)
Write-Host ""
Write-Host "$($shots.Count) screen(s) in $Output"
$audit = Join-Path $Output "layout-audit.md"
if (Test-Path $audit) {
    Write-Host ""
    Get-Content $audit | Select-Object -First 40
}
if ($code -ne 0 -or $shots.Count -eq 0) {
    Write-Host ""
    Write-Host "Capture failed (exit $code). Compiler errors, if any:" -ForegroundColor Red
    Get-Content $log -ErrorAction SilentlyContinue | Select-String "error CS" | Select-Object -First 20
    exit 1
}
