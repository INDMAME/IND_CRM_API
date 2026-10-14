param([string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path $PSScriptRoot '..\bin\x86\Release\IND_CRM_API.exe'
}
$AssemblyPath = [IO.Path]::GetFullPath($AssemblyPath)
if ([IntPtr]::Size -ne 4) {
    & "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -AssemblyPath $AssemblyPath
    exit $LASTEXITCODE
}
Add-Type -AssemblyName System.Net.Http
$assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$privateInstance = [Reflection.BindingFlags]'Instance,NonPublic'
$privateStatic = [Reflection.BindingFlags]'Static,NonPublic'
$script:checks = 0

# Assert contract values without making any external requests.
function Assert-Equal {
    param($Actual, $Expected, [string]$Message)
    if ($Actual -cne $Expected) { throw "$Message Expected=[$Expected] Actual=[$Actual]" }
    $script:checks++
}

$expectedDefaults = @{
    IND_OpenAiAudioTranscriptionService = 'gpt-transcribe'
    IND_OpenAiExpenseTicketDraftService = 'gpt-6-luna'
    IND_OpenAiTextFormattingService = 'gpt-5.6-terra'
    IND_OpenAiDatasetAnswerService = 'gpt-5.4-mini'
}
foreach ($serviceName in $expectedDefaults.Keys) {
    $type = $assembly.GetType('IND_CRM_API.Services.' + $serviceName, $true)
    Assert-Equal $type.GetField('DefaultModel', $privateStatic).GetRawConstantValue() $expectedDefaults[$serviceName] "$serviceName fallback"
}

$audioType = $assembly.GetType('IND_CRM_API.Services.IND_OpenAiAudioTranscriptionService', $true)
$audioService = [Activator]::CreateInstance($audioType, [object[]]@($null))
$modelField = $audioType.GetField('_model', $privateInstance)
$languageMethod = $audioType.GetMethod('AddLanguageHint', $privateInstance)
foreach ($model in @('gpt-transcribe', 'gpt-transcribe-2026-10-01', 'custom-transcription-model')) {
    $modelField.SetValue($audioService, $model)
    foreach ($language in @('es', 'en', 'auto', 'AUTO')) {
        $form = New-Object Net.Http.MultipartFormDataContent
        try {
            $arguments = New-Object 'Object[]' 2
            $arguments[0] = $form.PSObject.BaseObject
            $arguments[1] = $language
            $languageMethod.Invoke($audioService, $arguments) | Out-Null
            $parts = @($form)
            if ($language -ieq 'auto') {
                Assert-Equal $parts.Count 0 'Automatic language detection must omit hints'
            } else {
                Assert-Equal $parts.Count 1 'Send exactly one language field'
                $expectedName = if ($model.StartsWith('gpt-transcribe')) { 'languages[]' } else { 'language' }
                Assert-Equal $parts[0].Headers.ContentDisposition.Name.Trim('"') $expectedName 'Model-specific language field'
                Assert-Equal $parts[0].ReadAsStringAsync().GetAwaiter().GetResult() $language 'Preserve the requested language'
            }
        } finally { $form.Dispose() }
    }
}

$parseMethod = $audioType.GetMethod('TryExtractText', $privateStatic)
Assert-Equal $parseMethod.Invoke($null, [object[]]@('{"text":"Test transcript","languages":[{"code":"es"}]}')) 'Test transcript' 'Ignore additional transcription metadata'
$datasetType = $assembly.GetType('IND_CRM_API.Services.IND_OpenAiDatasetAnswerService', $true)
Assert-Equal $datasetType.GetField('DefaultReasoningEffort', $privateStatic).GetRawConstantValue() 'low' 'Use a supported fallback reasoning effort'
Write-Host "$script:checks model migration contract checks passed."
