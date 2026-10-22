param([string]$AssemblyPath)

$ErrorActionPreference = 'Stop'
if (-not $AssemblyPath) { $AssemblyPath = Join-Path $PSScriptRoot '..\bin\x86\Release\IND_CRM_API.exe' }
$AssemblyPath = [IO.Path]::GetFullPath($AssemblyPath)
if ([IntPtr]::Size -ne 4) {
    & "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -AssemblyPath $AssemblyPath
    exit $LASTEXITCODE
}

# Validate production request builders without starting the host or calling a provider.
function Assert-Contract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

# Structured Outputs requires every object property to be required and no extra properties.
function Assert-StrictSchema {
    param($Schema)
    if ($Schema.type -eq 'object') {
        Assert-Contract ($Schema.additionalProperties -eq $false) 'Schema allows extra properties.'
        $names = @($Schema.properties.PSObject.Properties.Name)
        Assert-Contract (@(Compare-Object $names @($Schema.required)).Count -eq 0) 'Schema required fields differ from properties.'
        foreach ($property in $Schema.properties.PSObject.Properties) { Assert-StrictSchema $property.Value }
    }
    if ($Schema.items) { Assert-StrictSchema $Schema.items }
}

Push-Location (Split-Path -Parent $AssemblyPath)
try {
    Add-Type -AssemblyName System.Net.Http
    $assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
    $staticFlags = [Reflection.BindingFlags]'NonPublic,Static'
    $audio = $assembly.GetType('IND_CRM_API.Services.IND_OpenAiAudioTranscriptionService', $true)
    $hintMethod = $audio.GetMethod('AddLanguageHint', $staticFlags)
    foreach ($case in @(
        @{ Model='gpt-transcribe'; Language='es'; Field='languages[]' },
        @{ Model='gpt-transcribe-2026-10-15'; Language='en'; Field='languages[]' },
        @{ Model='gpt-4o-transcribe'; Language='es'; Field='language' },
        @{ Model='whisper-1'; Language='pt'; Field='language' },
        @{ Model='gpt-transcribe'; Language='auto'; Field=$null },
        @{ Model='gpt-4o-transcribe'; Language='AUTO'; Field=$null }
    )) {
        $form = New-Object Net.Http.MultipartFormDataContent
        try {
            $hintMethod.Invoke($null, [object[]]@($form.PSObject.BaseObject, $case.Model, $case.Language)) | Out-Null
            $parts = @($form)
            if ($null -eq $case.Field) { Assert-Contract ($parts.Count -eq 0) 'Auto must omit language hints.' }
            else {
                Assert-Contract ($parts.Count -eq 1) 'Expected one language hint.'
                Assert-Contract ($parts[0].Headers.ContentDisposition.Name.Trim('"') -eq $case.Field) 'Incorrect multipart language field.'
                Assert-Contract ($parts[0].ReadAsStringAsync().GetAwaiter().GetResult() -eq $case.Language) 'Language hint changed.'
            }
        } finally { $form.Dispose() }
    }

    $ticket = $assembly.GetType('IND_CRM_API.Services.IND_OpenAiExpenseTicketDraftService', $true)
    $optionsType = $ticket.GetNestedType('ExpenseTicketRequestOptions', [Reflection.BindingFlags]::NonPublic)
    $profileType = $assembly.GetType('IND_CRM_API.Services.Interfaces.ExpenseTicketDraftProfile', $true)
    foreach ($profile in @('FullDraft', 'QuickCreate')) {
        $options = [Activator]::CreateInstance($optionsType, $true)
        $options.Model = 'gpt-6-luna'
        $options.DraftProfile = [Enum]::Parse($profileType, $profile)
        $options.ImageDetail = 'auto'
        $options.MaxOutputTokens = 4000
        $options.ProfileTag = 'contract-test'
        $options.ReasoningEffort = 'low'
        foreach ($method in @('BuildTextPayloadJson', 'BuildPayloadJson')) {
            $arguments = if ($method -eq 'BuildTextPayloadJson') { [object[]]@('Normalize this receipt.', '{"total":12.50,"currency":"EUR"}', 'fixture.txt', $options) }
                else { [object[]]@('AA==', 'image/png', 'fixture.png', 'Normalize this receipt.', $options) }
            $payload = $ticket.GetMethod($method, $staticFlags).Invoke($null, $arguments) | ConvertFrom-Json
            Assert-Contract ($payload.model -eq 'gpt-6-luna') 'Incorrect ticket model.'
            Assert-Contract ($payload.reasoning.effort -eq 'low') 'Reasoning effort changed.'
            Assert-Contract ($payload.text.format.strict -eq $true) 'Strict output disabled.'
            Assert-Contract ($null -eq $payload.temperature -and $null -eq $payload.top_p) 'Sampling controls conflict with reasoning.'
            Assert-StrictSchema $payload.text.format.schema
        }
    }
    $formatting = $assembly.GetType('IND_CRM_API.Services.IND_OpenAiTextFormattingService', $true)
    $schema = $formatting.GetMethod('BuildSchema', $staticFlags).Invoke($null, @()).ToString() | ConvertFrom-Json
    Assert-StrictSchema $schema
    Write-Host 'PASS: transcription language compatibility and strict Responses schemas for both ticket profiles and formatting.'
} finally { Pop-Location }
