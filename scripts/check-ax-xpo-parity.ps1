<#
.SYNOPSIS
Validates the canonical API XPO inventory and its APP mirror.

.DESCRIPTION
Uses Git's tracked XPO files in IND_CRM_API as the canonical inventory. It
requires IND_CRM_APP to track the same relative paths and compares the current
working-tree bytes with SHA-256. Untracked local XPO files are intentionally
ignored.

Each tracked XPO file is also checked for Windows-1252 compatibility, CRLF line
endings, absence of a UTF-8 BOM, and balanced SOURCE/ENDSOURCE blocks.

.PARAMETER ApiRepositoryPath
Path to the canonical IND_CRM_API repository. Defaults to the repository that
contains this script.

.PARAMETER AppRepositoryPath
Path to the IND_CRM_APP mirror repository. Defaults to the IND_CRM_APP sibling
of the canonical API repository.

.EXAMPLE
.\scripts\check-ax-xpo-parity.ps1

Validates the standard sibling repository layout.

.EXAMPLE
.\scripts\check-ax-xpo-parity.ps1 -AppRepositoryPath C:\work\IND_CRM_APP

Validates a mirror in a custom location.
#>

[CmdletBinding()]
param(
    [string]$ApiRepositoryPath,
    [string]$AppRepositoryPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$failures = [System.Collections.Generic.List[string]]::new()
$validatedFileCount = 0

function Add-ValidationFailure {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $failures.Add($Message)
}

function Resolve-GitRepositoryRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $candidate = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $candidate -PathType Container)) {
        throw "$Label repository directory does not exist: $candidate"
    }

    $gitOutput = @(& git -C $candidate rev-parse --show-toplevel 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "$Label path is not a readable Git repository: $candidate. $($gitOutput -join ' ')"
    }

    return [System.IO.Path]::GetFullPath([string]$gitOutput[-1])
}

function Get-TrackedXpoPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryPath,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $gitOutput = @(& git -C $RepositoryPath ls-files -- "*.xpo" 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read tracked XPO files from $Label. $($gitOutput -join ' ')"
    }

    return @(
        $gitOutput |
            ForEach-Object { ([string]$_).Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object
    )
}

function New-OrdinalPathSet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Paths
    )

    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($path in $Paths) {
        $null = $set.Add($path)
    }

    return ,$set
}

function Test-ByteArraysEqual {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]]$First,
        [Parameter(Mandatory = $true)]
        [byte[]]$Second
    )

    if ($First.Length -ne $Second.Length) {
        return $false
    }

    for ($index = 0; $index -lt $First.Length; $index++) {
        if ($First[$index] -ne $Second[$index]) {
            return $false
        }
    }

    return $true
}

function Test-XpoFormat {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryPath,
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,
        [Parameter(Mandatory = $true)]
        [string]$Label,
        [Parameter(Mandatory = $true)]
        [System.Text.Encoding]$Windows1252
    )

    $fullPath = Join-Path $RepositoryPath $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Add-ValidationFailure "$Label file is tracked but missing from the working tree: $RelativePath"
        return
    }

    $script:validatedFileCount++
    $bytes = [System.IO.File]::ReadAllBytes($fullPath)

    if (
        $bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF
    ) {
        Add-ValidationFailure "$Label file has a UTF-8 BOM: $RelativePath"
    }

    if ($bytes -contains 0x00) {
        Add-ValidationFailure "$Label file contains NUL bytes and is not valid Windows-1252 text: $RelativePath"
    }

    $text = $null
    try {
        $text = $Windows1252.GetString($bytes)
        $roundTripBytes = $Windows1252.GetBytes($text)
        if (-not (Test-ByteArraysEqual -First $bytes -Second $roundTripBytes)) {
            Add-ValidationFailure "$Label file does not round-trip as Windows-1252: $RelativePath"
        }
    }
    catch {
        Add-ValidationFailure "$Label file is not Windows-1252 compatible: $RelativePath ($($_.Exception.Message))"
    }

    $bareLfCount = 0
    $bareCrCount = 0
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        if ($bytes[$index] -eq 0x0A -and ($index -eq 0 -or $bytes[$index - 1] -ne 0x0D)) {
            $bareLfCount++
        }

        if ($bytes[$index] -eq 0x0D -and ($index -eq $bytes.Length - 1 -or $bytes[$index + 1] -ne 0x0A)) {
            $bareCrCount++
        }
    }

    if ($bareLfCount -gt 0) {
        Add-ValidationFailure "$Label file contains $bareLfCount bare LF line ending(s): $RelativePath"
    }

    if ($bareCrCount -gt 0) {
        Add-ValidationFailure "$Label file contains $bareCrCount bare CR line ending(s): $RelativePath"
    }

    if ($null -eq $text) {
        return
    }

    $sourceCount = 0
    $endSourceCount = 0
    $sourceBalance = 0
    $endSourceBeforeSource = $false
    $lines = [System.Text.RegularExpressions.Regex]::Split($text, "\r\n|\n|\r")

    foreach ($line in $lines) {
        if ([System.Text.RegularExpressions.Regex]::IsMatch($line, "^\s*ENDSOURCE(?:\s|$)")) {
            $endSourceCount++
            $sourceBalance--
            if ($sourceBalance -lt 0) {
                $endSourceBeforeSource = $true
            }
        }
        elseif ([System.Text.RegularExpressions.Regex]::IsMatch($line, "^\s*SOURCE(?:\s|$)")) {
            $sourceCount++
            $sourceBalance++
        }
    }

    if ($endSourceBeforeSource -or $sourceBalance -ne 0 -or $sourceCount -ne $endSourceCount) {
        Add-ValidationFailure "$Label file has unbalanced SOURCE/ENDSOURCE blocks ($sourceCount/$endSourceCount): $RelativePath"
    }
}

