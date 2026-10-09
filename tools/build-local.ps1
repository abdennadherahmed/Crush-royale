<#
  Builds the Android binaries with the editor installed on this machine.

    powershell -ExecutionPolicy Bypass -File tools/build-local.ps1            # APK, then the Play bundle
    powershell -ExecutionPolicy Bypass -File tools/build-local.ps1 -Apk       # APK only, the fast one
    powershell -ExecutionPolicy Bypass -File tools/build-local.ps1 -VersionCode 60

  What this is for and what it is not for.

  It is for seeing a real binary in minutes instead of the forty the cloud takes, most of which is spent
  installing Unity. Install it on a phone, check that the atlases packed, that the pets are the right way up,
  that a stage plays.

  It is not for the store. Google Play ties an application to the key that signed its first upload, for its
  whole life: lose that key or leak it and the application can never be updated again, by anyone. That key
  lives encrypted in the GitHub secrets and is applied by the cloud build, so the binary that goes to Play
  comes from there. Without it this build is debug-signed, which installs on a phone and is refused by Play.
#>
param(
    [switch]$Apk,
    [int]$VersionCode = 0,
    [string]$EditorVersion = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "unity/CrushRoyale"
$output = Join-Path $root "build/Android"

function Find-Editor([string]$wanted) {
    $roots = @("C:\Program Files\Unity\Hub\Editor", "$env:LOCALAPPDATA\Unity\Hub\Editor")
    $found = @()
    foreach ($r in $roots) {
        if (-not (Test-Path $r)) { continue }
        foreach ($d in Get-ChildItem $r -Directory -ErrorAction SilentlyContinue) {
            $exe = Join-Path $d.FullName "Editor\Unity.exe"
            if (Test-Path $exe) { $found += [pscustomobject]@{ Version = $d.Name; Exe = $exe; Root = $d.FullName } }
        }
    }
    if ($found.Count -eq 0) { throw "No Unity editor found." }
    if ($wanted) {
        $match = $found | Where-Object { $_.Version -eq $wanted } | Select-Object -First 1
        if (-not $match) { throw "Unity $wanted is not installed. Available: $(($found.Version) -join ', ')" }
        return $match
    }
    return $found | Sort-Object Version -Descending | Select-Object -First 1
}

$editor = Find-Editor $EditorVersion
Write-Host "Editor: $($editor.Version)"

# Without the Android module Unity cannot produce an Android binary at all, and the failure deep inside a build
# log is far less clear than saying so here.
$android = Join-Path $editor.Root "Editor\Data\PlaybackEngines\AndroidPlayer"
if (-not (Test-Path $android)) {
    throw "Android Build Support is not installed for $($editor.Version). Unity Hub > Installs > Manage > Add modules > Android Build Support, with the SDK/NDK and OpenJDK."
}

& powershell -ExecutionPolicy Bypass -File (Join-Path $root "tools/sync-unity.ps1")
if ($LASTEXITCODE -ne 0) { throw "sync-unity.ps1 failed" }

New-Item -ItemType Directory -Force $output | Out-Null
if ($VersionCode -le 0) { $VersionCode = [int](Get-Date -UFormat %j) + 1000 }
$log = Join-Path $output "build.log"

$unityArgs = @(
    "-quit", "-batchmode", "-nographics",
    "-projectPath", $project,
    "-executeMethod", "CrushRoyale.EditorTools.CIBuild.BuildAndroid",
    "-customBuildPath", (Join-Path $output "CrushRoyale.apk"),
    "-androidVersionCode", $VersionCode,
    "-fastTestBuild", "true",
    "-alsoBundle", $(if ($Apk) { "false" } else { "true" }),
    "-logFile", $log
)

Write-Host "Building (first Android build on a machine downloads the SDK platform, which takes a while)..."
$process = Start-Process -FilePath $editor.Exe -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
$code = $process.ExitCode

Get-Content $log -ErrorAction SilentlyContinue | Select-String "CrushRoyaleSetup: targeting|CIBuild:|error CS|BuildFailedException" |
    Select-Object -First 25 | ForEach-Object { $_.Line }

Write-Host ""
foreach ($name in "CrushRoyale.apk", "CrushRoyale.aab") {
    $path = Join-Path $output $name
    if (Test-Path $path) {
        Write-Host ("{0}  {1:N1} MB" -f $name, ((Get-Item $path).Length / 1MB))
    }
}
if ($code -ne 0) {
    Write-Host "Build failed (exit $code). Full log: $log" -ForegroundColor Red
    exit 1
}
Write-Host "Debug-signed: installable on a phone, refused by Google Play." -ForegroundColor Yellow
