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

# Compares ordered string collections without relying on PowerShell object identity.
function Test-ExactStringSequence {
    param($Left, $Right)

    $leftItems = @($Left | ForEach-Object { [string]$_ })
    $rightItems = @($Right | ForEach-Object { [string]$_ })
    if ($leftItems.Count -ne $rightItems.Count) {
        return $false
    }
    for ($index = 0; $index -lt $leftItems.Count; $index++) {
        if ($leftItems[$index] -cne $rightItems[$index]) {
            return $false
        }
    }
    return $true
}

# Builds a minimal completed provider response around one structured help result.
function New-CompletedStructuredHelpResponse {
    param([Parameter(Mandatory = $true)]$StructuredOutput)

    $outputText = ConvertTo-Json -InputObject $StructuredOutput -Depth 8 -Compress
    return ConvertTo-Json -InputObject ([ordered]@{
        status = 'completed'
        output_text = $outputText
        output = @([ordered]@{ type = 'message'; content = @() })
        usage = [ordered]@{
            input_tokens = 10
            output_tokens = 10
            input_tokens_details = [ordered]@{ cached_tokens = 0 }
        }
    }) -Depth 10 -Compress
}

# Verifies one topic DTO against either canonical or localized display content.
function Test-HelpTopicProjection {
    param($Projection, $Topic, $Localization, [string]$ExpectedLocale)

    $expectedTitle = if ($null -eq $Localization) { $Topic.title } else { $Localization.title }
    $expectedSummary = if ($null -eq $Localization) { $Topic.summary } else { $Localization.summary }
    $expectedChunks = if ($null -eq $Localization) { @($Topic.chunks) } else { @($Localization.chunks) }
    $expectedQuickAnswers = if ($null -eq $Localization) { @($Topic.quickAnswers) } else { @($Localization.quickAnswers) }
    if ($Projection.ResponseLocale -cne $ExpectedLocale -or
        $Projection.Title -cne $expectedTitle -or
        $Projection.Summary -cne $expectedSummary -or
        @($Projection.Chunks).Count -ne $expectedChunks.Count -or
        @($Projection.QuickAnswers).Count -ne $expectedQuickAnswers.Count) {
        return $false
    }

    for ($index = 0; $index -lt $expectedChunks.Count; $index++) {
        $actual = $Projection.Chunks[$index]
        $expected = $expectedChunks[$index]
        if ($actual.Id -cne $expected.id -or
            $actual.Heading -cne $expected.heading -or
            $actual.Body -cne $expected.body -or
            -not (Test-ExactStringSequence $actual.ImageRefs $expected.imageRefs)) {
            return $false
        }
    }

    for ($index = 0; $index -lt $expectedQuickAnswers.Count; $index++) {
        $actual = $Projection.QuickAnswers[$index]
        $expected = $expectedQuickAnswers[$index]
        if ($actual.Id -cne $expected.id -or
            $actual.Question -cne $expected.question -or
            $actual.Answer -cne $expected.answer -or
            -not (Test-ExactStringSequence $actual.SourceChunkIds $expected.sourceChunkIds)) {
            return $false
        }
    }
    return $true
}

# Verifies that a catalog response uses one locale consistently without mixed fallback.
function Test-HelpCatalogProjection {
    param($Projection, $Snapshot, [string]$ExpectedLocale)

    $useLocalization = @($Snapshot.Bundle.modules | Where-Object {
        $null -eq $_.localizations -or -not $_.localizations.ContainsKey($ExpectedLocale)
    }).Count -eq 0 -and @($Snapshot.Bundle.topics | Where-Object {
        $null -eq $_.localizations -or -not $_.localizations.ContainsKey($ExpectedLocale)
    }).Count -eq 0
    $expectedModules = @($Snapshot.Bundle.modules | Sort-Object order, id)
    if ($Projection.ResponseLocale -cne $ExpectedLocale -or @($Projection.Modules).Count -ne $expectedModules.Count) {
        return $false
    }

    for ($moduleIndex = 0; $moduleIndex -lt $expectedModules.Count; $moduleIndex++) {
        $module = $expectedModules[$moduleIndex]
        $actualModule = $Projection.Modules[$moduleIndex]
        $moduleLocalization = if ($useLocalization) { $module.localizations[$ExpectedLocale] } else { $null }
        $expectedTitle = if ($null -eq $moduleLocalization) { $module.title } else { $moduleLocalization.title }
        $expectedDescription = if ($null -eq $moduleLocalization) { $module.description } else { $moduleLocalization.description }
        if ($actualModule.Id -cne $module.id -or
            $actualModule.Title -cne $expectedTitle -or
            $actualModule.Description -cne $expectedDescription -or
            @($actualModule.Topics).Count -ne @($module.topicIds).Count) {
            return $false
        }

        for ($topicIndex = 0; $topicIndex -lt @($module.topicIds).Count; $topicIndex++) {
            $topic = $Snapshot.TopicsById[[string]$module.topicIds[$topicIndex]]
            $actualTopic = $actualModule.Topics[$topicIndex]
            $topicLocalization = if ($useLocalization) { $topic.localizations[$ExpectedLocale] } else { $null }
            $expectedTopicTitle = if ($null -eq $topicLocalization) { $topic.title } else { $topicLocalization.title }
            $expectedTopicSummary = if ($null -eq $topicLocalization) { $topic.summary } else { $topicLocalization.summary }
            if ($actualTopic.Id -cne $topic.id -or
                $actualTopic.Title -cne $expectedTopicTitle -or
                $actualTopic.Summary -cne $expectedTopicSummary) {
                return $false
            }
        }
    }
    return $true
}

