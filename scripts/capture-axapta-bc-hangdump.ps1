[CmdletBinding()]
param(
    [string]$ServiceName = "Navision Axapta Business Connector",
    [string]$ProcessUser = "INSERTEC\API_AXUSER",
    [int]$ExpectedSessionId = 0,
    [string]$DumpDirectory = "C:\Dumps\AxaptaBC",
    [string]$ProcDumpPath,
    [ValidateRange(1, 10)]
    [int]$DumpCount = 2,
    [ValidateRange(0, 3600)]
    [int]$DelaySecondsBetweenDumps = 30,
    [switch]$SkipRestart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    # Administrative rights are required to inspect the COM host and restart the service.
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)

    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-ProcDumpPath {
    param(
        [AllowEmptyString()]
        [string]$RequestedPath
    )

    # Search explicit and common locations so operators do not need to edit the script.
    $candidates = New-Object System.Collections.Generic.List[string]

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates.Add($RequestedPath)
    }

    foreach ($scope in @("Process", "User", "Machine")) {
        $envValue = [Environment]::GetEnvironmentVariable("PROCDUMP_PATH", $scope)

        if (-not [string]::IsNullOrWhiteSpace($envValue)) {
            $candidates.Add($envValue)
        }
    }

    foreach ($commandName in @("procdump64.exe", "procdump.exe")) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue | Select-Object -First 1

        if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
            $candidates.Add($command.Source)
        }
    }

    foreach ($candidate in @(
        "C:\Tools\Sysinternals\procdump64.exe",
        "C:\Tools\Sysinternals\procdump.exe",
        "C:\Sysinternals\procdump64.exe",
        "C:\Sysinternals\procdump.exe",
        "C:\Program Files\Sysinternals\procdump64.exe",
        "C:\Program Files\Sysinternals\procdump.exe"
    )) {
        $candidates.Add($candidate)
    }

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "ProcDump was not found. Install Sysinternals ProcDump or pass -ProcDumpPath."
}

function Get-ConnectorServiceController {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RequestedServiceName
    )

    # Resolve both service name and display name because COM connector installations vary.
    $service = Get-Service -Name $RequestedServiceName -ErrorAction SilentlyContinue | Select-Object -First 1

    if ($null -eq $service) {
        $service = Get-Service | Where-Object { $_.DisplayName -eq $RequestedServiceName } | Select-Object -First 1
    }

    if ($null -eq $service) {
        throw ("The service '{0}' was not found on this machine." -f $RequestedServiceName)
    }

    return $service
}

function Get-DllHostCandidates {
    param(
        [Parameter(Mandatory = $true)]
        [string]$UserName
    )

    # The Business Connector COM+ host runs under dllhost.exe, so we inspect ownership to find it.
    $processes = Get-CimInstance Win32_Process -Filter "Name = 'dllhost.exe'"

    $results = foreach ($process in $processes) {
        $owner = Invoke-CimMethod -InputObject $process -MethodName GetOwner
        $resolvedUser = ""

        if ($owner.ReturnValue -eq 0 -and -not [string]::IsNullOrWhiteSpace($owner.User)) {
            $resolvedUser = ("{0}\{1}" -f $owner.Domain, $owner.User)
        }

        [pscustomobject]@{
            ProcessId   = [int]$process.ProcessId
            SessionId   = [int]$process.SessionId
            User        = $resolvedUser
            CommandLine = $process.CommandLine
        }
    }

    return @($results | Where-Object { $_.User -eq $UserName })
}

function Resolve-TargetProcess {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Candidates,
        [int]$PreferredSessionId = 0
    )

    if ($Candidates.Count -eq 0) {
        throw "No dllhost.exe process matched the configured Business Connector identity."
    }

    $preferredCandidates = @($Candidates | Where-Object { $_.SessionId -eq $PreferredSessionId } | Sort-Object ProcessId)

    if ($preferredCandidates.Count -gt 1) {
        Write-Warning ("Multiple dllhost.exe candidates were found in session {0}. The oldest PID will be used." -f $PreferredSessionId)
    }

    if ($preferredCandidates.Count -ge 1) {
        return $preferredCandidates[0]
    }

    if ($Candidates.Count -gt 1) {
        Write-Warning "Multiple dllhost.exe candidates were found. No process matched the preferred session id, so the lowest PID will be used."
    }

    return @($Candidates | Sort-Object SessionId, ProcessId)[0]
}

function New-DumpFilePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseDirectory,
        [Parameter(Mandatory = $true)]
        [int]$ProcessId,
        [Parameter(Mandatory = $true)]
        [int]$SequenceNumber
    )

    # Timestamped dump names keep repeated incidents easy to compare.
    New-Item -ItemType Directory -Path $BaseDirectory -Force | Out-Null
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

    return (Join-Path $BaseDirectory ("AxaptaBC-dllhost-{0}-{1}-part{2:D2}.dmp" -f $ProcessId, $timestamp, $SequenceNumber))
}

