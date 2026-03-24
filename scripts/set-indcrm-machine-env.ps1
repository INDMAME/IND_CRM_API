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

function New-EnvSetting {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value,
        [bool]$Secret = $false,
        [string]$Category = "General"
    )

    return [pscustomobject]@{
        Name = $Name
        Value = $Value
        Secret = $Secret
        Category = $Category
    }
}

function Get-MaskedValue {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Setting
    )

    if (-not $Setting.Secret) {
        return $Setting.Value
    }

    if ($Setting.Value -match '^<SET_ME') {
        return $Setting.Value
    }

    return "********"
}

function Set-MachineEnvSetting {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Setting
    )

    if ([string]::IsNullOrWhiteSpace($Setting.Value)) {
        Write-Warning ("Skipping {0} because the value is empty." -f $Setting.Name)
        return
    }

    if ($Setting.Value -match '^<SET_ME') {
        Write-Warning ("Skipping {0} because it still uses a placeholder value." -f $Setting.Name)
        return
    }

    [Environment]::SetEnvironmentVariable($Setting.Name, $Setting.Value, "Machine")
    Write-Host ("Set machine variable {0}" -f $Setting.Name)
}

$defaults = Get-EnvironmentDefaults -EnvironmentName $TargetEnvironment

$settings = @(
    New-EnvSetting -Name "IND_ENV" -Value $TargetEnvironment -Category "Core"
    New-EnvSetting -Name "INDCRM_AX_CONFIG_FILE" -Value $defaults.AxConfigFile -Category "AX"
    New-EnvSetting -Name "USER_DEFAULT" -Value "API_AXUSER" -Secret $true -Category "AX"
    New-EnvSetting -Name "USER_PASS_DEFAULT" -Value "<SET_ME_AX_PASSWORD>" -Secret $true -Category "AX"
    New-EnvSetting -Name "INDCRM_AX_VERBOSE_LOGGING" -Value $defaults.VerboseLogging -Category "AX"
    New-EnvSetting -Name "INDCRM_AX_VERBOSE_LOG_PATH" -Value "C:\INDAxaptaLogs" -Category "AX"
    New-EnvSetting -Name "INDCRM_AX_ALLOW_DEFAULT_CREDENTIALS" -Value "false" -Category "AX"
    New-EnvSetting -Name "INDCRM_BASE_URL" -Value $defaults.BaseUrl -Category "Host"
    New-EnvSetting -Name "INDCRM_PUBLIC_HOST" -Value $defaults.PublicHost -Category "Host"
    New-EnvSetting -Name "INDCRM_PUBLIC_IP" -Value $defaults.PublicIp -Category "Host"
    New-EnvSetting -Name "INDCRM_PUBLIC_PORT" -Value $defaults.PublicPort -Category "Host"
    New-EnvSetting -Name "INDCRM_CORS_ENABLED" -Value $defaults.CorsEnabled -Category "Host"
    New-EnvSetting -Name "INDCRM_CORS_ALLOWED_ORIGINS" -Value $defaults.CorsAllowedOrigins -Category "Host"
    New-EnvSetting -Name "INDCRM_LOG_LEVEL" -Value $defaults.LogLevel -Category "Host"
    New-EnvSetting -Name "INDCRM_LOG_PATH" -Value "C:\INDAxaptaLogs" -Category "Host"
    New-EnvSetting -Name "INDCRM_SERVICE_USER" -Value $defaults.ServiceUser -Category "Ops"
    New-EnvSetting -Name "INDCRM_HTTP_SERVICE_USER" -Value $defaults.HttpServiceUser -Category "Ops"
    New-EnvSetting -Name "INDCRM_JWT_ISSUER" -Value "IND_CRM_API" -Category "JWT"
    New-EnvSetting -Name "INDCRM_JWT_AUDIENCE" -Value "IND_CRM_APIUsers" -Category "JWT"
    New-EnvSetting -Name "JWT_SECRET_KEY" -Value "<SET_ME_JWT_SECRET_KEY>" -Secret $true -Category "JWT"
    New-EnvSetting -Name "INDCRM_JWT_EXPIRATION_MINUTES" -Value "60" -Category "JWT"
    New-EnvSetting -Name "INDCRM_JWT_REFRESH_THRESHOLD_MINUTES" -Value "5" -Category "JWT"
    New-EnvSetting -Name "OPENAI_API_KEY" -Value "<SET_ME_OPENAI_API_KEY>" -Secret $true -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_TRANSCRIPTION_DEFAULT_PROMPT_PATH" -Value "C:\INDAxaptaConfigAPI\Prompts\Wisper\prompt.txt" -Category "OpenAI"
    New-EnvSetting -Name "AZURE_BLOB_CONNECTION_STRING" -Value "<SET_ME_AZURE_BLOB_CONNECTION_STRING>" -Secret $true -Category "AzureBlob"
    New-EnvSetting -Name "AZURE_BLOB_CONTAINER" -Value "tickets" -Category "AzureBlob"
    New-EnvSetting -Name "AZURE_BLOB_ENVIRONMENT_SEGMENT" -Value $defaults.BlobSegment -Category "AzureBlob"
    New-EnvSetting -Name "AZURE_DOCS_IA_KEY" -Value "<SET_ME_AZURE_DOCS_IA_KEY>" -Secret $true -Category "AzureDocs"
    New-EnvSetting -Name "AZURE_DOCS_IA_ENDPOINT" -Value "<SET_ME_AZURE_DOCS_IA_ENDPOINT>" -Category "AzureDocs"
    New-EnvSetting -Name "AZURE_DOCS_IA_MODEL" -Value "<SET_ME_AZURE_DOCS_IA_MODEL>" -Category "AzureDocs"
    New-EnvSetting -Name "COMPANY_ACCESS_CACHE_MINUTES" -Value "20" -Category "Cache"
)

Write-Host ""
Write-Host ("IND CRM machine environment bootstrap for {0}" -f $TargetEnvironment)
Write-Host "Preview values:"
Write-Host ""

$settings |
    Sort-Object Category, Name |
    Select-Object Category, Name, @{ Name = "Value"; Expression = { Get-MaskedValue -Setting $_ } } |
    Format-Table -AutoSize

if (-not $Apply) {
    Write-Host ""
    Write-Host "Preview mode only. No machine variables were changed."
    Write-Host "Run the base bootstrap with -Apply, then load the critical values interactively:"
    Write-Host ""
    Write-Host ("powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-env.ps1 -TargetEnvironment {0} -Apply" -f $TargetEnvironment)
    Write-Host ("powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-critical-env.ps1 -TargetEnvironment {0} -Apply" -f $TargetEnvironment)
    Write-Host ""
    return
}

Write-Host ""
Write-Host "Applying machine environment variables..."
Write-Host ""

foreach ($setting in $settings) {
    Set-MachineEnvSetting -Setting $setting
}

Write-Host ""
Write-Host "Done."
Write-Host "Restart the API service so the new machine variables are loaded."
Write-Host ("Next, load the real critical values interactively with: powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-critical-env.ps1 -TargetEnvironment {0} -Apply" -f $TargetEnvironment)
Write-Host "Suggested command: Restart-Service IND_CRM_API"
