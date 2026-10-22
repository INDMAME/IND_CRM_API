param(
    [string]$AssemblyPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path $PSScriptRoot '..\bin\x86\Release\IND_CRM_API.exe'
}
$AssemblyPath = [IO.Path]::GetFullPath($AssemblyPath)

# Load the API in a separate x86 process without starting its host or calling providers.
if ([IntPtr]::Size -ne 4) {
    $x86PowerShell = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
    & $x86PowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -AssemblyPath $AssemblyPath
    exit $LASTEXITCODE
}

# Fail immediately when the observed classification or public error contract differs.
function Assert-Equal {
    param($Actual, $Expected, [string]$Message)
    if ($Actual -ne $Expected) {
        throw "$Message Expected=[$Expected] Actual=[$Actual]"
    }
}

# Unwrap reflection errors so assertions inspect the application exception itself.
function Invoke-QuotaClassifier {
    param([AllowNull()][string]$Body)
    try {
        $quotaMethod.Invoke($null, [object[]]@($Body)) | Out-Null
        return $null
    }
    catch {
        $failure = $_.Exception
        while ($null -ne $failure.InnerException) { $failure = $failure.InnerException }
        return $failure
    }
}

Push-Location (Split-Path -Parent $AssemblyPath)
try {
    Add-Type -AssemblyName System.Net.Http
    $assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
    $handling = $assembly.GetType('IND_CRM_API.Services.IND_OpenAiErrorHandling', $true)
    $mapper = $assembly.GetType('IND_CRM_API.Services.IND_KnownExceptionMapper', $true).GetMethod('TryMap')
    $rateMethod = $handling.GetMethod('IsRateLimit')
    $quotaMethod = $handling.GetMethod('ThrowIfQuotaExceeded')
    $retryMethod = $handling.GetMethod('GetRetryAfterSeconds')
    $quotaBodies = @(
        '{"error":{"type":"insufficient_quota","code":"credit_balance_exhausted","message":"You have no credits remaining."}}',
        '{"error":{"type":"insufficient_quota","code":null}}',
        '{"error":{"code":"insufficient_quota"}}',
        '{"error":{"code":"credit_balance_exhausted"}}',
        '{"error":{"code":"billing_hard_limit_reached"}}',
        '{"error":{"code":"organization_spend_limit_exceeded"}}',
        '{"error":{"code":"project_spend_limit_exceeded"}}',
        '{"error":{"code":"organization_usage_limit_exceeded"}}',
        '{"error":{"type":" INSUFFICIENT_QUOTA ","code":" CREDIT_BALANCE_EXHAUSTED "}}'
    )

    foreach ($body in $quotaBodies) {
        Assert-Equal ($rateMethod.Invoke($null, [object[]]@(([Enum]::ToObject([Net.HttpStatusCode], 429)), $body))) $false 'Quota must not be classified as throttling.'
        $failure = Invoke-QuotaClassifier $body
        Assert-Equal $failure.GetType().Name 'IND_ExternalServiceException' 'Quota must use the existing dependency exception.'
        Assert-Equal $failure.ErrorCode 'AI_QUOTA_EXCEEDED' 'Quota error code must be specific.'
        $mapped = [object[]]@($failure, [Net.HttpStatusCode]::InternalServerError, $null, $null, $null)
        Assert-Equal ($mapper.Invoke($null, $mapped)) $true 'The common mapper must handle quota failures.'
        Assert-Equal ([int]$mapped[1]) 503 'Quota must report service unavailable.'
        Assert-Equal $mapped[3] 'AI_QUOTA_EXCEEDED' 'The mapper must preserve the quota code.'
        Assert-Equal $mapped[4] $null 'Quota must not suggest a retry delay.'
        Assert-Equal ($mapped[2] -match 'Contacte con el administrador') $true 'Quota must explain the required action.'
        Assert-Equal ($failure.ProviderSummary -match 'You have no credits') $false 'Provider summaries must not copy message text.'
    }
    Assert-Equal ((Invoke-QuotaClassifier $quotaBodies[0]).UserMessage -match 'saldo agotado') $true 'An exhausted balance must have a precise message.'

    $otherBodies = @(
        '{"error":{"type":"rate_limit_error","code":"rate_limit_exceeded"}}',
        '{"error":{"type":"rate_limit_error","code":"slow_down"}}',
        '{"error":{"message":"Text containing insufficient_quota is not a billing code."}}',
        '{"error":{"type":{},"code":[]}}',
        '{"error":"invalid"}', '[]', 'null', 'broken json', '', $null
    )
    foreach ($body in $otherBodies) {
        Assert-Equal (Invoke-QuotaClassifier $body) $null 'Unknown responses must not be invented billing errors.'
        Assert-Equal ($rateMethod.Invoke($null, [object[]]@(([Enum]::ToObject([Net.HttpStatusCode], 429)), $body))) $true 'Unknown 429 responses retain existing throttling behavior.'
    }
    Assert-Equal ($rateMethod.Invoke($null, [object[]]@([Net.HttpStatusCode]401, '{"error":{"code":"invalid_api_key"}}'))) $false 'Authentication failures must remain separate.'

    $response = New-Object Net.Http.HttpResponseMessage (([Enum]::ToObject([Net.HttpStatusCode], 429)))
    try {
        Assert-Equal ($retryMethod.Invoke($null, [object[]]@($response.PSObject.BaseObject))) $null 'Missing Retry-After must stay absent.'
        $response.Headers.TryAddWithoutValidation('Retry-After', '37') | Out-Null
        $retry = $retryMethod.Invoke($null, [object[]]@($response.PSObject.BaseObject))
        Assert-Equal $retry 37 'Retry-After seconds must be preserved.'
        $rateType = $assembly.GetType('IND_CRM_API.Services.IND_OpenAiRateLimitException', $true)
        $rateFailure = [Activator]::CreateInstance($rateType, [object[]]@('Temporary rate limit', [Nullable[int]]$retry, 'rate_limit_exceeded'))
        $mapped = [object[]]@($rateFailure, [Net.HttpStatusCode]::InternalServerError, $null, $null, $null)
        Assert-Equal ($mapper.Invoke($null, $mapped)) $true 'The common mapper must still handle throttling.'
        Assert-Equal ([int]$mapped[1]) 429 'Throttling must remain HTTP 429.'
        Assert-Equal $mapped[3] 'AI_RATE_LIMIT_EXCEEDED' 'Throttling must retain its existing code.'
        Assert-Equal $mapped[4] 37 'The mapper must preserve the retry delay.'
    }
    finally { $response.Dispose() }

    Write-Output 'PASS: 9 quota variants, 10 non-quota bodies, authentication, public mapping and Retry-After.'
}
finally { Pop-Location }
