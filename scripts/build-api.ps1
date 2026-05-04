<#
.SYNOPSIS
Builds IND_CRM_API with the Visual Studio MSBuild toolchain.

.DESCRIPTION
Compiles IND_CRM_API.sln for .NET Framework 4.8 in x86, which is required by
the Axapta 3.0 COM connector. The script searches for the 32-bit MSBuild.exe
installed with Visual Studio or Visual Studio Build Tools and avoids the legacy
.NET Framework MSBuild because it cannot import the Axapta COM reference here.

When Configuration is Release, a successful build is also published by copying
all files from bin\x86\Release to C:\inetpub\wwwroot\IND_CRM_APP.

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

Compiles Release|x86 and publishes bin\x86\Release to
C:\inetpub\wwwroot\IND_CRM_APP.

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
$publishPath = "C:\inetpub\wwwroot\IND_CRM_APP"

$vsRoots = @(
    "${env:ProgramFiles}\Microsoft Visual Studio",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio"
) | Where-Object { $_ -and (Test-Path $_) }

$msbuild = $null
foreach ($root in $vsRoots) {
    $candidate = Get-ChildItem -Path $root -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\MSBuild\\Current\\Bin\\MSBuild\.exe$" -and $_.FullName -notmatch "\\amd64\\" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($candidate) {
        $msbuild = $candidate.FullName
        break
    }
}

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

    if (-not (Test-Path $publishPath)) {
        New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
    }

    Write-Host "Publishing Release output to: $publishPath"
    try {
        # Mirror the compiled Release output into the IIS application folder.
        Copy-Item -Path (Join-Path $releaseOutputPath "*") -Destination $publishPath -Recurse -Force
        Write-Host "Publish completed."
    }
    catch [System.UnauthorizedAccessException] {
        throw "No tienes permisos para publicar en $publishPath. Ejecuta VS Code o PowerShell como administrador, o concede permisos de escritura sobre esa carpeta al usuario actual. Detalle: $($_.Exception.Message)"
    }
    catch {
        throw "No se pudo publicar en $publishPath. Detalle: $($_.Exception.Message)"
    }
}

exit 0
