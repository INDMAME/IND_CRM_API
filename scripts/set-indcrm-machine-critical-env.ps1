[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("DEV", "PROD")]
    [string]$TargetEnvironment,
    [switch]$Apply
)

function New-CriticalSetting {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Category,
        [bool]$Secret = $false,
        [string]$DefaultValue = $null,
        [bool]$Required = $true
    )

    return [pscustomobject]@{
        Name = $Name
        Category = $Category
        Secret = $Secret
        DefaultValue = $DefaultValue
        Required = $Required
    }
}

function New-RandomSecret {
    param(
        [int]$ByteCount = 64
    )

    $bytes = New-Object byte[] $ByteCount
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }

    return [Convert]::ToBase64String($bytes)
}

function ConvertTo-PlainText {
    param(
        [Parameter(Mandatory = $true)]
        [System.Security.SecureString]$SecureValue
    )

    # Only used in memory to write the final machine environment value.
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)

    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function Get-CriticalSettings {
    param(
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentName
    )

    $publicIpDefault = if ($EnvironmentName -eq "DEV") { "192.168.0.148" } else { "212.142.143.182" }
    $pfxPathDefault = if ($EnvironmentName -eq "DEV") { "C:\INDAxaptaConfigAPI\dev.insertec.biz\dominio.pfx" } else { "C:\INDAxaptaConfigAPI\crm.insertec.biz\dominio.pfx" }

    return @(
        New-CriticalSetting -Name "USER_DEFAULT" -Category "AX" -DefaultValue "APIAX"
        New-CriticalSetting -Name "USER_PASS_DEFAULT" -Category "AX" -Secret $true
        New-CriticalSetting -Name "CRM_TENANT_ID" -Category "WebAuth"
        New-CriticalSetting -Name "CRM_CLIENT_ID" -Category "WebAuth"
        New-CriticalSetting -Name "CRM_CLIENT_SECRET" -Category "WebAuth" -Secret $true
        New-CriticalSetting -Name "CRM_AUTHORITY" -Category "WebAuth"
        New-CriticalSetting -Name "INDCRM_SERVICE_PASSWORD" -Category "Ops" -Secret $true
        New-CriticalSetting -Name "JWT_SECRET_KEY" -Category "JWT" -Secret $true
        New-CriticalSetting -Name "INDCRM_CONTEXT_TOKEN_SECRET_KEY" -Category "JWT" -Secret $true -DefaultValue (New-RandomSecret) -Required $false
        New-CriticalSetting -Name "OPENAI_API_KEY" -Category "OpenAI" -Secret $true
        New-CriticalSetting -Name "AZURE_BLOB_CONNECTION_STRING" -Category "AzureBlob" -Secret $true
        New-CriticalSetting -Name "AZURE_DOCS_IA_KEY" -Category "AzureDocs" -Secret $true
        New-CriticalSetting -Name "AZURE_DOCS_IA_ENDPOINT" -Category "AzureDocs" -DefaultValue "https://westeurope.api.cognitive.microsoft.com/"
        New-CriticalSetting -Name "AZURE_DOCS_IA_MODEL" -Category "AzureDocs"
        New-CriticalSetting -Name "INDCRM_PUBLIC_IP" -Category "Host" -DefaultValue $publicIpDefault
        New-CriticalSetting -Name ("INDCRM_" + $EnvironmentName + "_PFX_PATH") -Category "HTTPS" -DefaultValue $pfxPathDefault -Required $false
        New-CriticalSetting -Name ("INDCRM_" + $EnvironmentName + "_PFX_PASSWORD") -Category "HTTPS" -Secret $true -Required $false
    )
}

function Get-PreviewValue {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Setting
    )

    $currentValue = [Environment]::GetEnvironmentVariable($Setting.Name, "Machine")

    if ($Setting.Secret) {
        if (-not [string]::IsNullOrWhiteSpace($currentValue)) {
            return "Configured"
        }

        if (-not [string]::IsNullOrWhiteSpace($Setting.DefaultValue)) {
            return "Default available"
        }

        if (-not $Setting.Required) {
            return "<empty>"
        }

        return "Missing"
    }

    if (-not [string]::IsNullOrWhiteSpace($currentValue)) {
        return $currentValue
    }

    if (-not [string]::IsNullOrWhiteSpace($Setting.DefaultValue)) {
        return $Setting.DefaultValue
    }

    if (-not $Setting.Required) {
        return "<empty>"
    }

    return "Missing"
}

function Read-PlainValue {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Setting
    )

    $currentValue = [Environment]::GetEnvironmentVariable($Setting.Name, "Machine")
    $label = if (-not [string]::IsNullOrWhiteSpace($currentValue)) {
        "{0} (press Enter to keep current value: {1})" -f $Setting.Name, $currentValue
    }
    elseif (-not [string]::IsNullOrWhiteSpace($Setting.DefaultValue)) {
        "{0} (press Enter to use default value: {1})" -f $Setting.Name, $Setting.DefaultValue
    }
    elseif (-not $Setting.Required) {
        "{0} (optional)" -f $Setting.Name
    }
    else {
        "{0} (required)" -f $Setting.Name
    }

    $inputValue = Read-Host -Prompt $label

    if (-not [string]::IsNullOrWhiteSpace($inputValue)) {
        return $inputValue.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($currentValue)) {
        return $currentValue
    }

    if (-not [string]::IsNullOrWhiteSpace($Setting.DefaultValue)) {
        return $Setting.DefaultValue
    }

    if (-not $Setting.Required) {
        return $null
    }

    throw ("A value is required for {0}." -f $Setting.Name)
}

