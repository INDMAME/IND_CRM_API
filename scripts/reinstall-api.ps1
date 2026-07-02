<#
.SYNOPSIS
Reinstalls the local IND_CRM_API Windows service after API changes.

.DESCRIPTION
Builds IND_CRM_API as Release|x86, stops and removes the existing Windows
service, mirrors bin\x86\Release into C:\inetpub\wwwroot\IND_CRM_API, creates
the service again, and starts it.

The script defaults to preview mode. Pass -Apply from an elevated PowerShell
session to change the machine. Service credentials are read from machine
environment variables and are never printed:

- INDCRM_SERVICE_USER
- INDCRM_SERVICE_PASSWORD

.EXAMPLE
.\scripts\reinstall-api.ps1

Shows the reinstall plan without changing files or services.

.EXAMPLE
.\scripts\reinstall-api.ps1 -Apply

Builds, publishes, reinstalls, and starts IND_CRM_API.

.EXAMPLE
.\scripts\reinstall-api.ps1 -Apply -SkipBuild

Reinstalls from the current bin\x86\Release output.
#>

[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$SkipBuild,
    [switch]$NoStart,
    [switch]$SkipHealthCheck,
    [switch]$RunAsLocalSystem,
    [int]$ServiceTimeoutSeconds = 60
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$solutionPath = Join-Path $repoRoot "IND_CRM_API.sln"
$releaseOutputPath = Join-Path $repoRoot "bin\x86\Release"
$installPath = "C:\inetpub\wwwroot\IND_CRM_API"
$serviceName = "IND_CRM_API"
$exeName = "IND_CRM_API.exe"
$exePath = Join-Path $installPath $exeName

function Write-Step {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host ""
    Write-Host ("==> {0}" -f $Message)
}

function Assert-Administrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run this script from an elevated PowerShell session."
    }
}

