[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("DEV", "PROD")]
    [string]$TargetEnvironment,
    [switch]$Apply
)

function Get-EnvironmentDefaults {
    param(
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentName
    )

    switch ($EnvironmentName) {
        "DEV" {
            return @{
                AxConfigFile = "C:\INDAxaptaConfigAPI\CRM_API_AxConfig_DEV.axc"
                BaseUrl = "https://dev.insertec.biz:7776/"
                PublicHost = "dev.insertec.biz"
                PublicIp = "192.168.0.146"
                PublicPort = "7776"
                BlobSegment = "DEV"
                ServiceUser = "INSERTEC\API_AXUSER"
                HttpServiceUser = "INSERTEC\API_AXUSER"
                VerboseLogging = "true"
                LogLevel = "Info"
                CorsEnabled = "true"
                CorsAllowedOrigins = "https://dev.insertec.biz:7702"
            }
        }
        default {
            return @{
                AxConfigFile = "C:\INDAxaptaConfigAPI\CRM_API_AxConfig.axc"
                BaseUrl = "https://crm.insertec.biz:7776/"
                PublicHost = "crm.insertec.biz"
                PublicIp = "212.142.143.182"
                PublicPort = "7776"
                BlobSegment = "PROD"
                ServiceUser = "INSERTEC\API_AXUSER"
                HttpServiceUser = "INSERTEC\API_AXUSER"
                VerboseLogging = "false"
                LogLevel = "Info"
                CorsEnabled = "false"
                CorsAllowedOrigins = ""
            }
        }
    }
}

function New-InteractiveSetting {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Category,
        [bool]$Secret = $false,
        [AllowNull()]
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

function Has-DefaultValue {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Setting
    )

    return $null -ne $Setting.DefaultValue
}

function Format-DisplayValue {
    param(
        [AllowNull()]
        [string]$Value
    )

    if ($null -eq $Value) {
        return "<empty>"
    }

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "<empty>"
    }

    return $Value
}

