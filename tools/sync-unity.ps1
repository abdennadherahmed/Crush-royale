<#
  Builds the shared assemblies (Release, netstandard2.1) and copies them into the Unity project,
  then exports the game data files (balance, 1000 stages, story, achievements, cosmetics)
  and regenerates the localization files (tools/LocalizationGen).

  Usage (from the repository root):
    powershell -ExecutionPolicy Bypass -File tools/sync-unity.ps1

  Newtonsoft.Json is NOT copied: Unity provides it through com.unity.nuget.newtonsoft-json.
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$unity = Join-Path $root "unity/CrushRoyale"
$plugins = Join-Path $unity "Assets/Plugins/CrushRoyale"
$data = Join-Path $unity "Assets/Resources/Data"

$dotnet = "dotnet"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -and $env:LOCALAPPDATA) {
    # Windows without admin rights: SDK installed per user by dotnet-install.ps1
    $userDotnet = Join-Path $env:LOCALAPPDATA "Microsoft/dotnet/dotnet.exe"
    if (Test-Path $userDotnet) { $dotnet = $userDotnet }
}

Write-Host "Building client assemblies..."
& $dotnet build (Join-Path $root "src/CrushRoyale.Client/CrushRoyale.Client.csproj") -c Release -v q
if ($LASTEXITCODE -ne 0) { throw "Client build failed" }

$bin = Join-Path $root "src/CrushRoyale.Client/bin/Release/netstandard2.1"
New-Item -ItemType Directory -Force $plugins | Out-Null
foreach ($name in "CrushRoyale.Core", "CrushRoyale.Contracts", "CrushRoyale.Client") {
    Copy-Item (Join-Path $bin "$name.dll") $plugins -Force
    $xml = Join-Path $bin "$name.xml"
    if (Test-Path $xml) { Copy-Item $xml $plugins -Force }
}

Write-Host "Exporting game data..."
New-Item -ItemType Directory -Force $data | Out-Null
& $dotnet run --project (Join-Path $root "src/CrushRoyale.Server/CrushRoyale.Server.csproj") -c Release -- export-stages $data
if ($LASTEXITCODE -ne 0) { throw "Data export failed" }

Write-Host "Generating localization (11 languages)..."
& $dotnet run --project (Join-Path $root "tools/LocalizationGen/LocalizationGen.csproj") -c Release -- $root
if ($LASTEXITCODE -ne 0) { throw "Localization validation failed" }

Write-Host "Unity project synced: $plugins"