function Assert-SafeInstallPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolvedInstallPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    $expectedInstallPath = [System.IO.Path]::GetFullPath("C:\inetpub\wwwroot\IND_CRM_API").TrimEnd('\')

    if (-not [string]::Equals($resolvedInstallPath, $expectedInstallPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe install path: $resolvedInstallPath"
    }

    return $resolvedInstallPath
}

function Get-EnvironmentValue {
    param([Parameter(Mandatory = $true)][string]$Name)

    $value = [Environment]::GetEnvironmentVariable($Name, "Process")
    if (-not [string]::IsNullOrWhiteSpace($value)) {
        return $value
    }

    $value = [Environment]::GetEnvironmentVariable($Name, "Machine")
    if (-not [string]::IsNullOrWhiteSpace($value)) {
        return $value
    }

    return [Environment]::GetEnvironmentVariable($Name, "User")
}

function Resolve-MSBuild {
    $vswherePaths = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    foreach ($vswherePath in $vswherePaths) {
        $installations = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
        foreach ($installation in $installations) {
            if ([string]::IsNullOrWhiteSpace($installation)) {
                continue
            }

            $candidate = Join-Path $installation "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path -LiteralPath $candidate) {
                return $candidate
            }
        }
    }

    $vsRoots = @(
        "${env:ProgramFiles}\Microsoft Visual Studio",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    foreach ($root in $vsRoots) {
        $candidate = Get-ChildItem -Path $root -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "\\MSBuild\\Current\\Bin\\MSBuild\.exe$" -and $_.FullName -notmatch "\\amd64\\" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($candidate) {
            return $candidate.FullName
        }
    }

    throw "MSBuild.exe was not found. Install Visual Studio Build Tools with .NET Framework 4.8 targeting pack."
}

function Invoke-ReleaseBuild {
    $msbuild = Resolve-MSBuild
    Write-Host "MSBuild: $msbuild"
    Write-Host "Solution: $solutionPath"
    Write-Host "Configuration: Release"
    Write-Host "Platform: x86"

    & $msbuild $solutionPath /p:Configuration=Release /p:Platform=x86 /m /v:minimal
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

function Get-RelativePathFromRoot {
    param(
        [Parameter(Mandatory = $true)][string]$RootPath,
        [Parameter(Mandatory = $true)][string]$ChildPath
    )

    $root = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\') + '\'
    $child = [System.IO.Path]::GetFullPath($ChildPath)

    if (-not $child.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path $child is not inside $root"
    }

    return $child.Substring($root.Length)
}

function Remove-StaleInstallItems {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $sourceRelativePaths = @{}
    foreach ($sourceItem in Get-ChildItem -LiteralPath $SourcePath -Recurse -Force) {
        $relativePath = Get-RelativePathFromRoot -RootPath $SourcePath -ChildPath $sourceItem.FullName
        $sourceRelativePaths[$relativePath.ToLowerInvariant()] = $true
    }

    $destinationItems = Get-ChildItem -LiteralPath $DestinationPath -Recurse -Force |
        Sort-Object { $_.FullName.Length } -Descending

    foreach ($destinationItem in $destinationItems) {
        $relativePath = Get-RelativePathFromRoot -RootPath $DestinationPath -ChildPath $destinationItem.FullName
        if (-not $sourceRelativePaths.ContainsKey($relativePath.ToLowerInvariant())) {
            Remove-Item -LiteralPath $destinationItem.FullName -Recurse -Force
            Write-Host ("Removed stale install item: {0}" -f $relativePath)
        }
    }
}

function Publish-ReleaseOutput {
    param([Parameter(Mandatory = $true)][string]$SafeInstallPath)

    if (-not (Test-Path -LiteralPath $releaseOutputPath)) {
        throw "Release output folder was not found: $releaseOutputPath"
    }

    if (-not (Test-Path -LiteralPath $SafeInstallPath)) {
        New-Item -ItemType Directory -Path $SafeInstallPath -Force | Out-Null
    }

    foreach ($sourceItem in Get-ChildItem -LiteralPath $releaseOutputPath -Force) {
        Copy-Item -LiteralPath $sourceItem.FullName -Destination $SafeInstallPath -Recurse -Force
    }

    Remove-StaleInstallItems -SourcePath $releaseOutputPath -DestinationPath $SafeInstallPath
}

function Stop-ExistingService {
    param([Parameter(Mandatory = $true)][string]$Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        Write-Host "Service $Name is not installed."
        return
    }

    if ($service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Write-Host "Service $Name is already stopped."
        return
    }

    Write-Host "Stopping service $Name..."
    Stop-Service -Name $Name -ErrorAction Stop
    $service = Get-Service -Name $Name -ErrorAction Stop
    $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds($ServiceTimeoutSeconds))
    Write-Host "Service $Name stopped."
}

function Remove-ExistingService {
    param([Parameter(Mandatory = $true)][string]$Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        Write-Host "Service $Name does not need removal."
        return
    }

    Write-Host "Removing service $Name..."
    & sc.exe delete $Name | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Could not delete service $Name."
    }

    $deadline = (Get-Date).AddSeconds($ServiceTimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 500
        $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($null -eq $service) {
            Write-Host "Service $Name removed."
            return
        }
    } while ((Get-Date) -lt $deadline)

    throw "Service $Name still exists after delete. It may be pending deletion."
}

function New-ServiceDisplayName {
    $environmentName = Get-EnvironmentValue -Name "IND_ENV"
    if ([string]::IsNullOrWhiteSpace($environmentName)) {
        return "CRM API"
    }

    return "CRM API " + $environmentName.Trim()
}

function Assert-RequiredEnvironment {
    $requiredNames = @(
        "IND_ENV",
        "ASPNETCORE_ENVIRONMENT",
        "INDCRM_AX_CONFIG_FILE",
        "INDCRM_PUBLIC_HOST",
        "INDCRM_PUBLIC_PORT",
        "INDCRM_BASE_URL",
        "AZURE_BLOB_ENVIRONMENT_SEGMENT"
    )

    foreach ($name in $requiredNames) {
        $value = Get-EnvironmentValue -Name $name
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw "$name is not defined. Run scripts\set-indcrm-machine-all-env.ps1 -TargetEnvironment DEV/PROD -Apply first."
        }
    }
}

