<#
  Lance le serveur du jeu sur cette machine, en memoire, pour jouer une action en ligne de bout en bout.

  Jusqu'ici l'editeur ne pouvait pas se connecter : la connexion passe par un compte Google, qu'un PC
  de developpement n'a pas. Tout ce qui demande le serveur (chercher une partie, envoyer une vie,
  rejoindre une guilde, acheter) ne pouvait donc etre essaye que sur un telephone, apres un build.

  En Development, le serveur accepte l'entete X-Dev-User a la place d'un jeton : c'est ainsi que ses
  propres tests s'authentifient. Le harnais s'en sert pour jouer comme un joueur connecte.

  Rien ne touche la vraie base : le magasin est en memoire et tout disparait a l'arret.

  Usage (depuis la racine du depot) :
    powershell -ExecutionPolicy Bypass -File tools/local-server.ps1
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# Le dotnet du PATH peut n'etre que l'executable, sans kit de developpement : `dotnet run` echoue alors
# avec « No SDKs were found », ce qui ressemble a une absence de dotnet alors qu'il est bien la.
$dotnet = $null
foreach ($candidate in @("dotnet", (Join-Path $env:USERPROFILE ".dotnet/dotnet.exe"))) {
    if ($candidate -ne "dotnet" -and -not (Test-Path $candidate)) { continue }
    if ($candidate -eq "dotnet" -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) { continue }
    $sdks = & $candidate --list-sdks 2>$null
    if ($sdks) { $dotnet = $candidate; break }
}
if (-not $dotnet) {
    throw "Aucun kit .NET trouve. Installez-le : https://dot.net puis relancez."
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5199"
# Pas de base de donnees : le magasin en memoire, celui des tests.
$env:Database__UseInMemory = "true"
$env:Game__EnableBackgroundJobs = "false"

Write-Host "Serveur local sur http://localhost:5199 (Ctrl+C pour arreter)."
& $dotnet run --project (Join-Path $root "src/CrushRoyale.Server") --no-launch-profile
