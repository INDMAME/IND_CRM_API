<#
.SYNOPSIS
Builds IND_CRM_API with the Visual Studio MSBuild toolchain.

.DESCRIPTION
Compiles IND_CRM_API.sln for .NET Framework 4.8 in x86, which is required by
the Axapta 3.0 COM connector. The script uses vswhere.exe when available to
locate the 32-bit MSBuild.exe installed with Visual Studio or Visual Studio
Build Tools. It avoids the legacy .NET Framework MSBuild because it cannot
import the Axapta COM reference here.

When Configuration is Release, a successful build stops the IND_CRM_API Windows
service when it is running, mirrors bin\x86\Release into
C:\inetpub\wwwroot\IND_CRM_API, removes stale files that no longer exist in the
Release output, and restarts the service.

.PARAMETER Configuration
Build configuration. Use Debug for local compilation only, or Release to compile
and publish the API output to IIS.

.PARAMETER Platform
Target platform. Only x86 is allowed because AxaptaCOMConnector is a 32-bit COM
dependency.

.EXAMPLE
.\scripts\build-api.ps1 -Configuration Debug

Compiles Debug|x86 and writes the output to bin\x86\Debug.

.EXAMPLE
.\scripts\build-api.ps1 -Configuration Release

Compiles Release|x86, stops the IND_CRM_API service when needed, mirrors
bin\x86\Release into C:\inetpub\wwwroot\IND_CRM_API, and restarts the service.

.NOTES
Publishing to C:\inetpub usually requires VS Code or PowerShell to run as an
administrator, unless the current user has explicit write permissions there.
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("x86")]
    [string]$Platform = "x86"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$solutionPath = Join-Path $repoRoot "IND_CRM_API.sln"

# IIS target folder used when Release builds are published.
$publishPath = "C:\inetpub\wwwroot\IND_CRM_API"
$serviceName = "IND_CRM_API"

function Find-MSBuild {
    $vswherePaths = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($vswherePath in $vswherePaths) {
        $installations = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
        foreach ($installation in $installations) {
            if ([string]::IsNullOrWhiteSpace($installation)) {
                continue
            }

            $candidate = Join-Path $installation "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path $candidate) {
                return $candidate
            }
        }
    }

    $vsRoots = @(
        "${env:ProgramFiles}\Microsoft Visual Studio",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio"
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($root in $vsRoots) {
        $candidate = Get-ChildItem -Path $root -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "\\MSBuild\\Current\\Bin\\MSBuild\.exe$" -and $_.FullName -notmatch "\\amd64\\" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($candidate) {
            return $candidate.FullName
        }
    }

    return $null
}

function Assert-SafePublishPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $resolvedPublishPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    $expectedPublishPath = [System.IO.Path]::GetFullPath("C:\inetpub\wwwroot\IND_CRM_API").TrimEnd('\')

    if (-not [string]::Equals($resolvedPublishPath, $expectedPublishPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Ruta de publicacion no segura: $resolvedPublishPath"
    }

    return $resolvedPublishPath
}

function Get-RelativePathFromRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath,
        [Parameter(Mandatory = $true)]
        [string]$ChildPath
    )

    $root = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\') + '\'
    $child = [System.IO.Path]::GetFullPath($ChildPath)

    if (-not $child.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "La ruta $child no esta dentro de $root"
    }

    return $child.Substring($root.Length)
}

function Remove-StalePublishItems {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
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
            Write-Host ("Removed stale publish item: {0}" -f $relativePath)
        }
    }
}

function Stop-ServiceForPublish {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Host ("Service {0} not found. Publish will copy files only." -f $Name)
        return $false
    }

    if ($service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Write-Host ("Service {0} is already stopped." -f $Name)
        return $false
    }

    Write-Host ("Stopping service {0} before publish..." -f $Name)
    Stop-Service -Name $Name -ErrorAction Stop
    $service = Get-Service -Name $Name -ErrorAction Stop
    $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(60))
    Write-Host ("Service {0} stopped." -f $Name)
    return $true
}

function Start-ServiceAfterPublish {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    Write-Host ("Starting service {0}..." -f $Name)
    Start-Service -Name $Name -ErrorAction Stop
    $service = Get-Service -Name $Name -ErrorAction Stop
    $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(60))
    Write-Host ("Service {0} started." -f $Name)
}

$msbuild = Find-MSBuild
if (-not $msbuild) {
    throw "No se encontro MSBuild de Visual Studio/Build Tools. Instala Visual Studio Build Tools con .NET Framework targeting pack y Windows SDK."
}

Write-Host "MSBuild: $msbuild"
Write-Host "Solution: $solutionPath"
Write-Host "Configuration: $Configuration"
Write-Host "Platform: $Platform"

& $msbuild $solutionPath /p:Configuration=$Configuration /p:Platform=$Platform /m /v:minimal
$buildExitCode = $LASTEXITCODE
if ($buildExitCode -ne 0) {
    exit $buildExitCode
}

if ($Configuration -eq "Release") {
    $releaseOutputPath = Join-Path $repoRoot "bin\$Platform\Release"

    if (-not (Test-Path $releaseOutputPath)) {
        throw "No se encontro la carpeta de salida Release: $releaseOutputPath"
    }

    $safePublishPath = Assert-SafePublishPath -Path $publishPath
    Write-Host "Publishing Release output to: $safePublishPath"

    $restartService = $false
    try {
        $restartService = Stop-ServiceForPublish -Name $serviceName

        if (-not (Test-Path $safePublishPath)) {
            New-Item -ItemType Directory -Path $safePublishPath -Force | Out-Null
        }

        foreach ($sourceItem in Get-ChildItem -LiteralPath $releaseOutputPath -Force) {
            Copy-Item -LiteralPath $sourceItem.FullName -Destination $safePublishPath -Recurse -Force
        }

        Remove-StalePublishItems -SourcePath $releaseOutputPath -DestinationPath $safePublishPath
        Write-Host "Publish completed."
    }
    catch [System.UnauthorizedAccessException] {
        throw "No tienes permisos para publicar en $safePublishPath. Ejecuta VS Code o PowerShell como administrador, o concede permisos de escritura sobre esa carpeta al usuario actual. Detalle: $($_.Exception.Message)"
    }
    catch {
        throw "No se pudo publicar en $safePublishPath. Detalle: $($_.Exception.Message)"
    }
    finally {
        if ($restartService) {
            Start-ServiceAfterPublish -Name $serviceName
        }
    }
}

exit 0