function Get-ExpectedAspNetCoreEnvironment {
    param([Parameter(Mandatory = $true)][string]$EnvironmentName)

    if ([string]::Equals($EnvironmentName, "DEV", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "Development"
    }

    if ([string]::Equals($EnvironmentName, "PROD", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "Production"
    }

    throw "IND_ENV must be DEV or PROD. Current value: $EnvironmentName"
}

function Get-ExpectedAxConfigFileName {
    param([Parameter(Mandatory = $true)][string]$EnvironmentName)

    if ([string]::Equals($EnvironmentName, "DEV", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "CRM_API_AxConfig_DEV.axc"
    }

    if ([string]::Equals($EnvironmentName, "PROD", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "CRM_API_AxConfig_PROD.axc"
    }

    throw "IND_ENV must be DEV or PROD. Current value: $EnvironmentName"
}

function Assert-ExpectedEnvironmentValue {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()][string]$Actual,
        [Parameter(Mandatory = $true)][string]$Expected
    )

    if ([string]::IsNullOrWhiteSpace($Actual) -or
        -not [string]::Equals($Actual.Trim(), $Expected, [System.StringComparison]::OrdinalIgnoreCase)) {
        $actualText = if ([string]::IsNullOrWhiteSpace($Actual)) { "<empty>" } else { $Actual }
        throw "$Name must be $Expected. Current value: $actualText"
    }
}

function Assert-EnvironmentAlignment {
    $environmentName = Get-EnvironmentValue -Name "IND_ENV"
    $expectedAspNetCoreEnvironment = Get-ExpectedAspNetCoreEnvironment -EnvironmentName $environmentName
    $expectedAxConfigFileName = Get-ExpectedAxConfigFileName -EnvironmentName $environmentName

    Assert-ExpectedEnvironmentValue `
        -Name "ASPNETCORE_ENVIRONMENT" `
        -Actual (Get-EnvironmentValue -Name "ASPNETCORE_ENVIRONMENT") `
        -Expected $expectedAspNetCoreEnvironment

    Assert-ExpectedEnvironmentValue `
        -Name "AZURE_BLOB_ENVIRONMENT_SEGMENT" `
        -Actual (Get-EnvironmentValue -Name "AZURE_BLOB_ENVIRONMENT_SEGMENT") `
        -Expected $environmentName.Trim()

    $axConfigFile = Get-EnvironmentValue -Name "INDCRM_AX_CONFIG_FILE"
    $actualAxConfigFileName = if ([string]::IsNullOrWhiteSpace($axConfigFile)) { $null } else { [System.IO.Path]::GetFileName($axConfigFile.Trim()) }
    Assert-ExpectedEnvironmentValue `
        -Name "INDCRM_AX_CONFIG_FILE" `
        -Actual $actualAxConfigFileName `
        -Expected $expectedAxConfigFileName
}

function New-ServiceCredentialFromEnvironment {
    if ($RunAsLocalSystem) {
        return $null
    }

    $user = Get-EnvironmentValue -Name "INDCRM_SERVICE_USER"
    $password = Get-EnvironmentValue -Name "INDCRM_SERVICE_PASSWORD"

    if ([string]::IsNullOrWhiteSpace($user)) {
        throw "INDCRM_SERVICE_USER is not defined. Define the service account outside the repository before reinstalling."
    }

    if ([string]::IsNullOrWhiteSpace($password)) {
        throw "INDCRM_SERVICE_PASSWORD is not defined. Define the service password outside the repository before reinstalling."
    }

    $securePassword = ConvertTo-SecureString $password -AsPlainText -Force
    return New-Object System.Management.Automation.PSCredential($user, $securePassword)
}

function Install-ServiceFromPublishedExe {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$PublishedExePath
    )

    if (-not (Test-Path -LiteralPath $PublishedExePath)) {
        throw "Published executable was not found: $PublishedExePath"
    }

    $displayName = New-ServiceDisplayName
    $description = "API REST de integracion CRM con Axapta (Business Connector)."
    $credential = New-ServiceCredentialFromEnvironment

    Write-Host "Installing service $Name..."
    Write-Host "Display name: $displayName"
    if ($credential) {
        Write-Host "Service user: $($credential.UserName)"
        New-Service -Name $Name `
            -BinaryPathName ('"{0}"' -f $PublishedExePath) `
            -DisplayName $displayName `
            -StartupType Automatic `
            -Credential $credential | Out-Null
    }
    else {
        Write-Host "Service user: LocalSystem"
        New-Service -Name $Name `
            -BinaryPathName ('"{0}"' -f $PublishedExePath) `
            -DisplayName $displayName `
            -StartupType Automatic | Out-Null
    }

    & sc.exe description $Name $description | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Service $Name was installed, but its description could not be set."
    }
}

