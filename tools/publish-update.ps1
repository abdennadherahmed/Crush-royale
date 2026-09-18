# Publishes a built APK for the in-game updater (Assets/Scripts/Core/AppUpdater.cs), from GitHub Actions.
#
#   ./tools/publish-update.ps1 -Apk build/Android/CrushRoyale.apk -VersionCode 30
#
# Needs SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY (repository secret: Supabase > Project Settings > API Keys >
# secret key). Uploads the APK in parts (the free plan caps files at 50 MB) to the public "releases" bucket, then
# android/latest.json, and removes older builds. Without the key it only prints a warning.
param(
    [Parameter(Mandatory = $true)][string]$Apk,
    [Parameter(Mandatory = $true)][long]$VersionCode,
    [long]$MinVersionCode = 0,
    [int]$PartMegabytes = 45
)

$ErrorActionPreference = "Stop"
$url = $env:SUPABASE_URL
$key = $env:SUPABASE_SERVICE_ROLE_KEY
if ([string]::IsNullOrWhiteSpace($url) -or [string]::IsNullOrWhiteSpace($key)) {
    Write-Host "::warning::SUPABASE_SERVICE_ROLE_KEY secret missing: this APK is not published for in-game updates."
    exit 0
}
$url = $url.TrimEnd('/')
$key = $key.Trim()
$headers = @{ apikey = $key }
# Legacy service_role keys are JWTs (sent as bearer too); new sb_secret_ keys go in the apikey header only.
if ($key.StartsWith("eyJ")) { $headers["Authorization"] = "Bearer $key" }
$bucket = "releases"

# Public, read-only bucket (created once; later runs just get "already exists").
try {
    $body = @{ id = $bucket; name = $bucket; public = $true; file_size_limit = 52428800 } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "$url/storage/v1/bucket" -Headers $headers -ContentType "application/json" -Body $body | Out-Null
    Write-Host "Created the public bucket '$bucket'."
} catch {
    Write-Host "Bucket '$bucket' already present."
}

function Upload([string]$path, [byte[]]$bytes, [string]$contentType, [string]$cache) {
    $h = $headers.Clone()
    $h["x-upsert"] = "true"
    $h["cache-control"] = $cache
    Invoke-RestMethod -Method Post -Uri "$url/storage/v1/object/$bucket/$path" -Headers $h -ContentType $contentType -Body $bytes | Out-Null
}

$data = [IO.File]::ReadAllBytes((Resolve-Path $Apk))
$sha = (Get-FileHash -Algorithm SHA256 -Path $Apk).Hash.ToLowerInvariant()
$partSize = $PartMegabytes * 1024 * 1024
$parts = @()
for ($offset = 0; $offset -lt $data.Length; $offset += $partSize) {
    $length = [Math]::Min($partSize, $data.Length - $offset)
    $chunk = New-Object byte[] $length
    [Array]::Copy($data, $offset, $chunk, 0, $length)
    $name = "android/$VersionCode/part$($parts.Count)"
    Upload $name $chunk "application/octet-stream" "max-age=31536000"
    $parts += $name
    Write-Host "Uploaded $name ($length bytes)"
}

# latest.json last: players only see a build once all of its parts are online.
$manifest = [ordered]@{
    versionCode = $VersionCode
    minVersionCode = $MinVersionCode
    size = $data.Length
    sha256 = $sha
    parts = $parts
    publishedAt = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json
Upload "android/latest.json" ([Text.Encoding]::UTF8.GetBytes($manifest)) "application/json" "no-cache, max-age=0"
Write-Host "Published build $VersionCode for in-game updates ($($data.Length) bytes, sha256 $sha)."

# Keep the current and the previous build only (1 GB free storage).
try {
    $listBody = @{ prefix = "android/"; limit = 100 } | ConvertTo-Json
    $entries = Invoke-RestMethod -Method Post -Uri "$url/storage/v1/object/list/$bucket" -Headers $headers -ContentType "application/json" -Body $listBody
    foreach ($entry in $entries) {
        $folder = 0L
        if ($null -eq $entry.id -and [long]::TryParse($entry.name, [ref]$folder) -and $folder -lt $VersionCode - 1) {
            $files = Invoke-RestMethod -Method Post -Uri "$url/storage/v1/object/list/$bucket" -Headers $headers -ContentType "application/json" -Body (@{ prefix = "android/$folder/"; limit = 100 } | ConvertTo-Json)
            $names = @($files | ForEach-Object { "android/$folder/$($_.name)" })
            if ($names.Count -gt 0) {
                Invoke-RestMethod -Method Delete -Uri "$url/storage/v1/object/$bucket" -Headers $headers -ContentType "application/json" -Body (@{ prefixes = $names } | ConvertTo-Json) | Out-Null
                Write-Host "Removed old build $folder."
            }
        }
    }
} catch {
    Write-Host "::warning::Old builds not cleaned: $($_.Exception.Message)"
}
