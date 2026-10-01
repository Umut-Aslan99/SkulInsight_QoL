# Creates the GitHub release for a version: tag v<version> (must already be pushed), the version's section of
# CHANGELOG.md as the text, and the Thunderstore zip attached.
# Uses the GitHub login Git already stores (Git Credential Manager); the token is never written anywhere.
#   powershell -ExecutionPolicy Bypass -File release/github-release.ps1 [-Version 0.9.3]
param(
    [string]$Version = "",
    [string]$Repo = "Umut-Aslan99/SkulInsight_QoL"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    [xml]$project = Get-Content (Join-Path $root "DamageInsight/DamageInsight.csproj")
    $Version = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
}
$zip = Join-Path $PSScriptRoot "out/DocRun-SkulInsight_QoL-$Version.zip"
if (-not (Test-Path $zip)) { throw "Missing $zip (run release/package.ps1 first)" }

# The version's changelog section: from "## <version>" to the next "## ".
$changelog = Get-Content (Join-Path $root "CHANGELOG.md") -Raw -Encoding UTF8
$match = [regex]::Match($changelog, "(?ms)^## $([regex]::Escape($Version))\b[^\n]*\n(.*?)(?=^## |\z)")
if (-not $match.Success) { throw "No '## $Version' section in CHANGELOG.md" }
$notes = $match.Groups[1].Value.Trim() + "`n`nInstall with r2modman / Gale from [Thunderstore](https://thunderstore.io/c/skul-the-hero-slayer/p/DocRun/SkulInsight_QoL/), or download the zip below and follow the manual installation in the README."

$credential = "protocol=https`nhost=github.com`n`n" | git credential fill
$token = ($credential | Where-Object { $_ -like "password=*" }) -replace "^password=", ""
if (-not $token) { throw "No stored GitHub login found (push once with git to store it)." }
$headers = @{ Authorization = "Bearer $token"; Accept = "application/vnd.github+json"; "X-GitHub-Api-Version" = "2022-11-28" }

$existing = $null
try { $existing = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repo/releases/tags/v$Version" } catch { }
if ($existing) {
    Write-Host "Release v$Version already exists: $($existing.html_url)"
    $release = $existing
} else {
    $body = @{ tag_name = "v$Version"; name = "SkulInsight QoL $Version"; body = $notes; draft = $false; prerelease = $false } | ConvertTo-Json
    $release = Invoke-RestMethod -Method Post -Headers $headers -Uri "https://api.github.com/repos/$Repo/releases" `
        -Body ([Text.Encoding]::UTF8.GetBytes($body)) -ContentType "application/json; charset=utf-8"
    Write-Host "Created release: $($release.html_url)"
}

$assetName = Split-Path -Leaf $zip
if ($release.assets | Where-Object { $_.name -eq $assetName }) {
    Write-Host "Asset $assetName is already attached."
} else {
    $uploadUrl = "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=$assetName"
    $asset = Invoke-RestMethod -Method Post -Headers $headers -Uri $uploadUrl -InFile $zip -ContentType "application/zip"
    Write-Host "Attached $($asset.name) ($($asset.size) bytes)"
}
