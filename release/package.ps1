# Builds the Release DLL and packs the Thunderstore zip:
#   release/out/DocRun-SkulInsight_QoL-<version>.zip
#     manifest.json, icon.png, README.md, CHANGELOG.md, plugins/DamageInsight.dll
# Usage: powershell -ExecutionPolicy Bypass -File release/package.ps1 [-RepoUrl https://github.com/<user>/SkulInsight_QoL]
param(
    [string]$RepoUrl = "https://github.com/Umut-Aslan99/SkulInsight_QoL"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$namespace = "DocRun"
$name = "SkulInsight_QoL"

# Version from the project file (single source of truth).
[xml]$project = Get-Content (Join-Path $root "DamageInsight/DamageInsight.csproj")
$version = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "No <Version> in DamageInsight.csproj" }

dotnet build (Join-Path $root "DamageInsight/DamageInsight.csproj") -c Release -p:DeployToSkul=false --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Release build failed" }
$dll = Join-Path $root "DamageInsight/bin/Release/DamageInsight.dll"

$stage = Join-Path $PSScriptRoot "out/stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force (Join-Path $stage "plugins") | Out-Null

Copy-Item $dll (Join-Path $stage "plugins")
Copy-Item (Join-Path $PSScriptRoot "icon.png") $stage
Copy-Item (Join-Path $root "CHANGELOG.md") $stage

# Thunderstore shows the README as the package page: links must be absolute.
$readme = Get-Content (Join-Path $root "README.md") -Raw -Encoding UTF8
$readme = $readme.Replace("<REPO_URL>", $RepoUrl).Replace("](LICENSE)", "]($RepoUrl/blob/main/LICENSE)")
[IO.File]::WriteAllText((Join-Path $stage "README.md"), $readme, (New-Object Text.UTF8Encoding $false))

$manifest = [ordered]@{
    name           = $name
    version_number = $version
    website_url    = $RepoUrl
    description    = "A Codex bestiary with boss move lists and fight films, real damage numbers in descriptions, damage source tags, boss HP numbers, a combat log that explains every hit, pickup preview, cooldowns."
    dependencies   = @("BepInEx-BepInExPack_Skul-5.4.2100")
}
if ($manifest.description.Length -gt 250) { throw "Description longer than 250 characters" }
[IO.File]::WriteAllText((Join-Path $stage "manifest.json"), ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))

$zip = Join-Path $PSScriptRoot "out/$namespace-$name-$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Write-Host "Package: $zip"
