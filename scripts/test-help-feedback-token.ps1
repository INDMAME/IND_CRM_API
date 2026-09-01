<#
.SYNOPSIS
Verifies that CRM help feedback tokens are valid exactly once per API process.
#>
param(
    [string]$AssemblyPath
)

$ErrorActionPreference = 'Stop'
$scriptFilePath = $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($scriptFilePath)) {
    throw 'The feedback token test script path could not be resolved.'
}
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path (Split-Path -Parent $scriptFilePath) '..\bin\x86\Debug\IND_CRM_API.exe'
}

# The API is built for x86, so use the matching Windows PowerShell host.
if ([System.IntPtr]::Size -ne 4) {
    $x86PowerShell = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $x86PowerShell)) {
        throw "The x86 Windows PowerShell host was not found: $x86PowerShell"
    }
    & $x86PowerShell -NoProfile -ExecutionPolicy Bypass -File $scriptFilePath -AssemblyPath $AssemblyPath
    exit $LASTEXITCODE
}

$assembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$assemblyDirectory = Split-Path -Parent $assembly
$secretBytes = New-Object byte[] 48
$resolver = [System.ResolveEventHandler]{
    param($sender, $eventArgs)
    $name = ([System.Reflection.AssemblyName]$eventArgs.Name).Name + '.dll'
    $candidate = Join-Path $assemblyDirectory $name
    if (Test-Path -LiteralPath $candidate) {
        return [System.Reflection.Assembly]::LoadFrom($candidate)
    }
    return $null
}
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)

try {
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($secretBytes)
    }
    finally {
        $rng.Dispose()
    }
    [void][System.Reflection.Assembly]::LoadFrom($assembly)
    $serviceType = [IND_CRM_API.Services.HelpFeedbackTokenService]
    $constructor = $serviceType.GetConstructor(
        [System.Reflection.BindingFlags]'Instance,NonPublic',
        $null,
        [Type[]]@([string], [int]),
        $null)
    if ($null -eq $constructor) {
        throw 'The isolated feedback token test constructor was not found.'
    }
    $secret = [System.Convert]::ToBase64String($secretBytes)
    $service = $constructor.Invoke([object[]]@($secret, 60))
    $interactionId = [guid]::NewGuid().ToString('D')
    $userKey = 'feedback-token-directed-test'
    $token = $service.Create($interactionId, $userKey)

    $firstPayload = $null
    $firstConsume = $service.TryConsume($token, $userKey, [ref]$firstPayload)
    $replayPayload = $null
    $replayConsume = $service.TryConsume($token, $userKey, [ref]$replayPayload)
    $malformedPayload = $null
    $malformedConsume = $service.TryConsume('not-a-valid-feedback-token', $userKey, [ref]$malformedPayload)

    $createPassed = -not [string]::IsNullOrWhiteSpace($token)
    $firstPassed = $firstConsume -and $firstPayload -and $firstPayload.InteractionId -eq $interactionId
    $replayRejected = -not $replayConsume -and $null -eq $replayPayload
    $malformedRejected = -not $malformedConsume -and $null -eq $malformedPayload

    Write-Host ('FeedbackTokenCreate={0} FirstConsume={1} ReplayRejected={2} MalformedRejected={3}' -f `
        $(if ($createPassed) { 'Passed' } else { 'Failed' }),
        $(if ($firstPassed) { 'Passed' } else { 'Failed' }),
        $(if ($replayRejected) { 'Passed' } else { 'Failed' }),
        $(if ($malformedRejected) { 'Passed' } else { 'Failed' }))

    if (-not $createPassed -or -not $firstPassed -or -not $replayRejected -or -not $malformedRejected) {
        exit 1
    }
}
finally {
    [Array]::Clear($secretBytes, 0, $secretBytes.Length)
    [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