function Start-InstalledService {
    param([Parameter(Mandatory = $true)][string]$Name)

    Write-Host "Starting service $Name..."
    Start-Service -Name $Name -ErrorAction Stop
    $service = Get-Service -Name $Name -ErrorAction Stop
    $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds($ServiceTimeoutSeconds))
    Write-Host "Service $Name is running."
}

function Get-HealthPingUrl {
    $baseUrl = Get-EnvironmentValue -Name "INDCRM_BASE_URL"
    if ([string]::IsNullOrWhiteSpace($baseUrl)) {
        return $null
    }

    $baseUrl = $baseUrl.Trim()
    if (-not $baseUrl.EndsWith("/")) {
        $baseUrl += "/"
    }

    return $baseUrl + "api/health/ping"
}

function Test-HealthPing {
    $pingUrl = Get-HealthPingUrl
    if ([string]::IsNullOrWhiteSpace($pingUrl)) {
        Write-Warning "INDCRM_BASE_URL is not available, so health ping was skipped."
        return
    }

    try {
        Write-Host "Health ping: $pingUrl"
        $response = Invoke-WebRequest -Uri $pingUrl -UseBasicParsing -TimeoutSec 15
        Write-Host ("Health ping returned HTTP {0}." -f [int]$response.StatusCode)
    }
    catch {
        Write-Warning ("Health ping failed after service start: {0}" -f $_.Exception.Message)
    }
}

$safeInstallPath = Assert-SafeInstallPath -Path $installPath

Write-Host "IND_CRM_API reinstall"
Write-Host "Repository: $repoRoot"
Write-Host "Install path: $safeInstallPath"
Write-Host "Service: $serviceName"
Write-Host "Mode: $(if ($Apply) { 'Apply' } else { 'Preview' })"

if (-not $Apply) {
    Write-Host ""
    Write-Host "Preview only. Run with -Apply from an elevated PowerShell session to reinstall."
    Write-Host "Planned actions:"
    Write-Host "  1. Build Release|x86 unless -SkipBuild is passed."
    Write-Host "  2. Stop service $serviceName if it exists."
    Write-Host "  3. Mirror $releaseOutputPath into $safeInstallPath."
    Write-Host "  4. Delete and recreate service $serviceName from $exePath."
    Write-Host "  5. Start service unless -NoStart is passed."
    Write-Host "  6. Ping /api/health/ping unless -SkipHealthCheck is passed."
    exit 0
}

Assert-Administrator
Assert-RequiredEnvironment
Assert-EnvironmentAlignment

if (-not $SkipBuild) {
    Write-Step "Building Release|x86"
    Invoke-ReleaseBuild
}
else {
    Write-Step "Skipping build"
    if (-not (Test-Path -LiteralPath $releaseOutputPath)) {
        throw "Release output folder was not found: $releaseOutputPath"
    }
}

Write-Step "Stopping existing service"
Stop-ExistingService -Name $serviceName

Write-Step "Publishing Release output"
Publish-ReleaseOutput -SafeInstallPath $safeInstallPath

Write-Step "Removing existing service"
Remove-ExistingService -Name $serviceName

Write-Step "Installing service"
Install-ServiceFromPublishedExe -Name $serviceName -PublishedExePath $exePath

if ($NoStart) {
    Write-Step "Service start skipped"
}
else {
    Write-Step "Starting service"
    Start-InstalledService -Name $serviceName

    if (-not $SkipHealthCheck) {
        Write-Step "Running health ping"
        Test-HealthPing
    }
}

Write-Host ""
Write-Host "IND_CRM_API reinstall completed."
