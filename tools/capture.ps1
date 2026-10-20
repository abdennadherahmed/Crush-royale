<#
  Photographie tous les ecrans du jeu, en s'assurant que le code vient d'etre compile.

  Unity, lance en batch avec -quit, recompile puis quitte sans executer la methode demandee. Une capture
  prise juste apres une modification renvoyait donc les images de la veille, sans le dire : j'ai corrige
  trois fois le meme defaut en regardant une photo qui ne bougeait pas. Le premier passage compile, le
  second photographie.

  Usage (depuis la racine du depot) :
    powershell -ExecutionPolicy Bypass -File tools/capture.ps1 [-Out shots]
#>
param(
    [string]$Out = "shots"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "unity/CrushRoyale"
$output = Join-Path $root $Out
$log = Join-Path $root "shots.log"

$unity = (Get-ChildItem "C:\Program Files\Unity\Hub\Editor\*\Editor\Unity.exe" | Select-Object -Last 1).FullName
if (-not $unity) { throw "Unity introuvable dans C:\Program Files\Unity\Hub\Editor." }

Write-Host "Compilation..."
& $unity -batchmode -quit -nographics -projectPath $project -logFile (Join-Path $root "compile.log") | Out-Null
$errors = Select-String -Path (Join-Path $root "compile.log") -Pattern "error CS" | Select-Object -First 5
if ($errors) {
    $errors | ForEach-Object { Write-Host $_.Line }
    throw "La compilation a echoue : rien n'a ete photographie."
}

Write-Host "Captures..."
& $unity -batchmode -quit -force-graphics -projectPath $project `
    -executeMethod CrushRoyale.EditorTools.ScreenshotTool.CaptureAll `
    -screenshotOutput $output -logFile $log | Out-Null

$done = Select-String -Path $log -Pattern "ScreenshotTool: \d+ screenshot" | Select-Object -First 1
if (-not $done) { throw "Unity n'a pas execute la capture : voir $log." }
Write-Host $done.Line.Trim()

# L'editeur retouche ces fichiers a chaque ouverture ; les laisser modifies pollue chaque commit.
git -C $root checkout -- unity/CrushRoyale/Packages/manifest.json unity/CrushRoyale/ProjectSettings 2>$null
Remove-Item (Join-Path $project "Packages/packages-lock.json") -ErrorAction SilentlyContinue