function Read-SecretValue {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Setting
    )

    $currentValue = [Environment]::GetEnvironmentVariable($Setting.Name, "Machine")
    $prompt = if ([string]::IsNullOrWhiteSpace($currentValue)) {
        if (-not [string]::IsNullOrWhiteSpace($Setting.DefaultValue)) {
            "{0} (press Enter to use generated default, input hidden)" -f $Setting.Name
        }
        elseif (-not $Setting.Required) {
            "{0} (optional, input hidden, press Enter to keep empty)" -f $Setting.Name
        }
        else {
            "{0} (required, input hidden)" -f $Setting.Name
        }
    }
    else {
        "{0} (press Enter to keep current value, input hidden)" -f $Setting.Name
    }

    $secureValue = Read-Host -Prompt $prompt -AsSecureString
    $plainValue = ConvertTo-PlainText -SecureValue $secureValue

    if (-not [string]::IsNullOrWhiteSpace($plainValue)) {
        return $plainValue
    }

    if (-not [string]::IsNullOrWhiteSpace($currentValue)) {
        return $currentValue
    }

    if (-not [string]::IsNullOrWhiteSpace($Setting.DefaultValue)) {
        return $Setting.DefaultValue
    }

    if (-not $Setting.Required) {
        return $null
    }

    throw ("A value is required for {0}." -f $Setting.Name)
}

function Set-MachineEnvSetting {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [AllowNull()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        [Environment]::SetEnvironmentVariable($Name, $null, "Machine")
        Write-Host ("Cleared machine variable {0}" -f $Name)
        return
    }

    [Environment]::SetEnvironmentVariable($Name, $Value, "Machine")
    Write-Host ("Set machine variable {0}" -f $Name)
}

function Get-ResolvedSettingValue {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$ResolvedSettings,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $setting = $ResolvedSettings | Where-Object { $_.Name -eq $Name } | Select-Object -First 1
    if ($null -eq $setting) {
        return $null
    }

    return $setting.Value
}

function Assert-ResolvedSettingValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [AllowNull()]
        [string]$Actual,
        [Parameter(Mandatory = $true)]
        [string]$Expected
    )

    if ([string]::IsNullOrWhiteSpace($Actual) -or
        -not [string]::Equals($Actual.Trim(), $Expected, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw ("{0} must be {1}. Current value: {2}" -f $Name, $Expected, $(if ([string]::IsNullOrWhiteSpace($Actual)) { "<empty>" } else { $Actual }))
    }
}

function Assert-ResolvedCriticalSettings {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$ResolvedSettings,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentName
    )

    $expectedPublicIp = if ($EnvironmentName -eq "DEV") { "192.168.0.148" } else { "212.142.143.182" }
    Assert-ResolvedSettingValue -Name "INDCRM_PUBLIC_IP" -Actual (Get-ResolvedSettingValue -ResolvedSettings $ResolvedSettings -Name "INDCRM_PUBLIC_IP") -Expected $expectedPublicIp
}

$settings = Get-CriticalSettings -EnvironmentName $TargetEnvironment

if (-not $Apply) {
    Write-Host ""
    Write-Host ("IND CRM critical machine environment helper for {0}" -f $TargetEnvironment)
    Write-Host "Preview values:"
    Write-Host ""

    $settings |
        Sort-Object Category, Name |
        Select-Object Category, Name, @{ Name = "CurrentOrDefault"; Expression = { Get-PreviewValue -Setting $_ } } |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "Preview mode only. No machine variables were changed."
    Write-Host "To set the real values interactively, run:"
    Write-Host ""
    Write-Host ("powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-critical-env.ps1 -TargetEnvironment {0} -Apply" -f $TargetEnvironment)
    Write-Host ""
    return
}

Write-Host ""
Write-Host ("Applying critical machine environment values for {0}" -f $TargetEnvironment)
Write-Host ""

$resolvedSettings = @()
foreach ($setting in $settings) {
    $value = if ($setting.Secret) {
        Read-SecretValue -Setting $setting
    }
    else {
        Read-PlainValue -Setting $setting
    }

    $resolvedSettings += [pscustomobject]@{
        Name = $setting.Name
        Value = $value
    }
}

Assert-ResolvedCriticalSettings -ResolvedSettings $resolvedSettings -EnvironmentName $TargetEnvironment

foreach ($resolvedSetting in $resolvedSettings) {
    Set-MachineEnvSetting -Name $resolvedSetting.Name -Value $resolvedSetting.Value
}

Write-Host ""
Write-Host "Done."
Write-Host "Suggested next steps:"
Write-Host "1. Run the bootstrap script if the machine does not have the base environment values yet."
Write-Host "2. Restart the API service so the new machine variables are loaded."
Write-Host "Suggested command: Restart-Service IND_CRM_API"
