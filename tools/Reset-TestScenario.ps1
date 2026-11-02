<#
.SYNOPSIS
  Resets the local demo repository and library to a known end-to-end test scenario.

.DESCRIPTION
  1. Backs up the current repository, library and settings to C:\FISDemoTest\backup-<timestamp>.
  2. Rebuilds the repository: the four bundled demos at 1.0.0, assigned to the current Windows user.
  3. Rebuilds the library: Designed for Digital, The Digital Advantage and Confident Delivery installed at 1.0.0.
  4. Releases 1.1.0 of Designed for Digital and The Digital Advantage, so Studio offers two updates on start.
     Data Lab stays published but not installed, so it appears in Downloads.
  5. Stages C:\FISDemoTest\TemplateDemo (Template + shared brand kit) for the Build Package test.

  Run it with Studio closed. See documentation\Guides\test-plan.md for the walkthrough.
#>
param(
    [string]$Repository = 'C:\FISDemoRepository',
    [string]$Library = (Join-Path $env:LOCALAPPDATA 'FISPresents\data'),
    [string]$TestRoot = 'C:\FISDemoTest'
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$settings = Join-Path $env:LOCALAPPDATA 'FISPresents\Settings'
# Release zip layout: Tools\Reset-TestScenario.ps1 beside Tools\DemoRepositoryTool\DemoRepositoryTool.exe (samples bundled with it).
$releaseTool = Join-Path $PSScriptRoot 'DemoRepositoryTool\DemoRepositoryTool.exe'
if (Test-Path $releaseTool) {
    $tool = $releaseTool
    $samples = Join-Path $PSScriptRoot 'DemoRepositoryTool\samples\Demos'
} else {
    $tool = Join-Path $root 'tools\DemoRepositoryTool\bin\Debug\net10.0-windows\DemoRepositoryTool.exe'
    $samples = Join-Path $root 'samples\Demos'
}

if (Get-Process FisPresentationPlatform -ErrorAction SilentlyContinue) { throw 'Close FIS Presentation Studio first.' }
if (-not (Test-Path $tool)) {
    Write-Host 'Building the repository tool...'
    dotnet build (Join-Path $root 'tools\DemoRepositoryTool') --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Tool build failed.' }
}

function Invoke-Tool { & $tool @args; if ($LASTEXITCODE -ne 0) { throw "DemoRepositoryTool $($args[0]) failed ($LASTEXITCODE)." } }

$backup = Join-Path $TestRoot ('backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory $backup -Force | Out-Null
foreach ($pair in @(@($Repository, 'repository'), @($Library, 'library'), @($settings, 'settings'))) {
    if (Test-Path $pair[0]) { Copy-Item $pair[0] (Join-Path $backup $pair[1]) -Recurse }
}
Write-Host "Backed up to $backup"

Remove-Item $Repository -Recurse -Force -ErrorAction SilentlyContinue
# Keep the library's usage queue (it belongs to the user); reset everything else.
if (Test-Path $Library) { Get-ChildItem $Library -Force | Where-Object Name -ne 'usage' | Remove-Item -Recurse -Force }

$user = "$env:USERDOMAIN\$env:USERNAME"
Invoke-Tool init $Repository $user
Invoke-Tool assign $Repository $user 'Instant Payments Hub' 'Digital Account Origination' 'Trade Lifecycle Manager'
foreach ($id in 'fis-designed-for-digital', 'fis-digital-advantage', 'fis-confident-delivery') { Invoke-Tool install $Repository $Library $id 1.0.0 }
Invoke-Tool release $Repository DesignedForDigital 1.1.0 'Test update: refreshed journey copy and new release notes.'
Invoke-Tool release $Repository DigitalAdvantage 1.1.0 'Test update: updated impact figures.'

$template = Join-Path $TestRoot 'TemplateDemo'
Remove-Item $template -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $samples 'Template') $template -Recurse
Copy-Item (Join-Path $samples 'shared') (Join-Path $template 'shared') -Recurse

Write-Host ''
Invoke-Tool list $Repository
Write-Host ''
Write-Host 'Ready. Installed: 3 demos at 1.0.0. Pending updates: 2. Available to download: Data Lab.'
Write-Host "Build Package test folder: $template"
Write-Host "To restore your previous state, close Studio and copy the folders back from $backup."
