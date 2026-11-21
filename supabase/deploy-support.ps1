$ErrorActionPreference = "Stop"

$projectRef = "qigzqfntaokxnsfcejiy"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$cli = Join-Path $repoRoot ".tools\supabase\supabase.exe"
$env:SUPABASE_HOME = Join-Path $repoRoot ".tools\supabase-home"
$env:SUPABASE_NO_UPDATE_NOTIFIER = "1"

if (-not (Test-Path -LiteralPath $cli)) {
  throw "Supabase CLI is missing. Re-download it before deploying."
}

function ConvertTo-TemporaryPlainText([Security.SecureString]$Secret) {
  $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secret)
  try {
    return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
  }
  finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
  }
}

$tokenSecure = Read-Host "Enter the project-scoped Supabase access token (input hidden)" -AsSecureString
$passwordSecure = Read-Host "Enter the Supabase database password (input hidden)" -AsSecureString
if ($tokenSecure.Length -eq 0 -or $passwordSecure.Length -eq 0) {
  throw "The access token and database password are required."
}
$env:SUPABASE_ACCESS_TOKEN = ConvertTo-TemporaryPlainText $tokenSecure
$env:SUPABASE_DB_PASSWORD = ConvertTo-TemporaryPlainText $passwordSecure

try {
  Push-Location $repoRoot
  try {
    Write-Host "Previewing the migration target. Confirm it names Donut Support / $projectRef."
    & $cli db push --dry-run "--remote=$projectRef"
    if ($LASTEXITCODE -ne 0) { throw "Supabase migration preview failed." }

    $approval = Read-Host "If that target is correct, type APPLY to update the private support table, attachment bucket, and rate limits"
    if ($approval -cne "APPLY") {
      Write-Host "Stopped without changing the Supabase project."
      return
    }

    & $cli db push "--remote=$projectRef" --yes
    if ($LASTEXITCODE -ne 0) { throw "Applying the support database migration failed." }

    $origins = "https://user-cariv.github.io,http://127.0.0.1:4173,http://localhost:4173"
    & $cli secrets set "--remote=$projectRef" "ALLOWED_ORIGINS=$origins"
    if ($LASTEXITCODE -ne 0) { throw "Setting the support function origin allow-list failed." }

    & $cli functions deploy submit-support "--remote=$projectRef" --no-verify-jwt --use-api
    if ($LASTEXITCODE -ne 0) { throw "Deploying the support function failed." }

    Write-Host "Support storage and the endpoint are deployed."
    Write-Host "Email notifications still need a verified support sender and inbox before users are asked to submit real requests."
  }
  finally {
    Pop-Location
  }
}
finally {
  $env:SUPABASE_ACCESS_TOKEN = $null
  $env:SUPABASE_DB_PASSWORD = $null
  $tokenSecure.Dispose()
  $passwordSecure.Dispose()
}
