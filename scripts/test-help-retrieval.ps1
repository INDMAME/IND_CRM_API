<#
.SYNOPSIS
Runs the production CRM help retriever against versioned evaluation cases.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$BundlePath,

    [Parameter(Mandatory = $true)]
    [string]$CasesPath,

    [string]$AssemblyPath,

    [double]$MinimumTop1 = 0.90,

    [double]$MinimumRecallAt5 = 0.95
)

$ErrorActionPreference = 'Stop'
$scriptFilePath = $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($scriptFilePath)) {
    throw 'The retrieval test script path could not be resolved.'
}
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path (Split-Path -Parent $scriptFilePath) '..\bin\x86\Debug\IND_CRM_API.exe'
}

# The API is built for x86, so relaunch under the matching Windows PowerShell host when needed.
if ([System.IntPtr]::Size -ne 4) {
    $x86PowerShell = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $x86PowerShell)) {
        throw "The x86 Windows PowerShell host was not found: $x86PowerShell"
    }
    & $x86PowerShell -NoProfile -ExecutionPolicy Bypass -File $scriptFilePath `
        -BundlePath $BundlePath `
        -CasesPath $CasesPath `
        -AssemblyPath $AssemblyPath `
        -MinimumTop1 $MinimumTop1 `
        -MinimumRecallAt5 $MinimumRecallAt5
    exit $LASTEXITCODE
}

$bundle = (Resolve-Path -LiteralPath $BundlePath).Path
$cases = (Resolve-Path -LiteralPath $CasesPath).Path
$assembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$assemblyDirectory = Split-Path -Parent $assembly

$resolver = [System.ResolveEventHandler]{
    param($sender, $eventArgs)
    $name = ([System.Reflection.AssemblyName]$eventArgs.Name).Name + '.dll'
    $candidate = Join-Path $assemblyDirectory $name
    if (Test-Path -LiteralPath $candidate) {
        return [System.Reflection.Assembly]::LoadFrom($candidate)
    }
    return $null
}
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)

try {
    [void][System.Reflection.Assembly]::LoadFrom($assembly)
    $snapshot = [IND_CRM_API.Services.HelpKnowledgeStore]::LoadForValidation($bundle)
    $retriever = [IND_CRM_API.Services.HelpTopicRetriever]::new()
    # Read UTF-8 explicitly because Windows PowerShell 5.1 treats BOM-less files as ANSI.
    $parsedEvaluationCases = [System.IO.File]::ReadAllText($cases, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $evaluationCases = @($parsedEvaluationCases)
    if ($evaluationCases.Count -eq 0) {
        throw 'No retrieval evaluation cases were found.'
    }

    $results = foreach ($case in $evaluationCases) {
        $question = if ($case.question) { [string]$case.question } else { [string]$case.query }
        $expectedIds = @()
        if ($case.expectedTopicIds) { $expectedIds = @($case.expectedTopicIds | ForEach-Object { [string]$_ }) }
        elseif ($case.expectedTopicId) { $expectedIds = @([string]$case.expectedTopicId) }
        $expectedResolution = if ($case.expectedResolution) { [string]$case.expectedResolution } else { 'answered' }
        $locale = if ($case.responseLocale) { [string]$case.responseLocale } else { 'es-ES' }

        $request = [IND_CRM_API.Services.HelpRetrievalRequest]::new()
        $request.Question = $question
        $request.SelectedTopicId = [string]$case.selectedTopicId
        $request.ResponseLocale = $locale
        try {
            $result = $retriever.Retrieve($snapshot, $request)
        }
        catch {
            throw "Retrieval failed for case '$([string]$case.id)': $($_.Exception.ToString())"
        }
        $actualIds = @($result.Ranking | ForEach-Object { $_.Topic.id })

        $top1 = $expectedIds.Count -eq 0 -or ($actualIds.Count -gt 0 -and $expectedIds -contains $actualIds[0])
        $recall = $expectedIds.Count -eq 0 -or @($expectedIds | Where-Object { $actualIds -contains $_ }).Count -eq $expectedIds.Count
        [pscustomobject]@{
            Id = [string]$case.id
            ExpectedResolution = $expectedResolution
            ActualResolution = $result.Resolution
            ExpectedTopicIds = ($expectedIds -join '|')
            ActualTopicIds = ($actualIds -join '|')
            ResolutionPassed = $result.Resolution -eq $expectedResolution
            Top1Passed = $top1
            RecallAt5Passed = $recall
        }
    }

    $count = @($results).Count
    if ($count -eq 0) {
        throw 'The retriever produced no evaluation results.'
    }
    $resolutionRate = @($results | Where-Object ResolutionPassed).Count / $count
    $topicCases = @($results | Where-Object { -not [string]::IsNullOrWhiteSpace($_.ExpectedTopicIds) })
    $top1Rate = @($topicCases | Where-Object Top1Passed).Count / [math]::Max(1, $topicCases.Count)
    $recallRate = @($topicCases | Where-Object RecallAt5Passed).Count / [math]::Max(1, $topicCases.Count)

    $menuExactResults = foreach ($topic in $snapshot.Bundle.topics) {
        $request = [IND_CRM_API.Services.HelpRetrievalRequest]::new()
        $request.Question = ''
        $request.SelectedTopicId = $topic.id
        $request.ResponseLocale = $snapshot.Bundle.defaultLocale
        $result = $retriever.Retrieve($snapshot, $request)
        $topicIds = @($result.Topics | ForEach-Object { $_.Topic.id })
        $rankingIds = @($result.Ranking | ForEach-Object { $_.Topic.id })
        [pscustomobject]@{
            TopicId = $topic.id
            Passed = $result.Resolution -eq 'answered' -and
                $topicIds.Count -eq 1 -and $topicIds[0] -eq $topic.id -and
                $rankingIds.Count -eq 1 -and $rankingIds[0] -eq $topic.id
        }
    }
    $menuExactCount = @($menuExactResults).Count
    $menuExactPassedCount = @($menuExactResults | Where-Object Passed).Count
    $menuExactRate = $menuExactPassedCount / [math]::Max(1, $menuExactCount)

    $missingRequest = [IND_CRM_API.Services.HelpRetrievalRequest]::new()
    $missingRequest.Question = ''
    $missingRequest.SelectedTopicId = '__missing-help-topic__'
    $missingRequest.ResponseLocale = $snapshot.Bundle.defaultLocale
    $missingResult = $retriever.Retrieve($snapshot, $missingRequest)
    $missingTopicPassed = $missingResult.Resolution -eq 'notDocumented' -and
        @($missingResult.Topics).Count -eq 0 -and @($missingResult.Ranking).Count -eq 0

    $results | Format-Table -AutoSize
    Write-Host ('Cases={0} TopicCases={1} Resolution={2:P2} Top1={3:P2} RecallAt5={4:P2}' -f $count, $topicCases.Count, $resolutionRate, $top1Rate, $recallRate)
    Write-Host ('MenuExact={0:P2} ({1}/{2}) MissingTopic={3}' -f `
        $menuExactRate, $menuExactPassedCount, $menuExactCount, $(if ($missingTopicPassed) { 'Passed' } else { 'Failed' }))

    if ($resolutionRate -lt 1.0 -or $top1Rate -lt $MinimumTop1 -or $recallRate -lt $MinimumRecallAt5 -or
        $menuExactRate -lt 1.0 -or -not $missingTopicPassed) {
        Write-Host 'Failed cases:'
        $results |
            Where-Object { -not $_.ResolutionPassed -or (-not [string]::IsNullOrWhiteSpace($_.ExpectedTopicIds) -and (-not $_.Top1Passed -or -not $_.RecallAt5Passed)) } |
            ForEach-Object {
                Write-Host ('- {0}: resolution {1}->{2}; expected [{3}]; actual [{4}]' -f `
                    $_.Id, $_.ExpectedResolution, $_.ActualResolution, $_.ExpectedTopicIds, $_.ActualTopicIds)
            }
        $menuExactResults | Where-Object { -not $_.Passed } | ForEach-Object {
            Write-Host ('- MenuExact failed: {0}' -f $_.TopicId)
        }
        if (-not $missingTopicPassed) {
            Write-Host ('- MissingTopic failed: resolution={0}' -f $missingResult.Resolution)
        }
        exit 1
    }
}
finally {
    [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
