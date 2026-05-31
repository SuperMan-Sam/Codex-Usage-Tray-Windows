$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot "CodexUsageTray"
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq "AMD64") { "x64" } else { $env:PROCESSOR_ARCHITECTURE }
$targetFramework = "net10.0-windows10.0.26100.0"
$appxManifest = Join-Path $projectDir "bin\$platform\Debug\$targetFramework\win-$($platform.ToLowerInvariant())\AppX\AppxManifest.xml"

dotnet build -c Debug "-p:Platform=$platform" $projectDir | Out-Null

if (-not (Test-Path -LiteralPath $appxManifest)) {
    throw "Built AppX manifest not found: $appxManifest"
}

Get-Process -Name "CodexUsageTray" -ErrorAction SilentlyContinue | Stop-Process -Force

$existingPackages = @(Get-AppxPackage -Name "15FA8D5C-9A8A-444E-823D-C71E45662FCD" -ErrorAction SilentlyContinue)
foreach ($package in $existingPackages) {
    Remove-AppxPackage -Package $package.PackageFullName
}

Add-AppxPackage -Register $appxManifest

Write-Host "Codex Usage Windows Widget registered."
Write-Host "Open Widgets with Win+W, choose Add widgets, then pin 'Codex Usage'."
