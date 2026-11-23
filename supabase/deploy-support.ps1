$ErrorActionPreference = "Stop"

$projectRef = "qigzqfntaokxnsfcejiy"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$cliCandidates = @(
  (Join-Path $repoRoot ".tools\supabase\supabase.exe"),
  (Join-Path (Split-Path $repoRoot -Parent) "_deploy-icon-fix\.tools\supabase\supabase.exe")
)
$cli = $cliCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
$env:SUPABASE_HOME = Join-Path $repoRoot ".tools\supabase-home"
$env:SUPABASE_NO_UPDATE_NOTIFIER = "1"

if (-not $cli) {
  throw "Supabase CLI is missing. Install it at .tools\supabase\supabase.exe before deploying."
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

    foreach ($functionName in @("submit-support", "redeem-boost-license", "issue-boost-license")) {
      & $cli functions deploy $functionName "--remote=$projectRef" --no-verify-jwt --use-api
      if ($LASTEXITCODE -ne 0) { throw "Deploying the $functionName function failed." }
    }

    Write-Host "Database migrations and all Donut support and license functions are deployed."
    Write-Host "Support email requires SUPPORT_INBOX, SUPPORT_FROM, and SENDGRID_API_KEY (or RESEND_API_KEY) secrets."
    Write-Host "License activation requires DONUT_LICENSE_ISSUER_TOKEN, DONUT_LICENSE_SIGNING_PRIVATE_KEY, and DONUT_LICENSE_KEY_ID secrets."
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
