# Copies the current checkout into the public repository's working copy (same exclusions as export-public.ps1),
# then commits and tags it. Pushing is a separate, explicit step:
#   powershell -ExecutionPolicy Bypass -File release/sync-public.ps1 [-Target <public working copy>]
#   git -C <public working copy> push origin main --tags
param([string]$Target = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Target) { $Target = Join-Path (Split-Path -Parent $root) "SkulInsight_QoL" }
if (-not (Test-Path (Join-Path $Target ".git"))) { throw "$Target is not a git repository (use export-public.ps1 first)." }

[xml]$project = Get-Content (Join-Path $root "DamageInsight/DamageInsight.csproj")
$version = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

$exclude = @("docs/PROJECT_STATUS.md", "docs/RELEASE_PLAN.md", "docs/CODEX_PLAN.md", "docs/ATTACK_ANALYSIS.md", "docs/GAME_KNOWLEDGE.md", "tools/*", "DamageInsight.Tests/Fixtures/*")
$keep = @(".git", "DamageInsight.Tests/Fixtures/README.md")

# Remove files that no longer exist here (everything tracked in the public copy except the kept ones).
Push-Location $Target
$publicFiles = git ls-files
Pop-Location
Push-Location $root
$files = git ls-files | Where-Object { $f = $_; -not ($exclude | Where-Object { $f -like $_ }) }
Pop-Location
foreach ($file in $publicFiles) {
    if ($keep -contains $file) { continue }
    if ($files -notcontains $file) { Remove-Item (Join-Path $Target $file) -Force }
}
foreach ($file in $files) {
    $dest = Join-Path $Target $file
    New-Item -ItemType Directory -Force (Split-Path -Parent $dest) | Out-Null
    Copy-Item (Join-Path $root $file) $dest -Force
}

Push-Location $Target
try {
    git add -A
    git commit -q -m "SkulInsight QoL $version"
    git tag -a "v$version" -m "SkulInsight QoL $version"
    git log --format="%h %an %s" -1
}
finally { Pop-Location }
Write-Host "Public copy updated to $version (not pushed)."
