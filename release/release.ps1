# Complete release of the version in DamageInsight.csproj, in one command:
#   1. all tests, 2. Thunderstore zip, 3. public GitHub repo updated, tagged and pushed,
#   4. GitHub release with the changelog and the zip, 5. upload to Thunderstore.
# Needs: CHANGELOG.md section "## <version>", the public working copy next to this repo, a stored GitHub login,
# and the Thunderstore service account token in %USERPROFILE%\.skulinsight\thunderstore.token (never in the repo).
#   powershell -ExecutionPolicy Bypass -File release/release.ps1 [-SkipThunderstore]
param([switch]$SkipThunderstore)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content (Join-Path $root "DamageInsight/DamageInsight.csproj")
$version = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$public = Join-Path (Split-Path -Parent $root) "SkulInsight_QoL"
Write-Host "=== Releasing SkulInsight QoL $version ==="

if (git -C $root status --porcelain) { throw "Uncommitted changes in the private repo: commit them first." }
$changelog = Get-Content (Join-Path $root "CHANGELOG.md") -Raw -Encoding UTF8
if ($changelog -notmatch "(?m)^## $([regex]::Escape($version))\b") { throw "CHANGELOG.md has no '## $version' section." }
if (git -C $public tag --list "v$version") { throw "v$version is already released (tag exists in the public repo)." }

Write-Host "--- 1/5 tests"
dotnet test (Join-Path $root "SkulMods.sln") -p:DeployToSkul=false --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

Write-Host "--- 2/5 package"
& (Join-Path $PSScriptRoot "package.ps1")
$zip = Join-Path $PSScriptRoot "out/DocRun-SkulInsight_QoL-$version.zip"

Write-Host "--- 3/5 public repository"
& (Join-Path $PSScriptRoot "sync-public.ps1") -Target $public
git -C $public push -q origin main --tags
if ($LASTEXITCODE -ne 0) { throw "Push to GitHub failed." }

Write-Host "--- 4/5 GitHub release"
& (Join-Path $PSScriptRoot "github-release.ps1") -Version $version

if ($SkipThunderstore) {
    Write-Host "--- 5/5 Thunderstore skipped"
} else {
    Write-Host "--- 5/5 Thunderstore"
    $tokenFile = Join-Path $env:USERPROFILE ".skulinsight/thunderstore.token"
    if (-not (Test-Path $tokenFile)) { throw "Missing $tokenFile" }
    $token = (Get-Content $tokenFile -Raw).Trim()
    $tcli = Join-Path $env:USERPROFILE ".dotnet/tools/tcli.exe"
    & $tcli publish --file $zip --token $token --config-path (Join-Path $PSScriptRoot "thunderstore.toml") `
        --package-namespace DocRun --package-name SkulInsight_QoL --package-version $version
    if ($LASTEXITCODE -ne 0) { throw "Thunderstore upload failed." }
}
Write-Host "=== SkulInsight QoL $version released ==="