function Is-ClearCommand {
    param(
        [AllowNull()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    return [string]::Equals($Value.Trim(), "__CLEAR__", [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-AllSettings {
    param(
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentName
    )

    $defaults = Get-EnvironmentDefaults -EnvironmentName $EnvironmentName

    return @(
        New-InteractiveSetting -Name "IND_ENV" -Category "Core" -DefaultValue $EnvironmentName

        New-InteractiveSetting -Name "INDCRM_AX_CONFIG_FILE" -Category "AX" -DefaultValue $defaults.AxConfigFile
        New-InteractiveSetting -Name "USER_DEFAULT" -Category "AX" -DefaultValue "API_AXUSER"
        New-InteractiveSetting -Name "USER_PASS_DEFAULT" -Category "AX" -Secret $true
        New-InteractiveSetting -Name "INDCRM_AX_VERBOSE_LOGGING" -Category "AX" -DefaultValue $defaults.VerboseLogging
        New-InteractiveSetting -Name "INDCRM_AX_VERBOSE_LOG_PATH" -Category "AX" -DefaultValue "C:\INDAxaptaLogs"
        New-InteractiveSetting -Name "INDCRM_AX_ALLOW_DEFAULT_CREDENTIALS" -Category "AX" -DefaultValue "false"

        New-InteractiveSetting -Name "INDCRM_BASE_URL" -Category "Host" -DefaultValue $defaults.BaseUrl
        New-InteractiveSetting -Name "INDCRM_PUBLIC_HOST" -Category "Host" -DefaultValue $defaults.PublicHost
        New-InteractiveSetting -Name "INDCRM_PUBLIC_IP" -Category "Host" -DefaultValue $defaults.PublicIp
        New-InteractiveSetting -Name "INDCRM_PUBLIC_PORT" -Category "Host" -DefaultValue $defaults.PublicPort
        New-InteractiveSetting -Name "INDCRM_CORS_ENABLED" -Category "Host" -DefaultValue $defaults.CorsEnabled
        New-InteractiveSetting -Name "INDCRM_CORS_ALLOWED_ORIGINS" -Category "Host" -DefaultValue $defaults.CorsAllowedOrigins -Required $false
        New-InteractiveSetting -Name "INDCRM_LOG_LEVEL" -Category "Host" -DefaultValue $defaults.LogLevel
        New-InteractiveSetting -Name "INDCRM_LOG_PATH" -Category "Host" -DefaultValue "C:\INDAxaptaLogs"
        New-InteractiveSetting -Name "INDCRM_SERVICE_USER" -Category "Ops" -DefaultValue $defaults.ServiceUser
        New-InteractiveSetting -Name "INDCRM_HTTP_SERVICE_USER" -Category "Ops" -DefaultValue $defaults.HttpServiceUser

        New-InteractiveSetting -Name "INDCRM_JWT_ISSUER" -Category "JWT" -DefaultValue "IND_CRM_API"
        New-InteractiveSetting -Name "INDCRM_JWT_AUDIENCE" -Category "JWT" -DefaultValue "IND_CRM_APIUsers"
        New-InteractiveSetting -Name "JWT_SECRET_KEY" -Category "JWT" -Secret $true
        New-InteractiveSetting -Name "INDCRM_JWT_EXPIRATION_MINUTES" -Category "JWT" -DefaultValue "60"
        New-InteractiveSetting -Name "INDCRM_JWT_REFRESH_THRESHOLD_MINUTES" -Category "JWT" -DefaultValue "5"
        New-InteractiveSetting -Name "INDCRM_SERVICE_PASSWORD" -Category "Ops" -Secret $true

        New-InteractiveSetting -Name "OPENAI_API_KEY" -Category "OpenAI" -Secret $true
        New-InteractiveSetting -Name "OPENAI_TRANSCRIPTION_DEFAULT_PROMPT_PATH" -Category "OpenAI" -DefaultValue "C:\INDAxaptaConfigAPI\Prompts\Wisper\prompt.txt"
        New-InteractiveSetting -Name "OPENAI_TRANSCRIPTION_DEFAULT_PROMPT" -Category "OpenAI" -Required $false
        New-InteractiveSetting -Name ("INDCRM_" + $EnvironmentName + "_PFX_PASSWORD") -Category "HTTPS" -Secret $true -Required $false

        New-InteractiveSetting -Name "AZURE_BLOB_CONNECTION_STRING" -Category "AzureBlob" -Secret $true
        New-InteractiveSetting -Name "AZURE_BLOB_CONTAINER" -Category "AzureBlob" -DefaultValue "tickets"
        New-InteractiveSetting -Name "AZURE_BLOB_ENVIRONMENT_SEGMENT" -Category "AzureBlob" -DefaultValue $defaults.BlobSegment

        New-InteractiveSetting -Name "AZURE_DOCS_IA_KEY" -Category "AzureDocs" -Secret $true
        New-InteractiveSetting -Name "AZURE_DOCS_IA_ENDPOINT" -Category "AzureDocs"
        New-InteractiveSetting -Name "AZURE_DOCS_IA_MODEL" -Category "AzureDocs" -DefaultValue "prebuilt-receipt"

        New-InteractiveSetting -Name "COMPANY_ACCESS_CACHE_MINUTES" -Category "Cache" -DefaultValue "20"
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

        if ((Has-DefaultValue -Setting $Setting) -and -not [string]::IsNullOrWhiteSpace($Setting.DefaultValue)) {
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

    if (Has-DefaultValue -Setting $Setting) {
        return Format-DisplayValue -Value $Setting.DefaultValue
    }

    if (-not $Setting.Required) {
        return "<empty>"
    }

    return "Missing"
}

function Get-DefaultPreviewValue {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Setting
    )

    if ($Setting.Secret) {
        if ((Has-DefaultValue -Setting $Setting) -and -not [string]::IsNullOrWhiteSpace($Setting.DefaultValue)) {
            return "Default available"
        }

        if (-not $Setting.Required) {
            return "<empty>"
        }

        return "Missing"
    }

    if (Has-DefaultValue -Setting $Setting) {
        return Format-DisplayValue -Value $Setting.DefaultValue
    }

    if (-not $Setting.Required) {
        return "<empty>"
    }

    return "Missing"
}

function Build-PlainPromptLabel {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Setting
    )

    $currentValue = [Environment]::GetEnvironmentVariable($Setting.Name, "Machine")

    if (-not [string]::IsNullOrWhiteSpace($currentValue)) {
        if ((Has-DefaultValue -Setting $Setting) -and ($currentValue -ne $Setting.DefaultValue)) {
            $label = "{0} (press Enter to keep current value: {1}; target default for {2}: {3})" -f $Setting.Name, (Format-DisplayValue -Value $currentValue), $TargetEnvironment, (Format-DisplayValue -Value $Setting.DefaultValue)
            if (-not $Setting.Required) {
                return $label + "; type __CLEAR__ to clear"
            }

            return $label
        }

        $label = "{0} (press Enter to keep current value: {1})" -f $Setting.Name, (Format-DisplayValue -Value $currentValue)
        if (-not $Setting.Required) {
            return $label + "; type __CLEAR__ to clear"
        }

        return $label
    }

    if (Has-DefaultValue -Setting $Setting) {
        return "{0} (press Enter to use default value: {1})" -f $Setting.Name, (Format-DisplayValue -Value $Setting.DefaultValue)
    }

    if (-not $Setting.Required) {
        return "{0} (optional, press Enter to keep empty)" -f $Setting.Name
    }

    return "{0} (required)" -f $Setting.Name
}

function Build-SecretPromptLabel {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Setting
    )

    $currentValue = [Environment]::GetEnvironmentVariable($Setting.Name, "Machine")

    if (-not [string]::IsNullOrWhiteSpace($currentValue)) {
        return "{0} (press Enter to keep current value, input hidden)" -f $Setting.Name
    }

    if ((Has-DefaultValue -Setting $Setting) -and -not [string]::IsNullOrWhiteSpace($Setting.DefaultValue)) {
        return "{0} (press Enter to use default value, input hidden)" -f $Setting.Name
    }

    if (-not $Setting.Required) {
        return "{0} (optional, input hidden, press Enter to keep empty)" -f $Setting.Name
    }

    return "{0} (required, input hidden)" -f $Setting.Name
}

function Read-PlainValue {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Setting
    )

    $currentValue = [Environment]::GetEnvironmentVariable($Setting.Name, "Machine")
    $inputValue = Read-Host -Prompt (Build-PlainPromptLabel -Setting $Setting)

    if (Is-ClearCommand -Value $inputValue) {
        if (-not $Setting.Required) {
            return $null
        }

        throw ("{0} cannot be cleared because it is required." -f $Setting.Name)
    }

    if (-not [string]::IsNullOrWhiteSpace($inputValue)) {
        return $inputValue.Trim()
    }

    if (-not [string]::IsNullOrWhiteSpace($currentValue)) {
        return $currentValue
    }

    if (Has-DefaultValue -Setting $Setting) {
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
    $secureValue = Read-Host -Prompt (Build-SecretPromptLabel -Setting $Setting) -AsSecureString
    $plainValue = ConvertTo-PlainText -SecureValue $secureValue

    if (Is-ClearCommand -Value $plainValue) {
        if (-not $Setting.Required) {
            return $null
        }

        throw ("{0} cannot be cleared because it is required." -f $Setting.Name)
    }

    if (-not [string]::IsNullOrWhiteSpace($plainValue)) {
        return $plainValue
    }

    if (-not [string]::IsNullOrWhiteSpace($currentValue)) {
        return $currentValue
    }

    if ((Has-DefaultValue -Setting $Setting) -and -not [string]::IsNullOrWhiteSpace($Setting.DefaultValue)) {
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

    # Optional values are cleared when left empty so App.config fallbacks can stay active.
    if ([string]::IsNullOrWhiteSpace($Value)) {
        [Environment]::SetEnvironmentVariable($Name, $null, "Machine")
        Write-Host ("Cleared machine variable {0}" -f $Name)
        return
    }

    [Environment]::SetEnvironmentVariable($Name, $Value, "Machine")
    Write-Host ("Set machine variable {0}" -f $Name)
}

$settings = Get-AllSettings -EnvironmentName $TargetEnvironment

if (-not $Apply) {
    Write-Host ""
    Write-Host ("IND CRM full machine environment helper for {0}" -f $TargetEnvironment)
    Write-Host "Preview values:"
    Write-Host ""

    $settings |
        Sort-Object Category, Name |
        Select-Object Category, Name, @{ Name = "Current"; Expression = { Get-PreviewValue -Setting $_ } }, @{ Name = ("Default" + $TargetEnvironment); Expression = { Get-DefaultPreviewValue -Setting $_ } } |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "Preview mode only. No machine variables were changed."
    Write-Host "To set the real values interactively, run:"
    Write-Host ""
    Write-Host ("powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-all-env.ps1 -TargetEnvironment {0} -Apply" -f $TargetEnvironment)
    Write-Host ""
    return
}

Write-Host ""
Write-Host ("Applying full machine environment values for {0}" -f $TargetEnvironment)
Write-Host "Press Enter to keep the current value or use the environment default when available."
Write-Host ""

foreach ($setting in $settings) {
    $value = if ($setting.Secret) {
        Read-SecretValue -Setting $setting
    }
    else {
        Read-PlainValue -Setting $setting
    }

    Set-MachineEnvSetting -Name $setting.Name -Value $value
}

Write-Host ""
Write-Host "Done."
Write-Host "Restart the API service so the new machine variables are loaded."
Write-Host "Suggested command: Restart-Service IND_CRM_API"
