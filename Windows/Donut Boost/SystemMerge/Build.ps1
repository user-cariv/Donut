param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$work = Join-Path ([IO.Path]::GetTempPath()) ('donut-merge-' + [guid]::NewGuid())
New-Item -ItemType Directory -Force $work, $destination | Out-Null

$archive = Join-Path $work 'source.zip'
curl.exe -fsSL --max-time 60 'https://codeload.github.com/net-merge/net-merge/zip/refs/tags/v0.3.7' -o $archive
if ($LASTEXITCODE -ne 0) { throw 'System Merge source download failed.' }
if ((Get-FileHash $archive).Hash -ne 'D837B54075CEEA5565996EFF0CBE6CE5072A11299F0E105851DD881C05CB3DBA') {
    throw 'System Merge upstream source checksum mismatch.'
}
Expand-Archive $archive $work
$source = Join-Path $work 'net-merge-0.3.7'
$command = Join-Path $source 'cmd/donut-system-merge'
New-Item -ItemType Directory $command | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'main_windows.go') $command

# Give Donut its own tunnel identity. Keep changes to physical adapter settings in memory.
$tun = Join-Path $source 'internal/tun/tun.go'
$windowsTun = Join-Path $source 'internal/tun/tun_windows.go'
$nics = Join-Path $source 'internal/nics/nics.go'
[IO.File]::WriteAllText($tun, [IO.File]::ReadAllText($tun).Replace('tunName = "net-merge"', 'tunName = "Donut Boost Merge"'))
[IO.File]::WriteAllText($nics, [IO.File]::ReadAllText($nics).Replace('wintun|net-merge|', 'wintun|net-merge|donut boost merge|'))
$platform = [IO.File]::ReadAllText($windowsTun).
    Replace('wun.WintunTunnelType = "net-merge"', 'wun.WintunTunnelType = "Donut Boost Merge"').
    Replace('3f0d5a7e-9b1c-4e52-8a47-1c6b2d9e5f30', '0c03e9f2-8af7-47b9-b408-89c7994c925a').
    Replace('staleHook = restoreStrongHost', '').
    Replace('"os"', '').
    Replace('var strongHostFile = filepath.Join(os.TempDir(), "net-merge-weakhost.txt")', '').
    Replace('os.WriteFile(strongHostFile, []byte(strings.Join(strongHost.restore, "\n")), 0o600)', '').
    Replace('os.Remove(strongHostFile)', '').
    Replace('exec.Command("powershell",', 'exec.Command(filepath.Join(systemDirectory(), "WindowsPowerShell", "v1.0", "powershell.exe"),').
    Replace('exec.Command(name, args...)', 'exec.Command(filepath.Join(systemDirectory(), name+".exe"), args...)')
$platform = [regex]::Replace($platform, '(?s)func restoreStrongHost\(\) \{.*?\n\}', '')
$platform += @"

func systemDirectory() string {
    dir, err := windows.GetSystemDirectory()
    if err != nil {
        panic(err)
    }
    return dir
}
"@
[IO.File]::WriteAllText($windowsTun, $platform)

Push-Location $source
try {
    go test ./internal/engine ./internal/nics ./internal/tun
    if ($LASTEXITCODE -ne 0) { throw 'System Merge engine tests failed.' }
    go build -trimpath -o (Join-Path $destination 'DonutBoost.SystemMerge.exe') ./cmd/donut-system-merge
    if ($LASTEXITCODE -ne 0) { throw 'System Merge engine build failed.' }
    & (Join-Path $destination 'DonutBoost.SystemMerge.exe') --check
    if ($LASTEXITCODE -ne 0) { throw 'System Merge executable check failed.' }
    $driverArchive = Join-Path $work 'wintun.zip'
    curl.exe -fsSL --max-time 60 'https://www.wintun.net/builds/wintun-0.14.1.zip' -o $driverArchive
    if ($LASTEXITCODE -ne 0 -or (Get-FileHash $driverArchive).Hash -ne '07c256185d6ee3652e09fa55c0b673e2624b565e02c4b9091c79ca7d2f24ef51') {
        throw 'Wintun download failed verification.'
    }
    Expand-Archive $driverArchive $work
    $dll = Join-Path $work 'wintun/bin/amd64/wintun.dll'
    $signature = Get-AuthenticodeSignature $dll
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'WireGuard LLC') {
        throw "Wintun signature check failed: $($signature.Status)"
    }
    Copy-Item $dll $destination
    Copy-Item (Join-Path $work 'wintun/LICENSE.txt') (Join-Path $destination 'wintun-LICENSE.txt')
    Copy-Item LICENSE (Join-Path $destination 'SystemMerge-LICENSE.txt')
    Copy-Item THIRD-PARTY-NOTICES.md (Join-Path $destination 'SystemMerge-THIRD-PARTY-NOTICES.md')
} finally {
    Pop-Location
}