function Invoke-ProcDumpCapture {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,
        [Parameter(Mandatory = $true)]
        [int]$ProcessId,
        [Parameter(Mandatory = $true)]
        [string]$DumpPath
    )

    # Full dumps are preferred for hang analysis because they keep the blocked stacks.
    & $ExecutablePath -accepteula -ma $ProcessId $DumpPath
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        throw ("ProcDump failed with exit code {0}." -f $exitCode)
    }

    if (-not (Test-Path -LiteralPath $DumpPath -PathType Leaf)) {
        throw "ProcDump reported success, but the dump file was not created."
    }
}

function Test-TargetProcessAlive {
    param(
        [Parameter(Mandatory = $true)]
        [int]$ProcessId
    )

    # Recheck the process before each dump in case the connector crashed or recycled itself.
    return $null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)
}

function Invoke-DumpSequence {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,
        [Parameter(Mandatory = $true)]
        [int]$ProcessId,
        [Parameter(Mandatory = $true)]
        [string]$BaseDirectory,
        [Parameter(Mandatory = $true)]
        [int]$TotalDumps,
        [Parameter(Mandatory = $true)]
        [int]$DelaySeconds
    )

    # Taking two spaced dumps makes it easier to separate a live wait from a frozen state.
    $dumpPaths = New-Object System.Collections.Generic.List[string]

    for ($index = 1; $index -le $TotalDumps; $index++) {
        if (-not (Test-TargetProcessAlive -ProcessId $ProcessId)) {
            throw ("The target process {0} is no longer running before dump #{1}." -f $ProcessId, $index)
        }

        $dumpPath = New-DumpFilePath -BaseDirectory $BaseDirectory -ProcessId $ProcessId -SequenceNumber $index
        Write-Host ("Capturing dump #{0} of {1}: {2}" -f $index, $TotalDumps, $dumpPath)
        Invoke-ProcDumpCapture -ExecutablePath $ExecutablePath -ProcessId $ProcessId -DumpPath $dumpPath
        $dumpPaths.Add($dumpPath)

        if ($index -lt $TotalDumps -and $DelaySeconds -gt 0) {
            Write-Host ("Waiting {0} seconds before the next dump..." -f $DelaySeconds)
            Start-Sleep -Seconds $DelaySeconds
        }
    }

    return $dumpPaths
}

function Restart-ConnectorService {
    param(
        [Parameter(Mandatory = $true)]
        [System.ServiceProcess.ServiceController]$Service
    )

    # Restart the connector only after the dump is safely on disk.
    Restart-Service -Name $Service.Name -Force -ErrorAction Stop
    $Service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(45))
}

if (-not (Test-IsAdministrator)) {
    throw "Run this script from an elevated PowerShell or Command Prompt window."
}

$resolvedProcDumpPath = Resolve-ProcDumpPath -RequestedPath $ProcDumpPath
$service = Get-ConnectorServiceController -RequestedServiceName $ServiceName
$candidates = Get-DllHostCandidates -UserName $ProcessUser
$targetProcess = Resolve-TargetProcess -Candidates $candidates -PreferredSessionId $ExpectedSessionId

Write-Host ""
Write-Host "Axapta Business Connector hang dump capture"
Write-Host ("Service     : {0}" -f $service.DisplayName)
Write-Host ("Process PID : {0}" -f $targetProcess.ProcessId)
Write-Host ("Session Id  : {0}" -f $targetProcess.SessionId)
Write-Host ("Run As      : {0}" -f $targetProcess.User)
Write-Host ("ProcDump    : {0}" -f $resolvedProcDumpPath)
Write-Host ("Dump count  : {0}" -f $DumpCount)
Write-Host ("Dump delay  : {0} seconds" -f $DelaySecondsBetweenDumps)
Write-Host ""

$dumpPaths = Invoke-DumpSequence `
    -ExecutablePath $resolvedProcDumpPath `
    -ProcessId $targetProcess.ProcessId `
    -BaseDirectory $DumpDirectory `
    -TotalDumps $DumpCount `
    -DelaySeconds $DelaySecondsBetweenDumps

Write-Host "Dump capture completed."
foreach ($dumpPath in $dumpPaths) {
    Write-Host ("Saved dump  : {0}" -f $dumpPath)
}

if ($SkipRestart) {
    Write-Host "SkipRestart was specified, so the connector service was left untouched."
    return
}

Write-Host ("Restarting service '{0}'..." -f $service.DisplayName)
Restart-ConnectorService -Service $service
Write-Host "Connector service restarted successfully."