$schema10BundleValidationPath = $null
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
    $manualOnlyModuleIds = @('troubleshooting', 'glossary')

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
        $manualOnlyEvaluation = $expectedIds.Count -gt 0 -and
            @($expectedIds | Where-Object {
                -not $snapshot.TopicsById.ContainsKey($_) -or
                $manualOnlyModuleIds -notcontains $snapshot.TopicsById[$_].moduleId
            }).Count -eq 0
        if ($manualOnlyEvaluation) {
            $returnedTopicIds = @($result.Topics | ForEach-Object { $_.Topic.id }) +
                @($result.Ranking | ForEach-Object { $_.Topic.id }) +
                @($result.Candidates | ForEach-Object { $_.topicId })
            $manualOnlyReturned = @($returnedTopicIds | Where-Object {
                $snapshot.TopicsById.ContainsKey($_) -and
                $manualOnlyModuleIds -contains $snapshot.TopicsById[$_].moduleId
            }).Count -gt 0
            $expectedResolution = 'manualOnlyHidden'
            $actualResolution = if ($manualOnlyReturned) { 'manualOnlyExposed' } else { 'manualOnlyHidden' }
            $expectedIds = @()
        }
        else {
            $actualResolution = $result.Resolution
        }

        $top1 = $expectedIds.Count -eq 0 -or ($actualIds.Count -gt 0 -and $expectedIds -contains $actualIds[0])
        $recall = $expectedIds.Count -eq 0 -or @($expectedIds | Where-Object { $actualIds -contains $_ }).Count -eq $expectedIds.Count
        [pscustomobject]@{
            Id = [string]$case.id
            ExpectedResolution = $expectedResolution
            ActualResolution = $actualResolution
            ExpectedTopicIds = ($expectedIds -join '|')
            ActualTopicIds = ($actualIds -join '|')
            ResolutionPassed = $actualResolution -eq $expectedResolution
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

    $chatTopics = @($snapshot.Bundle.topics | Where-Object {
        $manualOnlyModuleIds -notcontains $_.moduleId
    })
    $menuExactResults = foreach ($topic in $chatTopics) {
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

    $manualOnlySelectionResults = foreach ($topic in @($snapshot.Bundle.topics | Where-Object {
        $manualOnlyModuleIds -contains $_.moduleId
    })) {
        $topicRequest = [IND_CRM_API.Services.HelpRetrievalRequest]::new()
        $topicRequest.Question = $topic.title
        $topicRequest.SelectedTopicId = $topic.id
        $topicRequest.ResponseLocale = $snapshot.Bundle.defaultLocale
        $topicResult = $retriever.Retrieve($snapshot, $topicRequest)

        $moduleRequest = [IND_CRM_API.Services.HelpRetrievalRequest]::new()
        $moduleRequest.Question = $topic.title
        $moduleRequest.SelectedModuleId = $topic.moduleId
        $moduleRequest.ResponseLocale = $snapshot.Bundle.defaultLocale
        $moduleResult = $retriever.Retrieve($snapshot, $moduleRequest)

        $genericRequest = [IND_CRM_API.Services.HelpRetrievalRequest]::new()
        $genericRequest.Question = $topic.title
        $genericRequest.ResponseLocale = $snapshot.Bundle.defaultLocale
        $genericResult = $retriever.Retrieve($snapshot, $genericRequest)
        $genericVisibleModules = @((@($genericResult.Topics) + @($genericResult.Ranking)) | ForEach-Object {
            $_.Topic.moduleId
        }) + @($genericResult.Candidates | ForEach-Object {
            $snapshot.TopicsById[$_.topicId].moduleId
        })

        [pscustomobject]@{
            TopicId = $topic.id
            Passed = $topicResult.Resolution -eq 'notDocumented' -and
                $moduleResult.Resolution -eq 'notDocumented' -and
                @($genericVisibleModules | Where-Object { $manualOnlyModuleIds -contains $_ }).Count -eq 0
        }
    }
    $manualOnlyHiddenPassed = @($manualOnlySelectionResults | Where-Object { -not $_.Passed }).Count -eq 0

    $missingRequest = [IND_CRM_API.Services.HelpRetrievalRequest]::new()
    $missingRequest.Question = ''
    $missingRequest.SelectedTopicId = '__missing-help-topic__'
    $missingRequest.ResponseLocale = $snapshot.Bundle.defaultLocale
    $missingResult = $retriever.Retrieve($snapshot, $missingRequest)
    $missingTopicPassed = $missingResult.Resolution -eq 'notDocumented' -and
        @($missingResult.Topics).Count -eq 0 -and @($missingResult.Ranking).Count -eq 0

    $moduleScopeResults = foreach ($module in @($snapshot.Bundle.modules | Where-Object {
        $manualOnlyModuleIds -notcontains $_.id
    })) {
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
        $expectedTopicIds = @($module.topicIds | Where-Object { $snapshot.TopicsById.ContainsKey($_) })
        $actualTopicIds = @($result.Topics | ForEach-Object { $_.Topic.id })
        $expectedSupportingTopicIds = @(if ($snapshot.ModulesById.ContainsKey('troubleshooting')) {
            @($snapshot.ModulesById['troubleshooting'].topicIds | Where-Object { $snapshot.TopicsById.ContainsKey($_) })
        }
        else {
            @()
        })
        $actualSupportingTopicIds = @($result.SupportingTopics | ForEach-Object { $_.Topic.id })
        [pscustomobject]@{
            ModuleId = $module.id
            ActualTopicIds = $actualTopicIds -join '|'
            ExpectedTopicIds = $expectedTopicIds -join '|'
            ActualSupportingTopicIds = $actualSupportingTopicIds -join '|'
            ExpectedSupportingTopicIds = $expectedSupportingTopicIds -join '|'
            OutsideTopicCount = $outsideTopics.Count
            OutsideRankingCount = $outsideRanking.Count
            CandidateCount = @($result.Candidates).Count
            Resolution = $result.Resolution
            Mode = $result.Mode
            Passed = $result.Resolution -eq 'answered' -and
                $result.Mode -eq 'module-ai-scope' -and
                (Test-ExactStringSequence $actualTopicIds $expectedTopicIds) -and
                (Test-ExactStringSequence -Left $actualSupportingTopicIds -Right $expectedSupportingTopicIds) -and
                $outsideTopics.Count -eq 0 -and
                $outsideRanking.Count -eq 0 -and
                @($result.Candidates).Count -eq 0
        }
    }
    $moduleScopeCount = @($moduleScopeResults).Count
    $moduleScopePassedCount = @($moduleScopeResults | Where-Object Passed).Count
    $moduleScopeRate = $moduleScopePassedCount / [math]::Max(1, $moduleScopeCount)

    $chatSupportAttachedPassed = $true
    if ($chatTopics.Count -gt 0 -and $snapshot.ModulesById.ContainsKey('troubleshooting')) {
        $supportProbeRequest = [IND_CRM_API.Services.HelpRetrievalRequest]::new()
        $supportProbeRequest.Question = $chatTopics[0].title
        $supportProbeRequest.SelectedTopicId = $chatTopics[0].id
        $supportProbeRequest.ResponseLocale = $snapshot.Bundle.defaultLocale
        $supportProbeResult = $retriever.Retrieve($snapshot, $supportProbeRequest)
        $expectedSupportIds = @($snapshot.ModulesById['troubleshooting'].topicIds | Where-Object {
            $snapshot.TopicsById.ContainsKey($_)
        })
        $actualSupportIds = @($supportProbeResult.SupportingTopics | ForEach-Object { $_.Topic.id })
        $chatSupportAttachedPassed = Test-ExactStringSequence $actualSupportIds $expectedSupportIds
    }

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
    $moduleNoMatchPassed = $true
    $moduleAiContextPassed = $true
    $moduleAiOversizePassed = $true
    $moduleAiResolutionPassed = $false
    $moduleAiContextFailure = $null
    if ($snapshot.ModulesById.ContainsKey('expenses')) {
        $broadModuleRequest = [IND_CRM_API.Services.HelpRetrievalRequest]::new()
        $broadModuleRequest.Question = 'gastos'
        $broadModuleRequest.SelectedModuleId = 'expenses'
        $broadModuleRequest.ResponseLocale = $snapshot.Bundle.defaultLocale
        $broadModuleResult = $retriever.Retrieve($snapshot, $broadModuleRequest)
        $broadModulePassed = $broadModuleResult.Resolution -ne 'needsSelection' -and
            @($broadModuleResult.Topics | Where-Object { $_.Topic.moduleId -ne 'expenses' }).Count -eq 0 -and
            @($broadModuleResult.Ranking | Where-Object { $_.Topic.moduleId -ne 'expenses' }).Count -eq 0

        $moduleNoMatchRequest = [IND_CRM_API.Services.HelpRetrievalRequest]::new()
        $moduleNoMatchRequest.Question = 'Como?'
        $moduleNoMatchRequest.SelectedModuleId = 'expenses'
        $moduleNoMatchRequest.ResponseLocale = $snapshot.Bundle.defaultLocale
        $moduleNoMatchResult = $retriever.Retrieve($snapshot, $moduleNoMatchRequest)
        $moduleNoMatchPassed = $moduleNoMatchResult.Resolution -eq 'answered' -and
            $moduleNoMatchResult.Mode -eq 'module-ai-scope' -and
            @($moduleNoMatchResult.Topics).Count -eq @($snapshot.ModulesById['expenses'].topicIds).Count -and
            @($moduleNoMatchResult.Ranking).Count -eq 0

        $completeEvidenceProperty = [IND_CRM_API.Services.HelpRetrievalResult].GetProperty('RequireCompleteEvidence')
        $buildContextMethod = [IND_CRM_API.Services.HelpOpenAiAnswerService].GetMethod(
            'BuildContext',
            [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Instance)
        $parseResponseMethod = [IND_CRM_API.Services.HelpOpenAiAnswerService].GetMethod(
            'ParseResponse',
            [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Instance)
        $buildActionsMethod = [IND_CRM_API.Controllers.System.INDHelpAiController].GetMethod(
            'BuildActions',
            [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static)
        $buildGeneratedSourcesMethod = [IND_CRM_API.Controllers.System.INDHelpAiController].GetMethod(
            'BuildGeneratedSources',
            [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static)
        if ($null -eq $completeEvidenceProperty -or $null -eq $buildContextMethod -or
            $null -eq $parseResponseMethod -or $null -eq $buildActionsMethod -or
            $null -eq $buildGeneratedSourcesMethod) {
            $moduleAiContextPassed = $false
            $moduleAiOversizePassed = $false
            $moduleAiResolutionPassed = $false
            $moduleAiContextFailure = 'Complete module evidence contract is missing.'
        }
        else {
            $answerService = [IND_CRM_API.Services.HelpOpenAiAnswerService]::new(
                [IND_CRM_API.Services.FileAxLogger]::new())
            $firstRetrievedTopic = @($broadModuleResult.Topics | Select-Object -First 1)[0].Topic
            $extraChunks = @(
                [IND_CRM_API.Services.HelpKnowledgeChunk]@{
                    id = $firstRetrievedTopic.id + '--test-extra-01'
                    heading = 'Additional module evidence one'
                    body = 'First additional chunk used to verify complete module evidence.'
                    imageRefs = [System.Collections.Generic.List[string]]::new()
                    estimatedTokens = 20
                },
                [IND_CRM_API.Services.HelpKnowledgeChunk]@{
                    id = $firstRetrievedTopic.id + '--test-extra-02'
                    heading = 'Additional module evidence two'
                    body = 'Second additional chunk used to verify complete module evidence.'
                    imageRefs = [System.Collections.Generic.List[string]]::new()
                    estimatedTokens = 20
                }
            )
            foreach ($extraChunk in $extraChunks) {
                $firstRetrievedTopic.chunks.Add($extraChunk)
            }
            try {
                $history = [System.Collections.Generic.List[IND_CRM_API.Contracts.Requests.HelpConversationMessageRequest]]::new()
                $largeHistoryText = [string]::new([char]0x754C, 1600)
                foreach ($index in 1..8) {
                    $history.Add([IND_CRM_API.Contracts.Requests.HelpConversationMessageRequest]@{
                        role = $(if ($index % 2 -eq 0) { 'assistant' } else { 'user' })
                        content = $largeHistoryText
                    })
                }
                $answerRequest = [IND_CRM_API.Services.HelpAnswerRequest]@{
                    Question = [string]::new([char]0x754C, 1200)
                    ResponseLocale = $snapshot.Bundle.defaultLocale
                    AnswerInstructions = [string]::new([char]0x754C, 2000)
                    History = $history
                    Snapshot = $snapshot
                    Retrieval = $broadModuleResult
                }
                $context = $buildContextMethod.Invoke($answerService, [object[]]@($answerRequest))
                $allowedSourceKeys = @($context.AllowedSourceKeys | Sort-Object)
                $primarySourceKeys = @($context.PrimarySourceKeys | Sort-Object)
                $contextTopics = @($broadModuleResult.Topics) + @($broadModuleResult.SupportingTopics)
                $expectedSourceKeys = @($contextTopics | ForEach-Object {
                    $topic = $_.Topic
                    @($topic.chunks | ForEach-Object { $topic.id + ':' + $_.id })
                } | Sort-Object)
                $expectedPrimarySourceKeys = @($broadModuleResult.Topics | ForEach-Object {
                    $topic = $_.Topic
                    @($topic.chunks | ForEach-Object { $topic.id + ':' + $_.id })
                } | Sort-Object)
                $completeEvidenceEnabled = [bool]$completeEvidenceProperty.GetValue($broadModuleResult)
                $sourceKeysPassed = Test-ExactStringSequence $allowedSourceKeys $expectedSourceKeys
                $contextHistoryCount = $context.History.Count
                $moduleAiContextPassed = $completeEvidenceEnabled -and
                    $broadModuleResult.Mode -eq 'module-ai-scope' -and
                    $sourceKeysPassed -and
                    (Test-ExactStringSequence $primarySourceKeys $expectedPrimarySourceKeys) -and
                    @($context.Knowledge.Children() | Where-Object {
                        [string]$_['contextRole'] -eq 'diagnostic-support'
                    }).Count -eq @($broadModuleResult.SupportingTopics).Count -and
                    $contextHistoryCount -lt 8
                if (-not $moduleAiContextPassed) {
                    $moduleAiContextFailure = 'Complete={0}; mode={1}; sources={2}/{3}; history={4}' -f `
                        $completeEvidenceEnabled, $broadModuleResult.Mode, `
                        $allowedSourceKeys.Count, $expectedSourceKeys.Count, $contextHistoryCount
                }

                $validSourceKey = @($primarySourceKeys | Select-Object -First 1)[0]
                $supportSourceKey = @($broadModuleResult.SupportingTopics | ForEach-Object {
                    $topic = $_.Topic
                    @($topic.chunks | ForEach-Object { $topic.id + ':' + $_.id })
                } | Select-Object -First 1)[0]
                if (-not [string]::IsNullOrWhiteSpace([string]$supportSourceKey)) {
                    $supportCitations = [System.Collections.Generic.List[string]]::new()
                    $supportCitations.Add([string]$supportSourceKey)
                    $visibleSupportSources = $buildGeneratedSourcesMethod.Invoke(
                        $null,
                        [object[]]@($broadModuleResult, $supportCitations))
                    $moduleAiContextPassed = $moduleAiContextPassed -and @($visibleSupportSources).Count -eq 0
                }
                [string]$answeredBody = New-CompletedStructuredHelpResponse ([ordered]@{
                    resolution = 'answered'
                    answer = 'Use the documented CRM procedure.'
                    citationSourceKeys = @($validSourceKey)
                    actionRouteKeys = @()
                })
                $answeredParsed = $parseResponseMethod.Invoke(
                    $answerService,
                    [object[]]@($answeredBody, $context))
                [string]$notDocumentedBody = New-CompletedStructuredHelpResponse ([ordered]@{
                    resolution = 'notDocumented'
                    answer = 'The supplied CRM documentation does not contain this procedure.'
                    citationSourceKeys = @()
                    actionRouteKeys = @()
                })
                $notDocumentedParsed = $parseResponseMethod.Invoke(
                    $answerService,
                    [object[]]@($notDocumentedBody, $context))

                $supportOnlyRejected = $true
                $primaryAndSupportAccepted = $true
                if (-not [string]::IsNullOrWhiteSpace([string]$supportSourceKey)) {
                    $supportOnlyRejected = $false
                    try {
                        [string]$supportOnlyBody = New-CompletedStructuredHelpResponse ([ordered]@{
                            resolution = 'answered'
                            answer = 'Use the documented CRM procedure.'
                            citationSourceKeys = @($supportSourceKey)
                            actionRouteKeys = @()
                        })
                        [void]$parseResponseMethod.Invoke($answerService, [object[]]@($supportOnlyBody, $context))
                    }
                    catch {
                        $supportOnlyRejected = $_.Exception.InnerException.ProviderSummary -eq 'ungrounded-structured-output'
                    }

                    [string]$primaryAndSupportBody = New-CompletedStructuredHelpResponse ([ordered]@{
                        resolution = 'answered'
                        answer = 'Use the documented CRM procedure.'
                        citationSourceKeys = @($validSourceKey, $supportSourceKey)
                        actionRouteKeys = @()
                    })
                    $primaryAndSupportParsed = $parseResponseMethod.Invoke(
                        $answerService,
                        [object[]]@($primaryAndSupportBody, $context))
                    $primaryAndSupportAccepted = $primaryAndSupportParsed.Answer.Resolution -eq 'answered'
                }

                $invalidCitationRejected = $false
                try {
                    [string]$invalidCitationBody = New-CompletedStructuredHelpResponse ([ordered]@{
                        resolution = 'answered'
                        answer = 'Use the documented CRM procedure.'
                        citationSourceKeys = @('invented.topic:invented.chunk')
                        actionRouteKeys = @()
                    })
                    [void]$parseResponseMethod.Invoke($answerService, [object[]]@($invalidCitationBody, $context))
                }
                catch {
                    $invalidCitationRejected = $_.Exception.InnerException.ProviderSummary -eq 'ungrounded-structured-output'
                }

                $notDocumentedCitationRejected = $false
                try {
                    [string]$invalidNotDocumentedBody = New-CompletedStructuredHelpResponse ([ordered]@{
                        resolution = 'notDocumented'
                        answer = 'The supplied CRM documentation does not contain this procedure.'
                        citationSourceKeys = @($validSourceKey)
                        actionRouteKeys = @()
                    })
                    [void]$parseResponseMethod.Invoke($answerService, [object[]]@($invalidNotDocumentedBody, $context))
                }
                catch {
                    $notDocumentedCitationRejected = $_.Exception.InnerException.ProviderSummary -eq 'ungrounded-structured-output'
                }

                $citedRouteKey = $context.SourceTopicLookup[$validSourceKey].routeKey
                $unrelatedRouteKey = @($context.AllowedRouteKeys |
                    Where-Object { $_ -ne $citedRouteKey } |
                    Select-Object -First 1)
                $unrelatedActionRejected = $true
                if ($unrelatedRouteKey.Count -gt 0) {
                    $unrelatedActionRejected = $false
                    try {
                        [string]$unrelatedActionBody = New-CompletedStructuredHelpResponse ([ordered]@{
                            resolution = 'answered'
                            answer = 'Use the documented CRM procedure.'
                            citationSourceKeys = @($validSourceKey)
                            actionRouteKeys = @($unrelatedRouteKey[0])
                        })
                        [void]$parseResponseMethod.Invoke($answerService, [object[]]@($unrelatedActionBody, $context))
                    }
                    catch {
                        $unrelatedActionRejected = $_.Exception.InnerException.ProviderSummary -eq 'ungrounded-structured-output'
                    }
                }

                $actionLabelMatchesCitation = $true
                $sharedRouteGroup = @($context.SourceTopicLookup.GetEnumerator() |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_.Value.routeKey) } |
                    Group-Object { $_.Value.routeKey } |
                    Where-Object Count -gt 1 |
                    Select-Object -First 1)
                if ($sharedRouteGroup.Count -gt 0) {
                    $citedEntry = @($sharedRouteGroup[0].Group | Select-Object -Last 1)[0]
                    $actions = @($buildActionsMethod.Invoke($null, [object[]]@(
                        $broadModuleResult,
                        [string[]]@($citedEntry.Value.routeKey),
                        [string[]]@($citedEntry.Value.id))))
                    $actionLabelMatchesCitation = $actions.Count -eq 1 -and
                        $actions[0].Label -eq $citedEntry.Value.title
                }
                $moduleAiResolutionPassed = $answeredParsed.Answer.Resolution -eq 'answered' -and
                    $notDocumentedParsed.Answer.Resolution -eq 'notDocumented' -and
                    $supportOnlyRejected -and $primaryAndSupportAccepted -and
                    $invalidCitationRejected -and $notDocumentedCitationRejected -and
                    $unrelatedActionRejected -and $actionLabelMatchesCitation
                if (-not $moduleAiResolutionPassed) {
                    $moduleAiContextFailure = 'Structured resolution, citation, or action validation failed.'
                }
            }
            catch {
                $moduleAiContextPassed = $false
                $contextFailure = if ($null -ne $_.Exception.InnerException) {
                    $_.Exception.InnerException
                }
                else {
                    $_.Exception
                }
                $moduleAiContextFailure = $contextFailure.ToString()
            }
            finally {
                foreach ($extraChunk in $extraChunks) {
                    [void]$firstRetrievedTopic.chunks.Remove($extraChunk)
                }
            }

            $oversizedChunk = [IND_CRM_API.Services.HelpKnowledgeChunk]@{
                id = $firstRetrievedTopic.id + '--test-oversized'
                heading = 'Oversized module evidence'
                body = ('Oversized complete module evidence. ' * 5000)
                imageRefs = [System.Collections.Generic.List[string]]::new()
                estimatedTokens = 50000
            }
            $firstRetrievedTopic.chunks.Add($oversizedChunk)
            try {
                $oversizedRequest = [IND_CRM_API.Services.HelpAnswerRequest]@{
                    Question = 'Como debo meter un gasto?'
                    ResponseLocale = $snapshot.Bundle.defaultLocale
                    AnswerInstructions = $null
                    History = [System.Collections.Generic.List[IND_CRM_API.Contracts.Requests.HelpConversationMessageRequest]]::new()
                    Snapshot = $snapshot
                    Retrieval = $broadModuleResult
                }
                [void]$buildContextMethod.Invoke($answerService, [object[]]@($oversizedRequest))
                $moduleAiOversizePassed = $false
            }
            catch {
                $providerFailure = $_.Exception.InnerException
                $moduleAiOversizePassed = $null -ne $providerFailure -and
                    $providerFailure.ProviderSummary -eq 'module-context-budget-exceeded'
                if (-not $moduleAiOversizePassed) {
                    $moduleAiContextFailure = $_.Exception.ToString()
                }
            }
            finally {
                [void]$firstRetrievedTopic.chunks.Remove($oversizedChunk)
            }
        }
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

    $rewriteRequiredMethod = [IND_CRM_API.Services.HelpOpenAiAnswerService].GetMethod(
        'CreateRewriteRequired',
        [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static)
    if ($null -eq $rewriteRequiredMethod) {
        throw 'The answer rewrite error factory could not be resolved.'
    }
    $rewriteRequiredError = $rewriteRequiredMethod.Invoke($null, $null)
    $rewriteRequiredPassed = $rewriteRequiredError -is [IND_CRM_API.Services.HelpAnswerQualityException] -and
        $rewriteRequiredError.ErrorCode -ceq 'HELP_ANSWER_REWRITE_REQUIRED' -and
        $rewriteRequiredError.Summary -ceq 'verbatim-overlap'

    # The endpoint accepts every UI culture while the current bundle intentionally publishes Spanish only.
    $acceptedRequestLocales = @('es-ES', 'eu-ES', 'en', 'pt', 'it', 'zh-Hans')
    $defaultLocale = [string]$snapshot.Bundle.defaultLocale
    $nonSpanishModuleLocalizationCount = @($snapshot.Bundle.modules | ForEach-Object {
        @($_.localizations.Keys) | Where-Object { $_ -cne $defaultLocale }
    }).Count
    $nonSpanishTopicLocalizationCount = @($snapshot.Bundle.topics | ForEach-Object {
        @($_.localizations.Keys) | Where-Object { $_ -cne $defaultLocale }
    }).Count
    $bundleStructurePassed = $snapshot.Bundle.schemaVersion -ceq '1.1' -and
        $defaultLocale -ceq 'es-ES' -and
        (Test-ExactStringSequence $snapshot.Bundle.supportedResponseLocales @('es-ES')) -and
        $nonSpanishModuleLocalizationCount -eq 0 -and
        $nonSpanishTopicLocalizationCount -eq 0

    $bundleCatalogResults = foreach ($locale in $acceptedRequestLocales) {
        $projection = [IND_CRM_API.Services.HelpKnowledgeProjection]::ToCatalog($snapshot, $locale)
        [pscustomobject]@{
            Locale = $locale
            Passed = Test-HelpCatalogProjection $projection $snapshot $defaultLocale
        }
    }
    $bundleCatalogCount = @($bundleCatalogResults).Count
    $bundleCatalogPassedCount = @($bundleCatalogResults | Where-Object Passed).Count
    $bundleCatalogRate = $bundleCatalogPassedCount / [math]::Max(1, $bundleCatalogCount)

    $bundleTopicResults = foreach ($locale in $acceptedRequestLocales) {
        foreach ($topic in $snapshot.Bundle.topics) {
            $spanishLocalization = if ($topic.localizations.ContainsKey($defaultLocale)) {
                $topic.localizations[$defaultLocale]
            }
            else {
                $null
            }
            $projection = [IND_CRM_API.Services.HelpKnowledgeProjection]::ToTopic($snapshot, $topic, $locale)
            [pscustomobject]@{
                Locale = $locale
                TopicId = $topic.id
                Passed = Test-HelpTopicProjection `
                    $projection `
                    $topic `
                    $spanishLocalization `
                    $defaultLocale
            }
        }
    }
    $bundleTopicCount = @($bundleTopicResults).Count
    $bundleTopicPassedCount = @($bundleTopicResults | Where-Object Passed).Count
    $bundleTopicRate = $bundleTopicPassedCount / [math]::Max(1, $bundleTopicCount)

    # Derive a schema 1.0 fixture without localization maps and verify canonical fallback.
    $schema10BundleObject = [Newtonsoft.Json.Linq.JObject]::Parse(
        [Newtonsoft.Json.JsonConvert]::SerializeObject($snapshot.Bundle))
    $schema10BundleObject['schemaVersion'] = [Newtonsoft.Json.Linq.JValue]::CreateString('1.0')
    foreach ($moduleToken in @($schema10BundleObject['modules'])) {
        [void]$moduleToken.Remove('localizations')
    }
    foreach ($topicToken in @($schema10BundleObject['topics'])) {
        [void]$topicToken.Remove('localizations')
    }
    $schema10BundleValidationPath = Join-Path `
        ([System.IO.Path]::GetTempPath()) `
        ('ind-crm-help-schema-10-{0}.json' -f [Guid]::NewGuid().ToString('N'))
    [System.IO.File]::WriteAllText(
        $schema10BundleValidationPath,
        $schema10BundleObject.ToString([Newtonsoft.Json.Formatting]::None),
        [System.Text.UTF8Encoding]::new($false))
    $schema10Snapshot = [IND_CRM_API.Services.HelpKnowledgeStore]::LoadForValidation($schema10BundleValidationPath)
    $schema10StructurePassed = $schema10Snapshot.Bundle.schemaVersion -ceq '1.0' -and
        @($schema10Snapshot.Bundle.modules | Where-Object { $_.localizations.Count -ne 0 }).Count -eq 0 -and
        @($schema10Snapshot.Bundle.topics | Where-Object { $_.localizations.Count -ne 0 }).Count -eq 0

    $schema10CatalogResults = foreach ($locale in $acceptedRequestLocales) {
        $projection = [IND_CRM_API.Services.HelpKnowledgeProjection]::ToCatalog($schema10Snapshot, $locale)
        [pscustomobject]@{
            Locale = $locale
            Passed = Test-HelpCatalogProjection `
                $projection `
                $schema10Snapshot `
                $schema10Snapshot.Bundle.defaultLocale
        }
    }
    $schema10CatalogCount = @($schema10CatalogResults).Count
    $schema10CatalogPassedCount = @($schema10CatalogResults | Where-Object Passed).Count
    $schema10CatalogRate = $schema10CatalogPassedCount / [math]::Max(1, $schema10CatalogCount)

    $schema10TopicResults = foreach ($locale in $acceptedRequestLocales) {
        foreach ($topic in $schema10Snapshot.Bundle.topics) {
            $projection = [IND_CRM_API.Services.HelpKnowledgeProjection]::ToTopic($schema10Snapshot, $topic, $locale)
            [pscustomobject]@{
                Locale = $locale
                TopicId = $topic.id
                Passed = Test-HelpTopicProjection `
                    $projection `
                    $topic `
                    $null `
                    $schema10Snapshot.Bundle.defaultLocale
            }
        }
    }
    $schema10TopicCount = @($schema10TopicResults).Count
    $schema10TopicPassedCount = @($schema10TopicResults | Where-Object Passed).Count
    $schema10TopicRate = $schema10TopicPassedCount / [math]::Max(1, $schema10TopicCount)

    $results | Format-Table -AutoSize
    Write-Host ('Cases={0} TopicCases={1} Resolution={2:P2} Top1={3:P2} RecallAt5={4:P2}' -f $count, $topicCases.Count, $resolutionRate, $top1Rate, $recallRate)
    Write-Host ('MenuExact={0:P2} ({1}/{2}) MissingTopic={3} ManualOnlyHidden={4} InternalSupport={5}' -f `
        $menuExactRate, $menuExactPassedCount, $menuExactCount, `
        $(if ($missingTopicPassed) { 'Passed' } else { 'Failed' }), `
        $(if ($manualOnlyHiddenPassed) { 'Passed' } else { 'Failed' }), `
        $(if ($chatSupportAttachedPassed) { 'Passed' } else { 'Failed' }))
    Write-Host ('ModuleScope={0:P2} ({1}/{2}) MissingModule={3} MismatchedSelection={4} BroadModule={5}' -f `
        $moduleScopeRate, $moduleScopePassedCount, $moduleScopeCount, `
        $(if ($missingModulePassed) { 'Passed' } else { 'Failed' }), `
        $(if ($mismatchedSelectionPassed) { 'Passed' } else { 'Failed' }), `
        $(if ($broadModulePassed) { 'Passed' } else { 'Failed' }))
    Write-Host ('ModuleNoLexicalMatch={0} ModuleAiCompleteContext={1} ModuleAiOversizeRejected={2} ModuleAiResolutionContract={3}' -f `
        $(if ($moduleNoMatchPassed) { 'Passed' } else { 'Failed' }), `
        $(if ($moduleAiContextPassed) { 'Passed' } else { 'Failed' }), `
        $(if ($moduleAiOversizePassed) { 'Passed' } else { 'Failed' }), `
        $(if ($moduleAiResolutionPassed) { 'Passed' } else { 'Failed' }))
    Write-Host ('VerbatimOffsetCopy={0} ShortUiLabelAllowed={1} RewriteRequiredError={2}' -f `
        $(if ($offsetCopyDetected) { 'Passed' } else { 'Failed' }), `
        $(if (-not $shortLabelRejected) { 'Passed' } else { 'Failed' }), `
        $(if ($rewriteRequiredPassed) { 'Passed' } else { 'Failed' }))
    Write-Host ('BundleStructure={0} SpanishFallbackCatalog={1:P2} ({2}/{3}) SpanishFallbackTopics={4:P2} ({5}/{6})' -f `
        $(if ($bundleStructurePassed) { 'Passed' } else { 'Failed' }), `
        $bundleCatalogRate, $bundleCatalogPassedCount, $bundleCatalogCount, `
        $bundleTopicRate, $bundleTopicPassedCount, $bundleTopicCount)
    Write-Host ('Schema10Structure={0} Schema10Catalog={1:P2} ({2}/{3}) Schema10Topics={4:P2} ({5}/{6})' -f `
        $(if ($schema10StructurePassed) { 'Passed' } else { 'Failed' }), `
        $schema10CatalogRate, $schema10CatalogPassedCount, $schema10CatalogCount, `
        $schema10TopicRate, $schema10TopicPassedCount, $schema10TopicCount)
    if ($resolutionRate -lt 1.0 -or $top1Rate -lt $MinimumTop1 -or $recallRate -lt $MinimumRecallAt5 -or
        $menuExactRate -lt 1.0 -or -not $missingTopicPassed -or -not $manualOnlyHiddenPassed -or
        -not $chatSupportAttachedPassed -or $moduleScopeRate -lt 1.0 -or
        -not $missingModulePassed -or -not $mismatchedSelectionPassed -or -not $broadModulePassed -or
        -not $moduleNoMatchPassed -or -not $moduleAiContextPassed -or -not $moduleAiOversizePassed -or
        -not $moduleAiResolutionPassed -or
        -not $verbatimGuardPassed -or -not $rewriteRequiredPassed -or -not $bundleStructurePassed -or
        $bundleCatalogRate -lt 1.0 -or $bundleTopicRate -lt 1.0 -or
        -not $schema10StructurePassed -or $schema10CatalogRate -lt 1.0 -or $schema10TopicRate -lt 1.0) {
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
        $manualOnlySelectionResults | Where-Object { -not $_.Passed } | ForEach-Object {
            Write-Host ('- ManualOnlyHidden failed: {0}' -f $_.TopicId)
        }
        if (-not $chatSupportAttachedPassed) {
            Write-Host '- Internal troubleshooting support was not attached to a visible chatbot topic.'
        }
        $moduleScopeResults | Where-Object { -not $_.Passed } | ForEach-Object {
            Write-Host ('- ModuleScope failed: {0}; resolution={1}; mode={2}; topics={3}/{4}; support={5}/{6}; outside={7}/{8}; candidates={9}' -f `
                $_.ModuleId, $_.Resolution, $_.Mode, $_.ActualTopicIds, $_.ExpectedTopicIds, `
                $_.ActualSupportingTopicIds, $_.ExpectedSupportingTopicIds, $_.OutsideTopicCount, `
                $_.OutsideRankingCount, $_.CandidateCount)
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
        if (-not $moduleNoMatchPassed) {
            Write-Host '- A selected module question without lexical matches did not reach the AI scope.'
        }
        if (-not $moduleAiContextPassed -or -not $moduleAiOversizePassed -or -not $moduleAiResolutionPassed) {
            Write-Host ('- Module AI context failed: {0}' -f $moduleAiContextFailure)
        }
        if (-not $verbatimGuardPassed) {
            Write-Host '- Verbatim guard failed the offset-copy or short-label regression.'
        }
        if (-not $rewriteRequiredPassed) {
            Write-Host '- Rewrite-required answers are not separated from provider outages.'
        }
        if (-not $bundleStructurePassed) {
            Write-Host '- BundleStructure failed: expected schema 1.1 with Spanish-only locale coverage.'
        }
        $bundleCatalogResults | Where-Object { -not $_.Passed } | ForEach-Object {
            Write-Host ('- SpanishFallbackCatalog failed: requested={0}' -f $_.Locale)
        }
        $bundleTopicResults | Where-Object { -not $_.Passed } | ForEach-Object {
            Write-Host ('- SpanishFallbackTopics failed: requested={0} topic={1}' -f `
                $_.Locale, $_.TopicId)
        }
        if (-not $schema10StructurePassed) {
            Write-Host '- Schema10Structure failed: schema or localization-map compatibility changed.'
        }
        $schema10CatalogResults | Where-Object { -not $_.Passed } | ForEach-Object {
            Write-Host ('- Schema10Catalog failed: {0}' -f $_.Locale)
        }
        $schema10TopicResults | Where-Object { -not $_.Passed } | ForEach-Object {
            Write-Host ('- Schema10Topics failed: {0}/{1}' -f $_.Locale, $_.TopicId)
        }
        exit 1
    }
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($schema10BundleValidationPath) -and
        [System.IO.File]::Exists($schema10BundleValidationPath)) {
        [System.IO.File]::Delete($schema10BundleValidationPath)
    }
    [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
