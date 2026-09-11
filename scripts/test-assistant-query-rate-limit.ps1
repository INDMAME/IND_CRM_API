[CmdletBinding()]
param(
    [string]$AssemblyPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path $PSScriptRoot '..\bin\x86\Debug\IND_CRM_API.exe'
}

# Relaunch in 32-bit Windows PowerShell because the API is built for x86.
if ([Environment]::Is64BitProcess) {
    $powerShell32 = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $powerShell32 -PathType Leaf)) {
        throw "32-bit Windows PowerShell was not found at '$powerShell32'."
    }

    & $powerShell32 -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -AssemblyPath $AssemblyPath
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    exit 0
}

$resolvedAssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$binaryDirectory = Split-Path -Parent $resolvedAssemblyPath
Set-Location -LiteralPath $binaryDirectory
[Environment]::CurrentDirectory = $binaryDirectory

$assembly = [Reflection.Assembly]::LoadFrom($resolvedAssemblyPath)
$handlerType = $assembly.GetType(
    'IND_CRM_API.App_Start.IND_OpenAiRateLimitHandler',
    $true
)
$errorCodesType = $assembly.GetType('IND_CRM_API.Models.Responses.IndErrorCodes', $true)
$publicStatic = [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static
$nonPublicStatic = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$nonPublicInstance = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Instance

$localQuotaErrorCode = $errorCodesType.GetField(
    'AssistantQueryRateLimitExceeded',
    $publicStatic
).GetRawConstantValue()
if ($localQuotaErrorCode -ne 'ASSISTANT_QUERY_RATE_LIMIT_EXCEEDED') {
    throw "Unexpected local quota error code '$localQuotaErrorCode'."
}

$handler = [Activator]::CreateInstance($handlerType, [object[]]@($null))
$tryConsumeRequest = $handlerType.GetMethod('TryConsumeRequest', $nonPublicInstance)
$buildLimitMessage = $handlerType.GetMethod('BuildAssistantQueryRateLimitMessage', $nonPublicStatic)
$expectedMessage = 'Se ha superado el l' + [char]0x00ED + 'mite de consultas. Por favor, vuelva a intentarlo dentro de 15 minutos.'
$actualMessage = [string]$buildLimitMessage.Invoke($null, [object[]]@(900))
if ($actualMessage -ne $expectedMessage) {
    throw "Unexpected quota message '$actualMessage'."
}

# Exercise both endpoint counters independently: requests 1-30 pass and 31 is rejected.
foreach ($fieldName in @('HelpAskLimit', 'ExpenseSheetAskLimit')) {
    $endpoint = $handlerType.GetField($fieldName, $nonPublicStatic).GetValue($null)
    $userKey = 'rate-limit-test-' + [Guid]::NewGuid().ToString('N')
    $acceptedRequests = 0
    $retryAfterSeconds = 0
    $effectiveMaxRequests = 0

    foreach ($requestNumber in 1..31) {
        $arguments = [object[]]@($userKey, $endpoint, 1, 0, 0)
        $isAccepted = [bool]$tryConsumeRequest.Invoke($handler, $arguments)
        if ($isAccepted) {
            $acceptedRequests++
            continue
        }

        $retryAfterSeconds = [int]$arguments[3]
        $effectiveMaxRequests = [int]$arguments[4]
    }

    if ($acceptedRequests -ne 30 -or $effectiveMaxRequests -ne 30 -or $retryAfterSeconds -ne 900) {
        throw "$fieldName failed: accepted=$acceptedRequests effectiveMax=$effectiveMaxRequests retryAfter=$retryAfterSeconds"
    }

    Write-Output "$fieldName accepted=30 rejected=31 effectiveMax=30 retryAfter=900"
}

Write-Output 'Assistant query rate-limit checks passed.'
