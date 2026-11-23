param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("issue", "revoke")]
    [string]$Action,

    [int]$Tier = 0
)

$ErrorActionPreference = "Stop"
$endpoint = "https://qigzqfntaokxnsfcejiy.supabase.co/functions/v1/issue-boost-license"
$secureToken = Read-Host "Enter the Donut license issuer token" -AsSecureString
$tokenPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)

try {
    if ($Action -eq "issue" -and $Tier -notin 1..4) {
        throw "Tier must be 1, 2, 3, or 4."
    }
    $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($tokenPtr)
    $payload = @{ action = $Action; tier = $Tier }
    if ($Action -eq "revoke") {
        $payload.code = Read-Host "Enter the activation code to revoke"
    }

    $response = Invoke-RestMethod -Method Post -Uri $endpoint `
        -Headers @{ "x-donut-issuer-token" = $token } `
        -ContentType "application/json" `
        -Body ($payload | ConvertTo-Json -Compress)

    if ($Action -eq "issue") {
        Write-Host "Activation code (shown once): $($response.code)"
    } elseif ($response.revoked) {
        Write-Host "Activation code revoked. It will be rejected on the next online check."
    } else {
        Write-Host "No matching activation code was found."
    }
} catch {
    Write-Error "The licensing admin request failed. Check the endpoint, secret, and network, then retry."
    exit 1
} finally {
    if ($tokenPtr -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($tokenPtr)
    }
    $token = $null
    $secureToken.Dispose()
}
