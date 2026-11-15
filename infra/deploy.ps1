#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes and deploys the BSA troop finance tracker to Azure App Service.

.DESCRIPTION
    Wraps the whole "publish -> zip -> deploy" sequence, avoiding two gotchas discovered while
    building the sibling meeting sign-in app:
      - Publishing/zipping inside a OneDrive-synced project folder can hit transient file locks
        (OneDrive syncing mid-write). This script builds and zips in a temp folder instead.
      - Windows PowerShell 5.1's built-in System.IO.Compression.FileSystem (old .NET Framework)
        writes ZIP entries with backslash path separators for nested folders, which Azure's
        Linux-based Kudu deployment engine can't extract (rsync errors: "failed to stat ... :
        Invalid argument"). This script uses tar.exe (built into Windows 10/11) instead, which
        always writes spec-correct forward slashes.

.PARAMETER ResourceGroup
    Azure resource group the web app lives in. Defaults to infra/local/deploy.json's resourceGroup.

.PARAMETER WebAppName
    Azure Web App (site) name. Defaults to infra/local/deploy.json's webAppName.

.EXAMPLE
    .\infra\deploy.ps1
#>
param(
    [string]$ResourceGroup,
    [string]$WebAppName
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

# This deployment's own names live outside git (see infra/DEPLOY.md step 1).
$localSettings = Join-Path $PSScriptRoot "local\deploy.json"
if ((-not $ResourceGroup -or -not $WebAppName) -and (Test-Path $localSettings)) {
    $saved = Get-Content $localSettings -Raw | ConvertFrom-Json
    if (-not $ResourceGroup) { $ResourceGroup = $saved.resourceGroup }
    if (-not $WebAppName) { $WebAppName = $saved.webAppName }
}
if (-not $ResourceGroup -or -not $WebAppName) {
    throw "Give -ResourceGroup and -WebAppName, or save them in $localSettings as { `"resourceGroup`": ..., `"webAppName`": ... }."
}
$projectDir = Join-Path $repoRoot "src\BsaTroopFinances"
$publishDir = Join-Path $env:TEMP "troop-finances-publish"
$zipPath = Join-Path $env:TEMP "troop-finances-publish.zip"

Write-Host "==> Publishing (Release) from $projectDir" -ForegroundColor Cyan
Push-Location $projectDir
try {
    Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

    # Published for the App Service's own platform (Linux x64, see main.bicep) rather than for
    # every platform: the receipt-thumbnail libraries (PDFium, SkiaSharp) ship native code per
    # platform, and a portable publish carries all of them -- about 550 MB the site never loads.
    dotnet publish -c Release -r linux-x64 --self-contained false -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

Write-Host "==> Zipping with tar.exe (avoids PowerShell 5.1's backslash-path ZipFile bug)" -ForegroundColor Cyan
Push-Location $publishDir
try {
    tar -a -c -f $zipPath .
    if ($LASTEXITCODE -ne 0) {
        throw "tar failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

Write-Host "==> Deploying to $WebAppName (resource group $ResourceGroup)" -ForegroundColor Cyan
az webapp deploy --resource-group $ResourceGroup --name $WebAppName --src-path $zipPath --type zip --clean true
if ($LASTEXITCODE -ne 0) {
    throw "az webapp deploy failed with exit code $LASTEXITCODE"
}

Write-Host "==> Done. https://$WebAppName.azurewebsites.net/" -ForegroundColor Green
