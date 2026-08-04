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
                BaseUrl = "https://dev.insertec.biz:2083/"
                PublicHost = "dev.insertec.biz"
                PublicIp = "192.168.0.148"
                PublicPort = "2083"
                WebBaseUrl = "https://dev.insertec.biz:2053/"
                WebPublicHost = "dev.insertec.biz"
                WebPublicPort = "2053"
                InternalApiBaseUrl = "https://dev.service.insertec.eu:2087/"
                PfxPath = "C:\INDAxaptaConfigAPI\dev.insertec.biz\dominio.pfx"
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
                WebBaseUrl = "https://crm.insertec.biz:7702/"
                WebPublicHost = "crm.insertec.biz"
                WebPublicPort = "7702"
                InternalApiBaseUrl = "https://prod.service.insertec.eu:2096/"
                PfxPath = "C:\INDAxaptaConfigAPI\crm.insertec.biz\dominio.pfx"
                BlobSegment = "PROD"
                ServiceUser = "INSERTEC\API_AXUSER"
                HttpServiceUser = "INSERTEC\API_AXUSER"
                VerboseLogging = "true"
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
        New-InteractiveSetting -Name "ASPNETCORE_ENVIRONMENT" -Category "Core" -DefaultValue $defaults.AspNetCoreEnvironment
        New-InteractiveSetting -Name "CRM_TENANT_ID" -Category "WebAuth"
        New-InteractiveSetting -Name "CRM_CLIENT_ID" -Category "WebAuth"
        New-InteractiveSetting -Name "CRM_CLIENT_SECRET" -Category "WebAuth" -Secret $true
        New-InteractiveSetting -Name "CRM_AUTHORITY" -Category "WebAuth"

        New-InteractiveSetting -Name "INDCRM_AX_CONFIG_FILE" -Category "AX" -DefaultValue $defaults.AxConfigFile
        New-InteractiveSetting -Name "USER_DEFAULT" -Category "AX" -DefaultValue "APIAX"
        New-InteractiveSetting -Name "USER_PASS_DEFAULT" -Category "AX" -Secret $true
        New-InteractiveSetting -Name "INDCRM_AX_VERBOSE_LOGGING" -Category "AX" -DefaultValue $defaults.VerboseLogging
        New-InteractiveSetting -Name "INDCRM_AX_VERBOSE_LOG_PATH" -Category "AX" -DefaultValue "C:\INDAxaptaLogs"
        New-InteractiveSetting -Name "INDCRM_AX_ALLOW_DEFAULT_CREDENTIALS" -Category "AX" -DefaultValue "false"
        New-InteractiveSetting -Name "AXAPTA_CALL_TIMEOUT_SECONDS" -Category "AX" -DefaultValue "3600"

        New-InteractiveSetting -Name "INDCRM_BASE_URL" -Category "Host" -DefaultValue $defaults.BaseUrl
        New-InteractiveSetting -Name "INDCRM_PUBLIC_HOST" -Category "Host" -DefaultValue $defaults.PublicHost
        New-InteractiveSetting -Name "INDCRM_PUBLIC_IP" -Category "Host" -DefaultValue $defaults.PublicIp
        New-InteractiveSetting -Name "INDCRM_PUBLIC_PORT" -Category "Host" -DefaultValue $defaults.PublicPort
        New-InteractiveSetting -Name "INDCRM_CORS_ENABLED" -Category "Host" -DefaultValue $defaults.CorsEnabled
        New-InteractiveSetting -Name "INDCRM_CORS_ALLOWED_ORIGINS" -Category "Host" -DefaultValue $defaults.CorsAllowedOrigins -Required $false
        New-InteractiveSetting -Name "INDCRM_LOG_LEVEL" -Category "Host" -DefaultValue $defaults.LogLevel
        New-InteractiveSetting -Name "INDCRM_LOG_PATH" -Category "Host" -DefaultValue "C:\INDAxaptaLogs"
        New-InteractiveSetting -Name "ApiSettings__BaseUrl" -Category "Web" -DefaultValue $defaults.BaseUrl
        New-InteractiveSetting -Name "INDCRM_WEB_BASE_URL" -Category "Web" -DefaultValue $defaults.WebBaseUrl
        New-InteractiveSetting -Name "INDCRM_WEB_PUBLIC_HOST" -Category "Web" -DefaultValue $defaults.WebPublicHost
        New-InteractiveSetting -Name "INDCRM_WEB_PUBLIC_PORT" -Category "Web" -DefaultValue $defaults.WebPublicPort
        New-InteractiveSetting -Name "IND_E2E_BASE_URL" -Category "Web" -DefaultValue $defaults.WebBaseUrl
        New-InteractiveSetting -Name "INDCRM_INTERNAL_API_BASE_URL" -Category "InternalApi" -DefaultValue $defaults.InternalApiBaseUrl -Required $false
        New-InteractiveSetting -Name "INDCRM_INTERNAL_API_CLIENT_ID" -Category "InternalApi" -Required $false
        New-InteractiveSetting -Name "INDCRM_INTERNAL_API_CLIENT_SECRET" -Category "InternalApi" -Secret $true -Required $false
        New-InteractiveSetting -Name "INDCRM_EXPENSE_NOTIFICATIONS_ENABLED" -Category "ExpenseNotifications" -DefaultValue "false"
        New-InteractiveSetting -Name "INDCRM_EXPENSE_NOTIFICATIONS_BEST_EFFORT" -Category "ExpenseNotifications" -DefaultValue "true"
        New-InteractiveSetting -Name "INDCRM_EXPENSE_NOTIFY_TRANSITIONS" -Category "ExpenseNotifications" -DefaultValue "ExpenseSheetApprovalRequested,ExpenseSheetApproved"
        New-InteractiveSetting -Name "INDCRM_SERVICE_USER" -Category "Ops" -DefaultValue $defaults.ServiceUser
        New-InteractiveSetting -Name "INDCRM_HTTP_SERVICE_USER" -Category "Ops" -DefaultValue $defaults.HttpServiceUser

        New-InteractiveSetting -Name "INDCRM_JWT_ISSUER" -Category "JWT" -DefaultValue "IND_CRM_API"
        New-InteractiveSetting -Name "INDCRM_JWT_AUDIENCE" -Category "JWT" -DefaultValue "IND_CRM_APIUsers"
        New-InteractiveSetting -Name "JWT_SECRET_KEY" -Category "JWT" -Secret $true
        New-InteractiveSetting -Name "INDCRM_JWT_EXPIRATION_MINUTES" -Category "JWT" -DefaultValue "60"
        New-InteractiveSetting -Name "INDCRM_JWT_REFRESH_THRESHOLD_MINUTES" -Category "JWT" -DefaultValue "5"
        New-InteractiveSetting -Name "INDCRM_CONTEXT_TOKEN_ISSUER" -Category "JWT" -DefaultValue "IND_CRM_CONTEXT"
        New-InteractiveSetting -Name "INDCRM_CONTEXT_TOKEN_AUDIENCE" -Category "JWT" -DefaultValue "IND_CRM_WEB_CONTEXT"
        New-InteractiveSetting -Name "INDCRM_CONTEXT_TOKEN_SECRET_KEY" -Category "JWT" -Secret $true -DefaultValue (New-RandomSecret) -Required $false
        New-InteractiveSetting -Name "INDCRM_SERVICE_PASSWORD" -Category "Ops" -Secret $true

        New-InteractiveSetting -Name "OPENAI_API_KEY" -Category "OpenAI" -Secret $true
        New-InteractiveSetting -Name "OPENAI_AUDIO_MODEL" -Category "OpenAI" -DefaultValue "gpt-4o-transcribe"
        New-InteractiveSetting -Name "OPENAI_TIMEOUT_SECONDS" -Category "OpenAI" -DefaultValue "600"
        New-InteractiveSetting -Name "OPENAI_MODERATION_MODEL" -Category "OpenAI" -DefaultValue "omni-moderation-latest"
        New-InteractiveSetting -Name "OPENAI_TRANSCRIPTION_PROMPT_MAX_WORDS" -Category "OpenAI" -DefaultValue "500"
        New-InteractiveSetting -Name "OPENAI_TRANSCRIPTION_DEFAULT_PROMPT_PATH" -Category "OpenAI" -DefaultValue "C:\INDAxaptaConfigAPI\Prompts\Wisper\prompt.txt"
        New-InteractiveSetting -Name "OPENAI_TRANSCRIPTION_DEFAULT_PROMPT" -Category "OpenAI" -Required $false
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_MODEL" -Category "OpenAI" -DefaultValue "gpt-5.4-nano"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_TIMEOUT_SECONDS" -Category "OpenAI" -DefaultValue "180"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_MAX_IMAGE_BYTES" -Category "OpenAI" -DefaultValue "52428800"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_MAX_OUTPUT_TOKENS" -Category "OpenAI" -DefaultValue "1024"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_IMAGE_DETAIL" -Category "OpenAI" -DefaultValue "high"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_SERVICE_TIER" -Category "OpenAI" -DefaultValue "priority"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_PROFILE_TAG" -Category "OpenAI" -DefaultValue "ticket-fast-v1"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_PROMPT_CACHE_KEY" -Category "OpenAI" -DefaultValue "expense-ticket-draft-v2"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_REASONING_EFFORT" -Category "OpenAI" -DefaultValue "low"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_QUICK_CREATE_MAX_OUTPUT_TOKENS" -Category "OpenAI" -DefaultValue "768"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_QUICK_CREATE_IMAGE_DETAIL" -Category "OpenAI" -DefaultValue "auto"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_QUICK_CREATE_SERVICE_TIER" -Category "OpenAI" -DefaultValue "priority"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_QUICK_CREATE_PROFILE_TAG" -Category "OpenAI" -DefaultValue "ticket-quick-create-v1"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_QUICK_CREATE_PROMPT_CACHE_KEY" -Category "OpenAI" -DefaultValue "expense-ticket-quick-create-v1"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_TICKET_QUICK_CREATE_REASONING_EFFORT" -Category "OpenAI" -DefaultValue "low"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_SHEET_ASK_MODEL" -Category "OpenAI" -DefaultValue "gpt-5.4-mini"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_SHEET_ASK_TIMEOUT_SECONDS" -Category "OpenAI" -DefaultValue "180"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_SHEET_ASK_MAX_OUTPUT_TOKENS" -Category "OpenAI" -DefaultValue "2200"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_SHEET_ASK_CHUNK_MAX_OUTPUT_TOKENS" -Category "OpenAI" -DefaultValue "1200"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_SHEET_ASK_DIRECT_RECORD_LIMIT" -Category "OpenAI" -DefaultValue "400"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_SHEET_ASK_CHUNK_SIZE" -Category "OpenAI" -DefaultValue "250"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_SHEET_ASK_MAX_CHUNKS" -Category "OpenAI" -DefaultValue "24"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_SHEET_ASK_SERVICE_TIER" -Category "OpenAI" -DefaultValue "priority"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_SHEET_ASK_PROFILE_TAG" -Category "OpenAI" -DefaultValue "dataset-answer-v1"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_SHEET_ASK_PROMPT_CACHE_KEY" -Category "OpenAI" -DefaultValue "dataset-answer-v1"
        New-InteractiveSetting -Name "OPENAI_EXPENSE_SHEET_ASK_REASONING_EFFORT" -Category "OpenAI" -DefaultValue "low"
        New-InteractiveSetting -Name "OPENAI_RATE_LIMIT_ENABLED" -Category "OpenAI" -DefaultValue "false"
        New-InteractiveSetting -Name "OPENAI_RATE_LIMIT_SPEECH_MAX_REQUESTS" -Category "OpenAI" -DefaultValue "5"
        New-InteractiveSetting -Name "OPENAI_RATE_LIMIT_SPEECH_WINDOW_SECONDS" -Category "OpenAI" -DefaultValue "300"
        New-InteractiveSetting -Name "OPENAI_RATE_LIMIT_EXPENSE_TICKET_MAX_REQUESTS" -Category "OpenAI" -DefaultValue "10"
        New-InteractiveSetting -Name "OPENAI_RATE_LIMIT_EXPENSE_TICKET_WINDOW_SECONDS" -Category "OpenAI" -DefaultValue "600"
        New-InteractiveSetting -Name "OPENAI_RATE_LIMIT_MAX_CONCURRENT_PER_USER" -Category "OpenAI" -DefaultValue "1"
        New-InteractiveSetting -Name "OPENAI_RATE_LIMIT_VALIDATION_MULTIPLIER" -Category "OpenAI" -DefaultValue "4"

        New-InteractiveSetting -Name "INDCRM_HELP_ENABLED" -Category "CRMHelp" -DefaultValue "false"
        New-InteractiveSetting -Name "INDCRM_HELP_KNOWLEDGE_BUNDLE_PATH" -Category "CRMHelp" -DefaultValue "Knowledge\crm-help.bundle.json"
        New-InteractiveSetting -Name "INDCRM_HELP_MODEL" -Category "CRMHelp" -DefaultValue "gpt-5.4-mini"
        New-InteractiveSetting -Name "INDCRM_HELP_REASONING_EFFORT" -Category "CRMHelp" -DefaultValue "low"
        New-InteractiveSetting -Name "INDCRM_HELP_PROMPT_CACHE_KEY" -Category "CRMHelp" -DefaultValue "crm-help-v1"
        New-InteractiveSetting -Name "INDCRM_HELP_TIMEOUT_SECONDS" -Category "CRMHelp" -DefaultValue "90"
        New-InteractiveSetting -Name "INDCRM_HELP_MAX_INPUT_TOKENS" -Category "CRMHelp" -DefaultValue "18000"
        New-InteractiveSetting -Name "INDCRM_HELP_MIN_DOCUMENT_TOKENS" -Category "CRMHelp" -DefaultValue "4000"
        New-InteractiveSetting -Name "INDCRM_HELP_MAX_DOCUMENT_TOKENS" -Category "CRMHelp" -DefaultValue "12000"
        New-InteractiveSetting -Name "INDCRM_HELP_MIN_OUTPUT_TOKENS" -Category "CRMHelp" -DefaultValue "1600"
        New-InteractiveSetting -Name "INDCRM_HELP_MAX_OUTPUT_TOKENS" -Category "CRMHelp" -DefaultValue "3200"
        New-InteractiveSetting -Name "INDCRM_HELP_MAX_HISTORY_MESSAGES" -Category "CRMHelp" -DefaultValue "8"
        New-InteractiveSetting -Name "INDCRM_HELP_RATE_LIMIT_MAX_REQUESTS" -Category "CRMHelp" -DefaultValue "20"
        New-InteractiveSetting -Name "INDCRM_HELP_RATE_LIMIT_WINDOW_SECONDS" -Category "CRMHelp" -DefaultValue "600"
        New-InteractiveSetting -Name "INDCRM_HELP_FEEDBACK_HMAC_SECRET" -Category "CRMHelp" -Secret $true -DefaultValue (New-RandomSecret) -Required $false
        New-InteractiveSetting -Name "INDCRM_HELP_FEEDBACK_TOKEN_MINUTES" -Category "CRMHelp" -DefaultValue "60"
        New-InteractiveSetting -Name "INDCRM_HELP_ANALYTICS_PATH" -Category "CRMHelp" -DefaultValue "C:\INDData\CRMHelpAnalytics"
        New-InteractiveSetting -Name "INDCRM_HELP_ANALYTICS_HMAC_SECRET" -Category "CRMHelp" -Secret $true -DefaultValue (New-RandomSecret) -Required $false
        New-InteractiveSetting -Name "INDCRM_HELP_ANALYTICS_TEXT_ENABLED" -Category "CRMHelp" -DefaultValue "false"
        New-InteractiveSetting -Name "INDCRM_HELP_ANALYTICS_ACL_READY" -Category "CRMHelp" -DefaultValue "false"
        New-InteractiveSetting -Name "INDCRM_HELP_ANALYTICS_VOLUME_ENCRYPTED" -Category "CRMHelp" -DefaultValue "false"
        New-InteractiveSetting -Name "INDCRM_HELP_ANALYTICS_SUCCESS_SAMPLE_PERCENT" -Category "CRMHelp" -DefaultValue "10"
        New-InteractiveSetting -Name "INDCRM_HELP_ANALYTICS_SUCCESS_SAMPLE_MAX_PER_DAY" -Category "CRMHelp" -DefaultValue "200"
        New-InteractiveSetting -Name "INDCRM_HELP_ANALYTICS_TEXT_RETENTION_DAYS" -Category "CRMHelp" -DefaultValue "90"
        New-InteractiveSetting -Name "INDCRM_HELP_ANALYTICS_METRIC_RETENTION_DAYS" -Category "CRMHelp" -DefaultValue "180"
        New-InteractiveSetting -Name "INDCRM_HELP_ANALYTICS_AGGREGATE_RETENTION_DAYS" -Category "CRMHelp" -DefaultValue "730"
        New-InteractiveSetting -Name ("INDCRM_" + $EnvironmentName + "_PFX_PATH") -Category "HTTPS" -DefaultValue $defaults.PfxPath -Required $false
        New-InteractiveSetting -Name ("INDCRM_" + $EnvironmentName + "_PFX_PASSWORD") -Category "HTTPS" -Secret $true -Required $false

        New-InteractiveSetting -Name "AZURE_BLOB_CONNECTION_STRING" -Category "AzureBlob" -Secret $true
        New-InteractiveSetting -Name "AZURE_BLOB_CONTAINER" -Category "AzureBlob" -DefaultValue "tickets"
        New-InteractiveSetting -Name "AZURE_BLOB_ENVIRONMENT_SEGMENT" -Category "AzureBlob" -DefaultValue $defaults.BlobSegment

        New-InteractiveSetting -Name "AZURE_DOCS_IA_KEY" -Category "AzureDocs" -Secret $true
        New-InteractiveSetting -Name "AZURE_DOCS_IA_ENDPOINT" -Category "AzureDocs" -DefaultValue "https://westeurope.api.cognitive.microsoft.com/"
        New-InteractiveSetting -Name "AZURE_DOCS_IA_MODEL" -Category "AzureDocs" -DefaultValue "prebuilt-receipt"
        New-InteractiveSetting -Name "AZURE_DOCS_IA_API_VERSION" -Category "AzureDocs" -DefaultValue "2023-07-31"
        New-InteractiveSetting -Name "AZURE_DOCS_IA_POLL_INTERVAL_MS" -Category "AzureDocs" -DefaultValue "1000"
        New-InteractiveSetting -Name "AZURE_DOCS_IA_TIMEOUT_SECONDS" -Category "AzureDocs" -DefaultValue "120"
        New-InteractiveSetting -Name "AZURE_DOCS_IA_BLOB_READ_SAS_MINUTES" -Category "AzureDocs" -DefaultValue "15"

        New-InteractiveSetting -Name "EXCHANGE_RATE_ECB_TIMEOUT_SECONDS" -Category "ExchangeRate" -DefaultValue "5"
        New-InteractiveSetting -Name "EXCHANGE_RATE_FRANKFURTER_TIMEOUT_SECONDS" -Category "ExchangeRate" -DefaultValue "5"
        New-InteractiveSetting -Name "EXCHANGE_RATE_OPEN_ER_API_TIMEOUT_SECONDS" -Category "ExchangeRate" -DefaultValue "5"
        New-InteractiveSetting -Name "COMPANY_ACCESS_CACHE_MINUTES" -Category "Cache" -DefaultValue "20"
        New-InteractiveSetting -Name "CLIENT_SETTINGS_PROVIDER_SERVICE_URI" -Category "Client" -Required $false
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

    if (-not $Setting.Required) {
        return "<empty>"
    }

    return "<empty>"
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

