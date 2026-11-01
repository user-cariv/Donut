# Build the Donut installer and publish it as a GitHub release.
# Run from the Windows\Donut folder:  .\build-release.ps1 -Version 1.0.1
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [ValidateSet("win-x64", "win-arm64", "win-x86")]
    [string]$Runtime = "win-x64",
    [string]$Repo = "user-cariv/Donut",
    [string]$CertificateThumbprint = $env:DONUT_SIGNING_CERTIFICATE_THUMBPRINT
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "1/4 Closing running Donut..."
Get-Process Donut -ErrorAction SilentlyContinue | ForEach-Object {
    Stop-Process -Id $_.Id -Force
}

Write-Host "2/4 Setting installer version to $Version..."
(Get-Content installer.iss) `
    -replace '(#define MyAppVersion ")[^"]*(")', "`${1}$Version`$2" |
    Set-Content installer.iss -Encoding UTF8

Write-Host "3/4 Publishing app and building installer..."
dotnet publish Donut.csproj -c Release -r $Runtime --self-contained -o "publish\$Runtime"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$iscc = @(
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it from https://jrsoftware.org/isdl.php and run again." }

& $iscc installer.iss "/DRuntime=$Runtime"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

$asset = Join-Path $PSScriptRoot "installer\Install Donut Convert.exe"
if (-not (Test-Path $asset)) { throw "Installer not found at $asset" }

if ($CertificateThumbprint) {
    $signtool = Get-ChildItem `
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe",
        "$env:ProgramFiles\Windows Kits\10\bin\*\x64\signtool.exe" `
        -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $signtool) {
        throw "signtool.exe was not found. Install the Windows SDK or omit signing."
    }

    Write-Host "Signing installer..."
    & $signtool.FullName sign /sha1 $CertificateThumbprint /fd SHA256 /tr `
        "http://timestamp.digicert.com" /td SHA256 $asset
    if ($LASTEXITCODE -ne 0) { throw "Installer signing failed." }
} else {
    Write-Warning "Installer is unsigned. Smart App Control may block it; use a trusted code-signing certificate for public releases."
}

Write-Host "Installer built: $asset"

Write-Host "4/4 Creating GitHub release v$Version..."
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Host "GitHub CLI (gh) is not installed, so upload the installer manually:"
    Write-Host "https://github.com/$Repo/releases/new"
    exit 0
}

gh release create "v$Version-$Runtime" $asset --repo $Repo `
    --title "Donut Convert $Version" `
    --notes "Added PDF export, new logo, and the status line now shows where files are saved."
if ($LASTEXITCODE -ne 0) { throw "gh release create failed. Run 'gh auth login' and try again." }

Write-Host "Done: https://github.com/$Repo/releases/tag/v$Version"