if ([string]::IsNullOrWhiteSpace($ApiRepositoryPath)) {
    $ApiRepositoryPath = Join-Path $PSScriptRoot ".."
}

$apiRoot = Resolve-GitRepositoryRoot -Path $ApiRepositoryPath -Label "API"
if ([string]::IsNullOrWhiteSpace($AppRepositoryPath)) {
    $AppRepositoryPath = Join-Path (Split-Path $apiRoot -Parent) "IND_CRM_APP"
}
$appRoot = Resolve-GitRepositoryRoot -Path $AppRepositoryPath -Label "APP"

if ([string]::Equals($apiRoot, $appRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "API and APP repository paths must point to different repositories."
}

$apiPaths = @(Get-TrackedXpoPaths -RepositoryPath $apiRoot -Label "API")
$appPaths = @(Get-TrackedXpoPaths -RepositoryPath $appRoot -Label "APP")

if ($apiPaths.Count -eq 0) {
    throw "The canonical API repository does not track any XPO files."
}

$apiPathSet = New-OrdinalPathSet -Paths $apiPaths
$appPathSet = New-OrdinalPathSet -Paths $appPaths

foreach ($relativePath in $apiPaths) {
    if (-not $appPathSet.Contains($relativePath)) {
        Add-ValidationFailure "APP is missing canonical tracked XPO path: $relativePath"
    }
}

foreach ($relativePath in $appPaths) {
    if (-not $apiPathSet.Contains($relativePath)) {
        Add-ValidationFailure "APP tracks an XPO path that is not in the canonical API inventory: $relativePath"
    }
}

try {
    [System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
}
catch {
    # Windows PowerShell already provides Windows-1252 without a provider.
}

$windows1252 = [System.Text.Encoding]::GetEncoding(
    1252,
    [System.Text.EncoderExceptionFallback]::new(),
    [System.Text.DecoderExceptionFallback]::new()
)

foreach ($relativePath in $apiPaths) {
    Test-XpoFormat -RepositoryPath $apiRoot -RelativePath $relativePath -Label "API" -Windows1252 $windows1252
}

foreach ($relativePath in $appPaths) {
    Test-XpoFormat -RepositoryPath $appRoot -RelativePath $relativePath -Label "APP" -Windows1252 $windows1252
}

$commonPaths = @($apiPaths | Where-Object { $appPathSet.Contains($_) })
foreach ($relativePath in $commonPaths) {
    $apiFile = Join-Path $apiRoot $relativePath
    $appFile = Join-Path $appRoot $relativePath

    if (
        -not (Test-Path -LiteralPath $apiFile -PathType Leaf) -or
        -not (Test-Path -LiteralPath $appFile -PathType Leaf)
    ) {
        continue
    }

    $apiHash = (Get-FileHash -LiteralPath $apiFile -Algorithm SHA256).Hash
    $appHash = (Get-FileHash -LiteralPath $appFile -Algorithm SHA256).Hash
    if (-not [string]::Equals($apiHash, $appHash, [System.StringComparison]::Ordinal)) {
        Add-ValidationFailure "SHA-256 mismatch for $relativePath (API $apiHash, APP $appHash)"
    }
}

Write-Host ("Canonical API tracked XPO files: {0}" -f $apiPaths.Count)
Write-Host ("APP tracked XPO mirror files: {0}" -f $appPaths.Count)
Write-Host ("XPO files checked for format: {0}" -f $validatedFileCount)
Write-Host ("XPO pairs checked with SHA-256: {0}" -f $commonPaths.Count)

if ($failures.Count -gt 0) {
    Write-Host ("XPO parity and format validation failed with {0} issue(s):" -f $failures.Count) -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host (" - {0}" -f $failure) -ForegroundColor Red
    }

    exit 1
}

Write-Host "XPO parity and format validation passed." -ForegroundColor Green
exit 0