function Assert-ResolvedEnvironmentSettings {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$ResolvedSettings,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentName
    )

    $expectedAspNetCoreEnvironment = if ($EnvironmentName -eq "DEV") { "Development" } else { "Production" }
    $expectedAxConfigFileName = if ($EnvironmentName -eq "DEV") { "CRM_API_AxConfig_DEV.axc" } else { "CRM_API_AxConfig_PROD.axc" }

    Assert-ResolvedSettingValue -Name "IND_ENV" -Actual (Get-ResolvedSettingValue -ResolvedSettings $ResolvedSettings -Name "IND_ENV") -Expected $EnvironmentName
    Assert-ResolvedSettingValue -Name "ASPNETCORE_ENVIRONMENT" -Actual (Get-ResolvedSettingValue -ResolvedSettings $ResolvedSettings -Name "ASPNETCORE_ENVIRONMENT") -Expected $expectedAspNetCoreEnvironment
    Assert-ResolvedSettingValue -Name "AZURE_BLOB_ENVIRONMENT_SEGMENT" -Actual (Get-ResolvedSettingValue -ResolvedSettings $ResolvedSettings -Name "AZURE_BLOB_ENVIRONMENT_SEGMENT") -Expected $EnvironmentName

    $axConfigFile = Get-ResolvedSettingValue -ResolvedSettings $ResolvedSettings -Name "INDCRM_AX_CONFIG_FILE"
    $actualAxConfigFileName = if ([string]::IsNullOrWhiteSpace($axConfigFile)) { $null } else { [System.IO.Path]::GetFileName($axConfigFile.Trim()) }
    Assert-ResolvedSettingValue -Name "INDCRM_AX_CONFIG_FILE" -Actual $actualAxConfigFileName -Expected $expectedAxConfigFileName
}

