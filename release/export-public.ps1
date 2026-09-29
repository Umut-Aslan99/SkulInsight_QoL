# Exports a clean copy of this repository for the public GitHub repo, with a fresh git history.
# Left out: internal notes (docs/PROJECT_STATUS.md, docs/RELEASE_PLAN.md, docs/CODEX_PLAN.md) and the game data
# test fixtures (the tests that need them skip themselves).
# Usage: powershell -ExecutionPolicy Bypass -File release/export-public.ps1 -GitHubUser <name> -GitHubEmail <id+name@users.noreply.github.com>
param(
    [Parameter(Mandatory = $true)][string]$GitHubUser,
    [Parameter(Mandatory = $true)][string]$GitHubEmail,
    [string]$Target = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
# Default: a "SkulInsight_QoL" folder next to this repository.
if (-not $Target) { $Target = Join-Path (Split-Path -Parent $root) "SkulInsight_QoL" }
if (Test-Path (Join-Path $Target ".git")) { throw "$Target already has a git repository; update it by hand instead." }

$exclude = @(
    "docs/PROJECT_STATUS.md", "docs/RELEASE_PLAN.md", "docs/CODEX_PLAN.md",
    "DamageInsight.Tests/Fixtures/*"
)
New-Item -ItemType Directory -Force $Target | Out-Null
Push-Location $root
try {
    # Only tracked files, so build output and local files never leave the machine.
    foreach ($file in (git ls-files)) {
        if ($exclude | Where-Object { $file -like $_ }) { continue }
        $dest = Join-Path $Target $file
        New-Item -ItemType Directory -Force (Split-Path -Parent $dest) | Out-Null
        Copy-Item $file $dest
    }
}
finally { Pop-Location }
New-Item -ItemType Directory -Force (Join-Path $Target "DamageInsight.Tests/Fixtures") | Out-Null
Set-Content (Join-Path $Target "DamageInsight.Tests/Fixtures/README.md") "Game data for some tests goes here (not published). See docs/BUILDING.md."

Push-Location $Target
try {
    git init -q -b main
    git config user.name $GitHubUser
    git config user.email $GitHubEmail
    git add -A
    git commit -q -m "SkulInsight QoL (initial public version)"
    git log --format="%h %an <%ae> %s" -1
}
finally { Pop-Location }
Write-Host "Public repository prepared in $Target (not pushed)."
