<#
.SYNOPSIS
    Builds a distributable FIS Presentation Studio release (self-contained, win-x64) and zips it.
.EXAMPLE
    .\tools\Build-Release.ps1                 # version 1.0.0, tests run first
    .\tools\Build-Release.ps1 -Version 1.0.1 -SkipTests
#>
param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64",
    # Written to App\deployment.json: the first-run repository for every user (a UNC share in production).
    [string]$RepositoryPath = "C:\FISDemoRepository",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$name = "FISPresents-$Version-$Runtime"
$dist = Join-Path $root "dist"
$out = Join-Path $dist $name
$zip = Join-Path $dist "$name.zip"

function Invoke-Step([string]$label, [scriptblock]$action) {
    Write-Host "==> $label" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) { throw "$label failed (exit $LASTEXITCODE)." }
}

if (-not $SkipTests) {
    Invoke-Step "Running tests" { dotnet test (Join-Path $root "tests\FisPresentationPlatform.Tests") --nologo -v q }
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

$common = @("-c", "Release", "-r", $Runtime, "--self-contained", "true", "-p:Version=$Version", "--nologo", "-v", "q")

Invoke-Step "Publishing FIS Presentation Studio" {
    dotnet publish (Join-Path $root "FisPresentationPlatform.csproj") @common -o (Join-Path $out "App")
}
Invoke-Step "Publishing DemoRepositoryTool" {
    dotnet publish (Join-Path $root "tools\DemoRepositoryTool\DemoRepositoryTool.csproj") @common -o (Join-Path $out "Tools\DemoRepositoryTool")
}

Write-Host "==> Writing deployment.json" -ForegroundColor Cyan
$deployment = [ordered]@{
    demoRepositoryPath       = $RepositoryPath
    checkOnLaunch            = $true
    trackingEnabledByDefault = $true
    uploadUsage              = $true
}
Set-Content (Join-Path $out "App\deployment.json") ($deployment | ConvertTo-Json) -Encoding UTF8

Write-Host "==> Copying documentation and scripts" -ForegroundColor Cyan
$docs = Join-Path $out "Documentation"
foreach ($folder in "Guides", "Configuration", "Executive", "Settings", "DemoLibrary") {
    Copy-Item (Join-Path $root "documentation\$folder") (Join-Path $docs $folder) -Recurse
}
New-Item (Join-Path $docs "Review") -ItemType Directory | Out-Null
Copy-Item (Join-Path $root "documentation\Review\mvp-readiness.md") (Join-Path $docs "Review")
Copy-Item (Join-Path $root "tools\Reset-TestScenario.ps1") (Join-Path $out "Tools")

$readme = @"
FIS Presentation Studio $Version ($Runtime)
===========================================

Install:  see Documentation\Guides\deployment-guide.md (prerequisites, repository share, first-run defaults)
Run:      App\FisPresentationPlatform.exe   (no .NET install needed; requires the Microsoft Edge WebView2 Runtime, present on Windows 10/11)
Defaults: App\deployment.json sets the demo repository for users who have not saved settings yet
Guides:   Documentation\Guides\user-guide.md, authoring-guide.md, admin-guide.md, test-plan.md
Admin:    Tools\DemoRepositoryTool\DemoRepositoryTool.exe help
Repo:     defaults to C:\FISDemoRepository (change in Settings > Repository)
Data:     %LOCALAPPDATA%\FISPresents (library, settings, logs, usage)
"@
Set-Content (Join-Path $out "README.txt") $readme -Encoding UTF8

Write-Host "==> Zipping" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip

$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Release ready: $zip ($sizeMb MB)" -ForegroundColor Green
