<#
.SYNOPSIS
Creates the private CRM help analytics directory and restricts its ACL.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ServiceIdentity,

    [string]$AnalyticsPath = 'C:\INDData\CRMHelpAnalytics',

    [switch]$AllowExisting
)

$ErrorActionPreference = 'Stop'
$target = [System.IO.Path]::GetFullPath($AnalyticsPath).TrimEnd('\')
$expectedTarget = [System.IO.Path]::GetFullPath('C:\INDData\CRMHelpAnalytics').TrimEnd('\')
if (-not [string]::Equals($target, $expectedTarget, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to change ACL outside the exact analytics target: $expectedTarget"
}
if ((Test-Path -LiteralPath $target) -and -not $AllowExisting) {
    $existingItems = @(Get-ChildItem -LiteralPath $target -Force -ErrorAction Stop)
    if ($existingItems.Count -gt 0) {
        throw 'The analytics directory already contains data. Review it and pass -AllowExisting explicitly.'
    }
}

New-Item -ItemType Directory -Path $target -Force | Out-Null
foreach ($child in 'events','review','aggregates','quarantine') {
    New-Item -ItemType Directory -Path (Join-Path $target $child) -Force | Out-Null
}

$acl = [System.Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true, $false)
$inheritance = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
$propagation = [System.Security.AccessControl.PropagationFlags]::None
$allow = [System.Security.AccessControl.AccessControlType]::Allow
# Resolve built-in accounts by SID so localized Windows names do not break ACL setup.
$serviceSid = ([System.Security.Principal.NTAccount]::new($ServiceIdentity)).Translate(
    [System.Security.Principal.SecurityIdentifier])
$systemSid = [System.Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$administratorsSid = [System.Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$rules = @(
    [System.Security.AccessControl.FileSystemAccessRule]::new($serviceSid, 'Modify', $inheritance, $propagation, $allow),
    [System.Security.AccessControl.FileSystemAccessRule]::new($systemSid, 'FullControl', $inheritance, $propagation, $allow),
    [System.Security.AccessControl.FileSystemAccessRule]::new($administratorsSid, 'FullControl', $inheritance, $propagation, $allow)
)
foreach ($rule in $rules) { [void]$acl.AddAccessRule($rule) }
Set-Acl -LiteralPath $target -AclObject $acl

$volume = $null
$bitLocker = $null
if (Get-Command Get-Volume -ErrorAction SilentlyContinue) {
    $volume = Get-Volume -DriveLetter ([System.IO.Path]::GetPathRoot($target).Substring(0,1)) -ErrorAction SilentlyContinue
}
if ($volume -and (Get-Command Get-BitLockerVolume -ErrorAction SilentlyContinue)) {
    $bitLocker = Get-BitLockerVolume -MountPoint $volume.Path -ErrorAction SilentlyContinue
}
$encrypted = $bitLocker -and $bitLocker.ProtectionStatus -eq 'On'
Write-Host "Analytics path: $target"
Write-Host "ACL restricted for service identity: $ServiceIdentity"
Write-Host "Encrypted volume detected: $encrypted"
Write-Host 'Enable AnalyticsAclReady only after reviewing Get-Acl output.'
Write-Host 'Enable AnalyticsVolumeEncrypted and AnalyticsTextCaptureEnabled only when encryption is confirmed.'
