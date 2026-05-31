$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot "CodexUsageTray"
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq "AMD64") { "x64" } else { $env:PROCESSOR_ARCHITECTURE }
$targetFramework = "net10.0-windows10.0.26100.0"
$outputDir = Join-Path $projectDir "bin\$platform\Debug\$targetFramework\win-$($platform.ToLowerInvariant())"
$appxDir = Join-Path $outputDir "AppX"
$appxManifest = Join-Path $appxDir "AppxManifest.xml"

dotnet build -c Debug "-p:Platform=$platform" $projectDir | Out-Null

$runningProcesses = @(Get-Process -Name "CodexUsageTray" -ErrorAction SilentlyContinue)
if ($runningProcesses.Count -gt 0) {
    $runningProcesses | Stop-Process -Force
    $runningProcesses | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $appxDir | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $outputDir -File) {
    $destination = Join-Path $appxDir $file.Name
    try {
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force -ErrorAction Stop
    }
    catch {
        $destinationItem = Get-Item -LiteralPath $destination -ErrorAction SilentlyContinue
        if ($null -eq $destinationItem -or $destinationItem.Length -ne $file.Length) {
            throw
        }

        Write-Warning "Skipped locked unchanged AppX file: $($file.Name)"
    }
}
Copy-Item -LiteralPath (Join-Path $projectDir "Assets") -Destination $appxDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectDir "ProviderAssets") -Destination $appxDir -Recurse -Force

if (-not (Test-Path -LiteralPath $appxManifest)) {
    throw "Built AppX manifest not found: $appxManifest"
}

try {
    Add-AppxPackage -Register $appxManifest -ForceApplicationShutdown
}
catch {
    $existingPackages = @(Get-AppxPackage -Name "15FA8D5C-9A8A-444E-823D-C71E45662FCD" -ErrorAction SilentlyContinue)
    foreach ($package in $existingPackages) {
        Remove-AppxPackage -Package $package.PackageFullName
    }

    Add-AppxPackage -Register $appxManifest
}

Write-Host "Codex Usage Windows Widget registered."
Write-Host "Open Widgets with Win+W, choose Add widgets, then pin 'Codex Usage'."