$settings = Get-AllSettings -EnvironmentName $TargetEnvironment

if (-not $Apply) {
    Write-Host ""
    Write-Host ("IND CRM full machine environment helper for {0}" -f $TargetEnvironment)
    Write-Host "Preview values:"
    Write-Host ""

    $settings |
        Sort-Object Category, Name |
        Select-Object Category, Name, @{ Name = "CurrentMachine"; Expression = { Get-PreviewValue -Setting $_ } }, @{ Name = ("Default" + $TargetEnvironment); Expression = { Get-DefaultPreviewValue -Setting $_ } } |
        Format-Table -AutoSize

    Write-Host ""
    Write-Host "Preview mode only. No machine variables were changed."
    Write-Host "CurrentMachine shows the real machine value. Default columns show what the helper would propose for the target environment."
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

Assert-ResolvedEnvironmentSettings -ResolvedSettings $resolvedSettings -EnvironmentName $TargetEnvironment

foreach ($resolvedSetting in $resolvedSettings) {
    Set-MachineEnvSetting -Name $resolvedSetting.Name -Value $resolvedSetting.Value
}

Write-Host ""
Write-Host "Done."
Write-Host "Restart the API service so the new machine variables are loaded."
Write-Host ("Reminder: IND_CRM_APP should run with ASPNETCORE_ENVIRONMENT={0} on the target machine." -f $defaults.AspNetCoreEnvironment)
Write-Host "Suggested command: Restart-Service IND_CRM_API"
