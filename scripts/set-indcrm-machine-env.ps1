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
                AspNetCoreEnvironment = "Development"
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
                CorsEnabled = "false"
                CorsAllowedOrigins = ""
            }
        }
        default {
            return @{
                AspNetCoreEnvironment = "Production"
                AxConfigFile = "C:\INDAxaptaConfigAPI\CRM_API_AxConfig_PROD.axc"
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
    New-EnvSetting -Name "ASPNETCORE_ENVIRONMENT" -Value $defaults.AspNetCoreEnvironment -Category "Core"
    New-EnvSetting -Name "INDCRM_AX_CONFIG_FILE" -Value $defaults.AxConfigFile -Category "AX"
    New-EnvSetting -Name "USER_DEFAULT" -Value "API_AXUSER" -Secret $true -Category "AX"
    New-EnvSetting -Name "USER_PASS_DEFAULT" -Value "<SET_ME_AX_PASSWORD>" -Secret $true -Category "AX"
    New-EnvSetting -Name "INDCRM_AX_VERBOSE_LOGGING" -Value $defaults.VerboseLogging -Category "AX"
    New-EnvSetting -Name "INDCRM_AX_VERBOSE_LOG_PATH" -Value "C:\INDAxaptaLogs" -Category "AX"
    New-EnvSetting -Name "INDCRM_AX_ALLOW_DEFAULT_CREDENTIALS" -Value "false" -Category "AX"
    New-EnvSetting -Name "AXAPTA_CALL_TIMEOUT_SECONDS" -Value "3600" -Category "AX"
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
    New-EnvSetting -Name "OPENAI_AUDIO_MODEL" -Value "gpt-4o-transcribe" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_TIMEOUT_SECONDS" -Value "600" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_MODERATION_MODEL" -Value "omni-moderation-latest" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_TRANSCRIPTION_PROMPT_MAX_WORDS" -Value "500" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_TRANSCRIPTION_DEFAULT_PROMPT_PATH" -Value "C:\INDAxaptaConfigAPI\Prompts\Wisper\prompt.txt" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_MODEL" -Value "gpt-5-nano" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_TIMEOUT_SECONDS" -Value "180" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_MAX_IMAGE_BYTES" -Value "52428800" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_MAX_OUTPUT_TOKENS" -Value "1024" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_IMAGE_DETAIL" -Value "high" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_SERVICE_TIER" -Value "priority" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_PROFILE_TAG" -Value "ticket-fast-v1" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_PROMPT_CACHE_KEY" -Value "expense-ticket-draft-v2" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_REASONING_EFFORT" -Value "low" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_QUICK_CREATE_MAX_OUTPUT_TOKENS" -Value "768" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_QUICK_CREATE_IMAGE_DETAIL" -Value "auto" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_QUICK_CREATE_SERVICE_TIER" -Value "priority" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_QUICK_CREATE_PROFILE_TAG" -Value "ticket-quick-create-v1" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_QUICK_CREATE_PROMPT_CACHE_KEY" -Value "expense-ticket-quick-create-v1" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_TICKET_QUICK_CREATE_REASONING_EFFORT" -Value "low" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_SHEET_ASK_MODEL" -Value "gpt-5-mini" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_SHEET_ASK_TIMEOUT_SECONDS" -Value "180" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_SHEET_ASK_MAX_OUTPUT_TOKENS" -Value "1200" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_SHEET_ASK_CHUNK_MAX_OUTPUT_TOKENS" -Value "700" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_SHEET_ASK_DIRECT_RECORD_LIMIT" -Value "400" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_SHEET_ASK_CHUNK_SIZE" -Value "250" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_SHEET_ASK_MAX_CHUNKS" -Value "24" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_SHEET_ASK_SERVICE_TIER" -Value "priority" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_SHEET_ASK_PROFILE_TAG" -Value "dataset-answer-v1" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_SHEET_ASK_PROMPT_CACHE_KEY" -Value "dataset-answer-v1" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_EXPENSE_SHEET_ASK_REASONING_EFFORT" -Value "minimal" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_RATE_LIMIT_ENABLED" -Value "false" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_RATE_LIMIT_SPEECH_MAX_REQUESTS" -Value "5" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_RATE_LIMIT_SPEECH_WINDOW_SECONDS" -Value "300" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_RATE_LIMIT_EXPENSE_TICKET_MAX_REQUESTS" -Value "10" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_RATE_LIMIT_EXPENSE_TICKET_WINDOW_SECONDS" -Value "600" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_RATE_LIMIT_MAX_CONCURRENT_PER_USER" -Value "1" -Category "OpenAI"
    New-EnvSetting -Name "OPENAI_RATE_LIMIT_VALIDATION_MULTIPLIER" -Value "4" -Category "OpenAI"
    New-EnvSetting -Name "AZURE_BLOB_CONNECTION_STRING" -Value "<SET_ME_AZURE_BLOB_CONNECTION_STRING>" -Secret $true -Category "AzureBlob"
    New-EnvSetting -Name "AZURE_BLOB_CONTAINER" -Value "tickets" -Category "AzureBlob"
    New-EnvSetting -Name "AZURE_BLOB_ENVIRONMENT_SEGMENT" -Value $defaults.BlobSegment -Category "AzureBlob"
    New-EnvSetting -Name "AZURE_DOCS_IA_KEY" -Value "<SET_ME_AZURE_DOCS_IA_KEY>" -Secret $true -Category "AzureDocs"
    New-EnvSetting -Name "AZURE_DOCS_IA_ENDPOINT" -Value "<SET_ME_AZURE_DOCS_IA_ENDPOINT>" -Category "AzureDocs"
    New-EnvSetting -Name "AZURE_DOCS_IA_MODEL" -Value "<SET_ME_AZURE_DOCS_IA_MODEL>" -Category "AzureDocs"
    New-EnvSetting -Name "AZURE_DOCS_IA_API_VERSION" -Value "2023-07-31" -Category "AzureDocs"
    New-EnvSetting -Name "AZURE_DOCS_IA_POLL_INTERVAL_MS" -Value "1000" -Category "AzureDocs"
    New-EnvSetting -Name "AZURE_DOCS_IA_TIMEOUT_SECONDS" -Value "120" -Category "AzureDocs"
    New-EnvSetting -Name "AZURE_DOCS_IA_BLOB_READ_SAS_MINUTES" -Value "15" -Category "AzureDocs"
    New-EnvSetting -Name "EXCHANGE_RATE_ECB_TIMEOUT_SECONDS" -Value "5" -Category "ExchangeRate"
    New-EnvSetting -Name "EXCHANGE_RATE_FRANKFURTER_TIMEOUT_SECONDS" -Value "5" -Category "ExchangeRate"
    New-EnvSetting -Name "EXCHANGE_RATE_OPEN_ER_API_TIMEOUT_SECONDS" -Value "5" -Category "ExchangeRate"
    New-EnvSetting -Name "COMPANY_ACCESS_CACHE_MINUTES" -Value "20" -Category "Cache"
)

Write-Host ""
Write-Host ("IND CRM machine environment bootstrap for {0}" -f $TargetEnvironment)
Write-Host "Target values for bootstrap:"
Write-Host ""

$settings |
    Sort-Object Category, Name |
    Select-Object Category, Name, @{ Name = "Value"; Expression = { Get-MaskedValue -Setting $_ } } |
    Format-Table -AutoSize

if (-not $Apply) {
    Write-Host ""
    Write-Host "Preview mode only. No machine variables were changed."
    Write-Host "This preview shows the target values that would be written for the selected environment, not the current machine state."
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
Write-Host ("Reminder: IND_CRM_APP should run with ASPNETCORE_ENVIRONMENT={0} on the target machine." -f $defaults.AspNetCoreEnvironment)
Write-Host ("Next, load the real critical values interactively with: powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-critical-env.ps1 -TargetEnvironment {0} -Apply" -f $TargetEnvironment)
Write-Host "Suggested command: Restart-Service IND_CRM_API"
