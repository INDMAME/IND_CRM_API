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
        $request.SelectedModuleId = [string]$case.selectedModuleId
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

    $moduleScopeResults = foreach ($module in $snapshot.Bundle.modules) {
        $firstTopicId = @($module.topicIds | Where-Object { $snapshot.TopicsById.ContainsKey($_) } | Select-Object -First 1)
        if ($firstTopicId.Count -eq 0) {
            continue
        }
        $firstTopic = $snapshot.TopicsById[$firstTopicId[0]]
        $request = [IND_CRM_API.Services.HelpRetrievalRequest]::new()
        $request.Question = $firstTopic.title
        $request.SelectedModuleId = $module.id
        $request.ResponseLocale = $snapshot.Bundle.defaultLocale
        $result = $retriever.Retrieve($snapshot, $request)
        $outsideTopics = @($result.Topics | Where-Object { $_.Topic.moduleId -ne $module.id })
        $outsideRanking = @($result.Ranking | Where-Object { $_.Topic.moduleId -ne $module.id })
        [pscustomobject]@{
            ModuleId = $module.id
            Passed = $result.Resolution -eq 'answered' -and
                @($result.Topics).Count -gt 0 -and
                $outsideTopics.Count -eq 0 -and
                $outsideRanking.Count -eq 0 -and
                @($result.Candidates).Count -eq 0
        }
    }
    $moduleScopeCount = @($moduleScopeResults).Count
    $moduleScopePassedCount = @($moduleScopeResults | Where-Object Passed).Count
    $moduleScopeRate = $moduleScopePassedCount / [math]::Max(1, $moduleScopeCount)

    $missingModuleRequest = [IND_CRM_API.Services.HelpRetrievalRequest]::new()
    $missingModuleRequest.Question = 'ayuda'
    $missingModuleRequest.SelectedModuleId = '__missing-help-module__'
    $missingModuleRequest.ResponseLocale = $snapshot.Bundle.defaultLocale
    $missingModuleResult = $retriever.Retrieve($snapshot, $missingModuleRequest)
    $missingModulePassed = $missingModuleResult.Resolution -eq 'notDocumented' -and
        @($missingModuleResult.Topics).Count -eq 0 -and @($missingModuleResult.Ranking).Count -eq 0

    $modulePair = @($snapshot.Bundle.modules | Where-Object { @($_.topicIds).Count -gt 0 } | Select-Object -First 2)
    $mismatchedSelectionPassed = $modulePair.Count -lt 2
    if ($modulePair.Count -ge 2) {
        $mismatchRequest = [IND_CRM_API.Services.HelpRetrievalRequest]::new()
        $mismatchRequest.Question = ''
        $mismatchRequest.SelectedTopicId = [string]$modulePair[0].topicIds[0]
        $mismatchRequest.SelectedModuleId = [string]$modulePair[1].id
        $mismatchRequest.ResponseLocale = $snapshot.Bundle.defaultLocale
        $mismatchResult = $retriever.Retrieve($snapshot, $mismatchRequest)
        $mismatchedSelectionPassed = $mismatchResult.Resolution -eq 'notDocumented' -and
            @($mismatchResult.Topics).Count -eq 0
    }

    $broadModulePassed = $true
    if ($snapshot.ModulesById.ContainsKey('expenses')) {
        $broadModuleRequest = [IND_CRM_API.Services.HelpRetrievalRequest]::new()
        $broadModuleRequest.Question = 'gastos'
        $broadModuleRequest.SelectedModuleId = 'expenses'
        $broadModuleRequest.ResponseLocale = $snapshot.Bundle.defaultLocale
        $broadModuleResult = $retriever.Retrieve($snapshot, $broadModuleRequest)
        $broadModulePassed = $broadModuleResult.Resolution -ne 'needsSelection' -and
            @($broadModuleResult.Topics | Where-Object { $_.Topic.moduleId -ne 'expenses' }).Count -eq 0 -and
            @($broadModuleResult.Ranking | Where-Object { $_.Topic.moduleId -ne 'expenses' }).Count -eq 0
    }

    $overlapMethod = [IND_CRM_API.Services.HelpOpenAiAnswerService].GetMethod(
        'HasLongVerbatimOverlap',
        [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static)
    if ($null -eq $overlapMethod) {
        throw 'The long verbatim overlap guard could not be resolved.'
    }
    $compactEvidence = -join (1..5 | ForEach-Object { 'abcdefghijklmnopqrstuvwxyz0123456789' })
    $offsetKnowledgeJson = ConvertTo-Json -InputObject @([ordered]@{
        summary = ''
        quickAnswers = @()
        chunks = @([ordered]@{ body = $compactEvidence })
    }) -Depth 5 -Compress
    $offsetKnowledge = [Newtonsoft.Json.Linq.JArray]::Parse($offsetKnowledgeJson)
    $offsetAnswer = 'xyz' + $compactEvidence.Substring(0, 120)
    $offsetCopyDetected = [bool]$overlapMethod.Invoke($null, [object[]]@($offsetAnswer, $offsetKnowledge))

    $shortLabelKnowledgeJson = ConvertTo-Json -InputObject @([ordered]@{
        summary = ''
        quickAnswers = @()
        chunks = @([ordered]@{ body = 'Abre Hojas de gastos para consultar los registros disponibles.' })
    }) -Depth 5 -Compress
    $shortLabelKnowledge = [Newtonsoft.Json.Linq.JArray]::Parse($shortLabelKnowledgeJson)
    $shortLabelRejected = [bool]$overlapMethod.Invoke($null, [object[]]@('Hojas de gastos', $shortLabelKnowledge))
    $verbatimGuardPassed = $offsetCopyDetected -and -not $shortLabelRejected

    $results | Format-Table -AutoSize
    Write-Host ('Cases={0} TopicCases={1} Resolution={2:P2} Top1={3:P2} RecallAt5={4:P2}' -f $count, $topicCases.Count, $resolutionRate, $top1Rate, $recallRate)
    Write-Host ('MenuExact={0:P2} ({1}/{2}) MissingTopic={3}' -f `
        $menuExactRate, $menuExactPassedCount, $menuExactCount, $(if ($missingTopicPassed) { 'Passed' } else { 'Failed' }))
    Write-Host ('ModuleScope={0:P2} ({1}/{2}) MissingModule={3} MismatchedSelection={4} BroadModule={5}' -f `
        $moduleScopeRate, $moduleScopePassedCount, $moduleScopeCount, `
        $(if ($missingModulePassed) { 'Passed' } else { 'Failed' }), `
        $(if ($mismatchedSelectionPassed) { 'Passed' } else { 'Failed' }), `
        $(if ($broadModulePassed) { 'Passed' } else { 'Failed' }))
    Write-Host ('VerbatimOffsetCopy={0} ShortUiLabelAllowed={1}' -f `
        $(if ($offsetCopyDetected) { 'Passed' } else { 'Failed' }), `
        $(if (-not $shortLabelRejected) { 'Passed' } else { 'Failed' }))

    if ($resolutionRate -lt 1.0 -or $top1Rate -lt $MinimumTop1 -or $recallRate -lt $MinimumRecallAt5 -or
        $menuExactRate -lt 1.0 -or -not $missingTopicPassed -or $moduleScopeRate -lt 1.0 -or
        -not $missingModulePassed -or -not $mismatchedSelectionPassed -or -not $broadModulePassed -or
        -not $verbatimGuardPassed) {
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
        $moduleScopeResults | Where-Object { -not $_.Passed } | ForEach-Object {
            Write-Host ('- ModuleScope failed: {0}' -f $_.ModuleId)
        }
        if (-not $missingModulePassed) {
            Write-Host ('- MissingModule failed: resolution={0}' -f $missingModuleResult.Resolution)
        }
        if (-not $mismatchedSelectionPassed) {
            Write-Host '- Mismatched topic/module selection failed.'
        }
        if (-not $broadModulePassed) {
            Write-Host '- Broad selected module returned a granular selection or escaped its scope.'
        }
        if (-not $verbatimGuardPassed) {
            Write-Host '- Verbatim guard failed the offset-copy or short-label regression.'
        }
        exit 1
    }
}
finally {
    [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
