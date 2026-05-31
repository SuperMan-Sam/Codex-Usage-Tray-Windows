$ErrorActionPreference = "Stop"

$packageName = "15FA8D5C-9A8A-444E-823D-C71E45662FCD"
$appId = "App"
$scriptDir = $PSScriptRoot

$runningProcesses = @(Get-Process -Name "CodexUsageTray" -ErrorAction SilentlyContinue)
if ($runningProcesses.Count -gt 0) {
    $runningProcesses | Stop-Process -Force
    $runningProcesses | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

$existingPackages = @(Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue)
foreach ($package in $existingPackages) {
    Remove-AppxPackage -Package $package.PackageFullName -ErrorAction SilentlyContinue
}

$appManifest = Join-Path $scriptDir "app\AppxManifest.xml"
if (Test-Path -LiteralPath $appManifest) {
    Add-AppxPackage -Register $appManifest -ForceApplicationShutdown
}
else {
    $msix = Get-ChildItem -LiteralPath $scriptDir -Filter "CodexUsageTray_*.msix" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $msix) {
        throw "CodexUsageTray app layout or MSIX package was not found next to this installer."
    }

    $certificate = Get-ChildItem -LiteralPath $scriptDir -Filter "CodexUsageTray*.cer" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -ne $certificate) {
        Import-Certificate -FilePath $certificate.FullName -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
    }

    Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown
}

$registeredPackage = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $registeredPackage) {
    throw "CodexUsageTray was installed, but the registered app package was not found."
}

Start-Process "shell:AppsFolder\$($registeredPackage.PackageFamilyName)!$appId"

Write-Host "Codex Usage Tray installed and started."
Write-Host "For the Windows 11 Widget, press Win+W, choose Add widgets, then pin 'Codex Usage'."
