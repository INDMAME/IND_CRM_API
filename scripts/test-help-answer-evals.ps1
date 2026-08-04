<#
.SYNOPSIS
Runs authenticated CRM help answer evaluation cases sequentially.

.DESCRIPTION
Validates the evaluation corpus and, unless ValidateOnly is specified, calls the
direct CRM help ask endpoint. The bearer token is read only from the named
process environment variable and is never included in generated reports.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ApiBaseUrl,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$CasesPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory,

    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')]
    [string]$TokenEnvironmentVariable = 'INDCRM_HELP_EVAL_BEARER_TOKEN',

    [string]$CaseId,

    [ValidateRange(1, 600)]
    [int]$TimeoutSeconds = 120,

    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$supportedLocales = @('es-ES', 'eu-ES', 'en', 'pt', 'it', 'zh-Hans')
$supportedResolutions = @('answered', 'needsSelection', 'notDocumented')
$idPattern = '^[a-z0-9][a-z0-9._-]{0,199}$'

function Test-ObjectProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object]$InputObject,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    return $null -ne $InputObject.PSObject.Properties[$Name]
}

function Get-ObjectPropertyValue {
    param(
        [Parameter(Mandatory = $true)]
        [object]$InputObject,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

function Get-RequiredCaseString {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Case,

        [Parameter(Mandatory = $true)]
        [string]$CaseLabel,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [int]$MaximumLength = 0
    )

    if (-not (Test-ObjectProperty -InputObject $Case -Name $Name)) {
        throw "Evaluation case '$CaseLabel' is missing '$Name'."
    }
    $value = Get-ObjectPropertyValue -InputObject $Case -Name $Name
    if (-not ($value -is [string]) -or [string]::IsNullOrWhiteSpace([string]$value)) {
        throw "Evaluation case '$CaseLabel' must define '$Name' as a non-empty string."
    }
    $text = ([string]$value).Trim()
    if ($MaximumLength -gt 0 -and $text.Length -gt $MaximumLength) {
        throw "Evaluation case '$CaseLabel' exceeds the $MaximumLength character limit for '$Name'."
    }
    return $text
}

function Get-CaseStringArray {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Case,

        [Parameter(Mandatory = $true)]
        [string]$CaseLabel,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [switch]$RequireAtLeastOne
    )

    if (-not (Test-ObjectProperty -InputObject $Case -Name $Name)) {
        throw "Evaluation case '$CaseLabel' is missing '$Name'."
    }

    # Read the property directly so PowerShell does not unwrap a one-item JSON array.
    $rawValue = $Case.PSObject.Properties[$Name].Value
    if ($rawValue -is [string]) {
        throw "Evaluation case '$CaseLabel' must define '$Name' as a JSON array, not a string."
    }

    $items = @()
    if ($null -ne $rawValue) {
        foreach ($item in @($rawValue)) {
            if (-not ($item -is [string]) -or [string]::IsNullOrWhiteSpace([string]$item)) {
                throw "Evaluation case '$CaseLabel' contains an invalid value in '$Name'."
            }
            $items += ([string]$item).Trim()
        }
    }

    if ($RequireAtLeastOne -and $items.Count -eq 0) {
        throw "Evaluation case '$CaseLabel' must contain at least one value in '$Name'."
    }
    if (@($items | Select-Object -Unique).Count -ne $items.Count) {
        throw "Evaluation case '$CaseLabel' contains duplicate values in '$Name'."
    }
    return $items
}

function ConvertTo-NormalizedCase {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Case,

        [Parameter(Mandatory = $true)]
        [int]$Index
    )

    $caseLabel = "at index $Index"
    $id = Get-RequiredCaseString -Case $Case -CaseLabel $caseLabel -Name 'id' -MaximumLength 200
    $caseLabel = $id
    if ($id -cnotmatch $idPattern) {
        throw "Evaluation case '$id' has an invalid id. Use lowercase letters, numbers, dots, underscores, and hyphens."
    }

    $question = Get-RequiredCaseString -Case $Case -CaseLabel $caseLabel -Name 'question' -MaximumLength 1200
    $locale = Get-RequiredCaseString -Case $Case -CaseLabel $caseLabel -Name 'responseLocale' -MaximumLength 20
    if ($supportedLocales -cnotcontains $locale) {
        throw "Evaluation case '$id' has unsupported responseLocale '$locale'."
    }

    $resolution = Get-RequiredCaseString -Case $Case -CaseLabel $caseLabel -Name 'expectedResolution' -MaximumLength 40
    if ($supportedResolutions -cnotcontains $resolution) {
        throw "Evaluation case '$id' has unsupported expectedResolution '$resolution'."
    }

    $expectedTopicIds = @(Get-CaseStringArray -Case $Case -CaseLabel $caseLabel -Name 'expectedTopicIds')
    $sourceChunkIds = @(Get-CaseStringArray -Case $Case -CaseLabel $caseLabel -Name 'sourceChunkIds')
    $requiredSourceChunkIds = @(Get-CaseStringArray -Case $Case -CaseLabel $caseLabel -Name 'requiredSourceChunkIds')
    $requiredFacts = @(Get-CaseStringArray -Case $Case -CaseLabel $caseLabel -Name 'requiredFacts' -RequireAtLeastOne)
    $forbiddenClaims = @(Get-CaseStringArray -Case $Case -CaseLabel $caseLabel -Name 'forbiddenClaims' -RequireAtLeastOne)

    foreach ($topicId in $expectedTopicIds) {
        if ($topicId -cnotmatch $idPattern) {
            throw "Evaluation case '$id' contains invalid expected topic id '$topicId'."
        }
    }
    foreach ($chunkId in $sourceChunkIds + $requiredSourceChunkIds) {
        if ($chunkId -cnotmatch $idPattern) {
            throw "Evaluation case '$id' contains invalid source chunk id '$chunkId'."
        }
    }
    foreach ($requiredChunkId in $requiredSourceChunkIds) {
        if ($sourceChunkIds -cnotcontains $requiredChunkId) {
            throw "Evaluation case '$id' requires chunk '$requiredChunkId' but does not include it in sourceChunkIds."
        }
    }

    $expectedTopicId = $null
    if (Test-ObjectProperty -InputObject $Case -Name 'expectedTopicId') {
        $rawExpectedTopicId = Get-ObjectPropertyValue -InputObject $Case -Name 'expectedTopicId'
        if ($null -ne $rawExpectedTopicId -and -not [string]::IsNullOrWhiteSpace([string]$rawExpectedTopicId)) {
            if (-not ($rawExpectedTopicId -is [string])) {
                throw "Evaluation case '$id' must define expectedTopicId as a string or null."
            }
            $expectedTopicId = ([string]$rawExpectedTopicId).Trim()
            if ($expectedTopicIds -cnotcontains $expectedTopicId) {
                throw "Evaluation case '$id' has expectedTopicId '$expectedTopicId' outside expectedTopicIds."
            }
        }
    }

    $selectedTopicId = $null
    if (Test-ObjectProperty -InputObject $Case -Name 'selectedTopicId') {
        $rawSelectedTopicId = Get-ObjectPropertyValue -InputObject $Case -Name 'selectedTopicId'
        if ($null -ne $rawSelectedTopicId -and -not [string]::IsNullOrWhiteSpace([string]$rawSelectedTopicId)) {
            if (-not ($rawSelectedTopicId -is [string])) {
                throw "Evaluation case '$id' must define selectedTopicId as a string or null."
            }
            $selectedTopicId = ([string]$rawSelectedTopicId).Trim()
            if ($selectedTopicId -cnotmatch $idPattern) {
                throw "Evaluation case '$id' has invalid selectedTopicId '$selectedTopicId'."
            }
        }
    }

    if ($resolution -eq 'answered') {
        if ($expectedTopicIds.Count -eq 0) {
            throw "Evaluation case '$id' must define expectedTopicIds for an answered result."
        }
        if ($requiredSourceChunkIds.Count -eq 0) {
            throw "Evaluation case '$id' must define requiredSourceChunkIds for an answered result."
        }
    }
    else {
        if ($expectedTopicIds.Count -ne 0 -or $sourceChunkIds.Count -ne 0 -or $requiredSourceChunkIds.Count -ne 0) {
            throw "Evaluation case '$id' must keep topic and source expectations empty when expectedResolution is '$resolution'."
        }
    }

    return [pscustomobject][ordered]@{
        Id = $id
        Question = $question
        ResponseLocale = $locale
        SelectedTopicId = $selectedTopicId
        ExpectedResolution = $resolution
        ExpectedTopicIds = @($expectedTopicIds)
        SourceChunkIds = @($sourceChunkIds)
        RequiredSourceChunkIds = @($requiredSourceChunkIds)
        RequiredFacts = @($requiredFacts)
        ForbiddenClaims = @($forbiddenClaims)
    }
}

function Resolve-ApiEndpoint {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl
    )

    $candidate = $null
    if (-not [uri]::TryCreate($BaseUrl.Trim(), [UriKind]::Absolute, [ref]$candidate)) {
        throw 'ApiBaseUrl must be an absolute HTTP or HTTPS origin.'
    }
    if ($candidate.Scheme -notin @('http', 'https')) {
        throw 'ApiBaseUrl must use HTTP or HTTPS.'
    }
    if (-not [string]::IsNullOrEmpty($candidate.UserInfo) -or
        -not [string]::IsNullOrEmpty($candidate.Query) -or
        -not [string]::IsNullOrEmpty($candidate.Fragment)) {
        throw 'ApiBaseUrl cannot contain credentials, a query string, or a fragment.'
    }
    if ($candidate.AbsolutePath -ne '/') {
        throw 'ApiBaseUrl must be an origin only, without an application path.'
    }
    if ($candidate.Scheme -eq 'http' -and -not $candidate.IsLoopback) {
        throw 'ApiBaseUrl must use HTTPS unless the target is a loopback address.'
    }

    return [uri]::new($candidate, '/api/ia/service/help/ask')
}

function Resolve-OutputDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
        throw "OutputDirectory points to a file: $fullPath"
    }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $fullPath)
    }
    return (Resolve-Path -LiteralPath $fullPath).Path
}

function Get-SafeErrorText {
    param(
        [AllowNull()]
        [object]$Value
    )

    if ($null -eq $Value) {
        return $null
    }
    $text = [string]$Value
    $text = [regex]::Replace($text, '(?i)Bearer\s+[^\s,;]+', 'Bearer [REDACTED]')
    if ($text.Length -gt 600) {
        return $text.Substring(0, 600) + '...'
    }
    return $text
}

function New-CaseReportResult {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Case,

        [Parameter(Mandatory = $true)]
        [string]$ExecutionStatus,

        [AllowNull()]
        [object]$HttpStatusCode,

        [long]$DurationMilliseconds,

        [AllowNull()]
        [string]$ClientInteractionId,

        [Parameter(Mandatory = $true)]
        [bool]$StructuralPassed,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$StructuralFailures,

        [AllowNull()]
        [object]$Response,

        [AllowNull()]
        [string]$TraceId,

        [AllowNull()]
        [string]$EnvelopeMessage,

        [AllowNull()]
        [string]$EnvelopeErrorCode,

        [Parameter(Mandatory = $true)]
        [string]$SemanticReviewStatus
    )

    return [pscustomobject][ordered]@{
        CaseId = $Case.Id
        Question = $Case.Question
        ExpectedResolution = $Case.ExpectedResolution
        ExpectedTopicIds = @($Case.ExpectedTopicIds)
        RequiredSourceChunkIds = @($Case.RequiredSourceChunkIds)
        RequestedLocale = $Case.ResponseLocale
        RequiredFacts = @($Case.RequiredFacts)
        ForbiddenClaims = @($Case.ForbiddenClaims)
        ExecutionStatus = $ExecutionStatus
        HttpStatusCode = $HttpStatusCode
        DurationMilliseconds = $DurationMilliseconds
        ClientInteractionId = $ClientInteractionId
        TraceId = $TraceId
        EnvelopeMessage = $EnvelopeMessage
        EnvelopeErrorCode = $EnvelopeErrorCode
        StructuralPassed = $StructuralPassed
        StructuralFailures = @($StructuralFailures)
        SemanticReviewStatus = $SemanticReviewStatus
        Response = $Response
    }
}

function Invoke-AnswerCase {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Case,

        [Parameter(Mandatory = $true)]
        [uri]$Endpoint,

        [Parameter(Mandatory = $true)]
        [System.Net.Http.HttpClient]$HttpClient
    )

    $clientInteractionId = [guid]::NewGuid().ToString('D')
    $requestBody = [ordered]@{
        question = $Case.Question
        responseLocale = $Case.ResponseLocale
        history = @()
        clientInteractionId = $clientInteractionId
    }
    if (-not [string]::IsNullOrWhiteSpace($Case.SelectedTopicId)) {
        $requestBody['selectedTopicId'] = $Case.SelectedTopicId
    }

    $failures = New-Object 'System.Collections.Generic.List[string]'
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $httpStatusCode = $null
    $traceId = $null
    $envelopeMessage = $null
    $envelopeErrorCode = $null
    $safeResponse = $null
    $executionStatus = 'completed'
    $content = $null
    $httpResponse = $null

    try {
        $jsonBody = $requestBody | ConvertTo-Json -Depth 5 -Compress
        $content = [System.Net.Http.StringContent]::new($jsonBody, [Text.Encoding]::UTF8, 'application/json')
        $httpResponse = $HttpClient.PostAsync($Endpoint, $content).GetAwaiter().GetResult()
        $httpStatusCode = [int]$httpResponse.StatusCode
        $responseText = $httpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()

        if ([string]::IsNullOrWhiteSpace($responseText)) {
            [void]$failures.Add('The endpoint returned an empty response body.')
        }
        else {
            try {
                $envelope = $responseText | ConvertFrom-Json
            }
            catch {
                $envelope = $null
                [void]$failures.Add('The endpoint response is not valid JSON.')
            }

            if ($null -ne $envelope) {
                $traceId = Get-SafeErrorText (Get-ObjectPropertyValue -InputObject $envelope -Name 'TraceId')
                $envelopeMessage = Get-SafeErrorText (Get-ObjectPropertyValue -InputObject $envelope -Name 'Message')
                $envelopeErrorCode = Get-SafeErrorText (Get-ObjectPropertyValue -InputObject $envelope -Name 'ErrorCode')

                if (-not (Test-ObjectProperty -InputObject $envelope -Name 'Success')) {
                    [void]$failures.Add('The API envelope is missing Success.')
                }
                else {
                    $success = Get-ObjectPropertyValue -InputObject $envelope -Name 'Success'
                    if (-not ($success -is [bool]) -or -not $success) {
                        [void]$failures.Add('The API envelope does not report Success=true.')
                    }
                }

                $data = Get-ObjectPropertyValue -InputObject $envelope -Name 'Data'
                if ($null -eq $data) {
                    [void]$failures.Add('The API envelope is missing Data.')
                }
                else {
                    $actualResolution = [string](Get-ObjectPropertyValue -InputObject $data -Name 'Resolution')
                    $actualLocale = [string](Get-ObjectPropertyValue -InputObject $data -Name 'ResponseLocale')
                    $answer = Get-ObjectPropertyValue -InputObject $data -Name 'Answer'
                    $interactionId = [string](Get-ObjectPropertyValue -InputObject $data -Name 'InteractionId')
                    $knowledgeVersion = [string](Get-ObjectPropertyValue -InputObject $data -Name 'KnowledgeVersion')
                    $model = Get-ObjectPropertyValue -InputObject $data -Name 'Model'
                    $quickAnswerUsed = Get-ObjectPropertyValue -InputObject $data -Name 'QuickAnswerUsed'
                    $rawSources = @(Get-ObjectPropertyValue -InputObject $data -Name 'Sources')
                    $rawCandidates = @(Get-ObjectPropertyValue -InputObject $data -Name 'Candidates')
                    $rawActions = @(Get-ObjectPropertyValue -InputObject $data -Name 'Actions')

                    if (-not [string]::Equals($actualResolution, $Case.ExpectedResolution, [StringComparison]::Ordinal)) {
                        [void]$failures.Add("Resolution mismatch: expected '$($Case.ExpectedResolution)', received '$actualResolution'.")
                    }
                    if (-not [string]::Equals($actualLocale, $Case.ResponseLocale, [StringComparison]::Ordinal)) {
                        [void]$failures.Add("Locale mismatch: expected '$($Case.ResponseLocale)', received '$actualLocale'.")
                    }
                    $parsedInteractionId = [guid]::Empty
                    if (-not [guid]::TryParse($interactionId, [ref]$parsedInteractionId)) {
                        [void]$failures.Add('The response InteractionId is not a valid UUID.')
                    }
                    if ([string]::IsNullOrWhiteSpace($knowledgeVersion)) {
                        [void]$failures.Add('The response is missing KnowledgeVersion.')
                    }
                    if ([string]::IsNullOrWhiteSpace([string](Get-ObjectPropertyValue -InputObject $data -Name 'FeedbackToken'))) {
                        [void]$failures.Add('The response is missing FeedbackToken.')
                    }

                    $sources = @($rawSources | ForEach-Object {
                        [pscustomobject][ordered]@{
                            TopicId = [string](Get-ObjectPropertyValue -InputObject $_ -Name 'TopicId')
                            TopicTitle = [string](Get-ObjectPropertyValue -InputObject $_ -Name 'TopicTitle')
                            ChunkId = [string](Get-ObjectPropertyValue -InputObject $_ -Name 'ChunkId')
                            Heading = [string](Get-ObjectPropertyValue -InputObject $_ -Name 'Heading')
                        }
                    })
                    foreach ($source in $sources) {
                        if ([string]::IsNullOrWhiteSpace($source.TopicId) -or [string]::IsNullOrWhiteSpace($source.ChunkId)) {
                            [void]$failures.Add('A response source is missing TopicId or ChunkId.')
                        }
                    }
                    $actualTopicIds = @($sources | ForEach-Object TopicId | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
                    $actualChunkIds = @($sources | ForEach-Object ChunkId | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)

                    if ($Case.ExpectedResolution -eq 'answered') {
                        if ([string]::IsNullOrWhiteSpace([string]$answer)) {
                            [void]$failures.Add('An answered result must contain a non-empty answer.')
                        }
                        foreach ($expectedTopicId in $Case.ExpectedTopicIds) {
                            if ($actualTopicIds -cnotcontains $expectedTopicId) {
                                [void]$failures.Add("Sources do not contain expected topic '$expectedTopicId'.")
                            }
                        }
                        foreach ($actualTopicId in $actualTopicIds) {
                            if ($Case.ExpectedTopicIds -cnotcontains $actualTopicId) {
                                [void]$failures.Add("Sources contain unexpected topic '$actualTopicId'.")
                            }
                        }
                        foreach ($requiredChunkId in $Case.RequiredSourceChunkIds) {
                            if ($actualChunkIds -cnotcontains $requiredChunkId) {
                                [void]$failures.Add("Sources do not contain required chunk '$requiredChunkId'.")
                            }
                        }
                    }
                    elseif ($sources.Count -ne 0) {
                        [void]$failures.Add("A '$($Case.ExpectedResolution)' result must not contain sources.")
                    }

                    if ($Case.ExpectedResolution -eq 'needsSelection') {
                        if (-not [string]::IsNullOrWhiteSpace([string]$answer)) {
                            [void]$failures.Add('A needsSelection result must not contain an answer.')
                        }
                        if ($rawCandidates.Count -eq 0) {
                            [void]$failures.Add('A needsSelection result must contain candidates.')
                        }
                    }
                    elseif ($Case.ExpectedResolution -eq 'notDocumented' -and -not [string]::IsNullOrWhiteSpace([string]$answer)) {
                        [void]$failures.Add('A notDocumented result must not contain an answer.')
                    }

                    $candidates = @($rawCandidates | ForEach-Object {
                        [pscustomobject][ordered]@{
                            TopicId = [string](Get-ObjectPropertyValue -InputObject $_ -Name 'TopicId')
                            Title = [string](Get-ObjectPropertyValue -InputObject $_ -Name 'Title')
                        }
                    })
                    $actions = @($rawActions | ForEach-Object {
                        [pscustomobject][ordered]@{
                            Type = [string](Get-ObjectPropertyValue -InputObject $_ -Name 'Type')
                            RouteKey = [string](Get-ObjectPropertyValue -InputObject $_ -Name 'RouteKey')
                            Label = [string](Get-ObjectPropertyValue -InputObject $_ -Name 'Label')
                        }
                    })

                    # Project an allowlist and deliberately omit FeedbackToken from disk reports.
                    $safeResponse = [pscustomobject][ordered]@{
                        InteractionId = $interactionId
                        Resolution = $actualResolution
                        Answer = if ($null -eq $answer) { $null } else { [string]$answer }
                        ResponseLocale = $actualLocale
                        KnowledgeVersion = $knowledgeVersion
                        QuickAnswerUsed = $quickAnswerUsed
                        Model = if ($null -eq $model) { $null } else { [string]$model }
                        Sources = @($sources)
                        Candidates = @($candidates)
                        Actions = @($actions)
                    }
                }
            }
        }

        if ($httpStatusCode -ne 200) {
            [void]$failures.Add("HTTP status mismatch: expected 200, received $httpStatusCode.")
        }
    }
    catch {
        $executionStatus = 'request-failed'
        [void]$failures.Add('Request failed: ' + (Get-SafeErrorText $_.Exception.GetBaseException().Message))
    }
    finally {
        $stopwatch.Stop()
        if ($null -ne $httpResponse) {
            $httpResponse.Dispose()
        }
        if ($null -ne $content) {
            $content.Dispose()
        }
    }

    return New-CaseReportResult `
        -Case $Case `
        -ExecutionStatus $executionStatus `
        -HttpStatusCode $httpStatusCode `
        -DurationMilliseconds $stopwatch.ElapsedMilliseconds `
        -ClientInteractionId $clientInteractionId `
        -StructuralPassed ($failures.Count -eq 0) `
        -StructuralFailures @($failures) `
        -Response $safeResponse `
        -TraceId $traceId `
        -EnvelopeMessage $envelopeMessage `
        -EnvelopeErrorCode $envelopeErrorCode `
        -SemanticReviewStatus $(if ($null -ne $safeResponse) { 'pending-human-review' } else { 'not-available' })
}

function ConvertTo-HtmlEncoded {
    param(
        [AllowNull()]
        [object]$Value
    )

    if ($null -eq $Value) {
        return ''
    }
    return [Net.WebUtility]::HtmlEncode([string]$Value)
}

function Add-HtmlList {
    param(
        [Parameter(Mandatory = $true)]
        [Text.StringBuilder]$Builder,

        [AllowEmptyCollection()]
        [object[]]$Items
    )

    if (@($Items).Count -eq 0) {
        [void]$Builder.AppendLine('<p class="muted">None.</p>')
        return
    }
    [void]$Builder.AppendLine('<ul>')
    foreach ($item in $Items) {
        [void]$Builder.AppendLine('<li>' + (ConvertTo-HtmlEncoded $item) + '</li>')
    }
    [void]$Builder.AppendLine('</ul>')
}

function Write-EvaluationReports {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Report,

        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffZ', [Globalization.CultureInfo]::InvariantCulture)
    $jsonPath = Join-Path $Directory "help-answer-evals-$stamp.json"
    $htmlPath = Join-Path $Directory "help-answer-evals-$stamp.html"
    $utf8WithoutBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($jsonPath, ($Report | ConvertTo-Json -Depth 12), $utf8WithoutBom)

    $builder = [Text.StringBuilder]::new()
    [void]$builder.AppendLine('<!doctype html>')
    [void]$builder.AppendLine('<html lang="en"><head><meta charset="utf-8">')
    [void]$builder.AppendLine('<meta name="viewport" content="width=device-width,initial-scale=1">')
    [void]$builder.AppendLine('<title>CRM Help Answer Evaluation</title>')
    [void]$builder.AppendLine('<style>body{font-family:Segoe UI,Arial,sans-serif;margin:0;background:#f4f6f8;color:#18212b}main{max-width:1100px;margin:auto;padding:24px}.notice{padding:16px;background:#fff4ce;border-left:5px solid #d69e00}.summary,article{margin-top:18px;padding:18px;background:#fff;border:1px solid #d9e0e7;border-radius:8px}article.pass{border-left:5px solid #248b49}article.fail{border-left:5px solid #c42b1c}.muted{color:#586675}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f6f8fa;padding:12px;border-radius:6px}table{width:100%;border-collapse:collapse}th,td{text-align:left;vertical-align:top;padding:8px;border-bottom:1px solid #e4e9ee}code{overflow-wrap:anywhere}</style>')
    [void]$builder.AppendLine('</head><body><main>')
    [void]$builder.AppendLine('<h1>CRM Help Answer Evaluation</h1>')
    [void]$builder.AppendLine('<div class="notice"><strong>Human review required.</strong> Structural checks do not approve factual completeness, forbidden claims, semantic correctness, citation meaning, or translation quality.</div>')
    [void]$builder.AppendLine('<section class="summary"><h2>Run summary</h2>')
    [void]$builder.AppendLine('<p><strong>Mode:</strong> ' + (ConvertTo-HtmlEncoded $Report.Mode) + '<br>')
    [void]$builder.AppendLine('<strong>Generated UTC:</strong> ' + (ConvertTo-HtmlEncoded $Report.GeneratedAtUtc) + '<br>')
    [void]$builder.AppendLine('<strong>Endpoint:</strong> <code>' + (ConvertTo-HtmlEncoded $Report.ApiEndpoint) + '</code><br>')
    [void]$builder.AppendLine('<strong>Cases:</strong> ' + (ConvertTo-HtmlEncoded $Report.Summary.SelectedCases) +
        '; <strong>structural pass:</strong> ' + (ConvertTo-HtmlEncoded $Report.Summary.StructuralPassed) +
        '; <strong>structural fail:</strong> ' + (ConvertTo-HtmlEncoded $Report.Summary.StructuralFailed) +
        '; <strong>human review pending:</strong> ' + (ConvertTo-HtmlEncoded $Report.Summary.SemanticReviewsPending) + '</p></section>')

    foreach ($result in @($Report.Results)) {
        $cssClass = if ($result.StructuralPassed) { 'pass' } else { 'fail' }
        [void]$builder.AppendLine('<article class="' + $cssClass + '">')
        [void]$builder.AppendLine('<h2>' + (ConvertTo-HtmlEncoded $result.CaseId) + '</h2>')
        [void]$builder.AppendLine('<p><strong>Question:</strong> ' + (ConvertTo-HtmlEncoded $result.Question) + '<br>')
        [void]$builder.AppendLine('<strong>Structural result:</strong> ' + $(if ($result.StructuralPassed) { 'PASS' } else { 'FAIL' }) + '<br>')
        [void]$builder.AppendLine('<strong>Semantic review:</strong> ' + (ConvertTo-HtmlEncoded $result.SemanticReviewStatus) + '<br>')
        [void]$builder.AppendLine('<strong>Expected:</strong> ' + (ConvertTo-HtmlEncoded $result.ExpectedResolution) + ' / ' + (ConvertTo-HtmlEncoded $result.RequestedLocale) + '<br>')
        [void]$builder.AppendLine('<strong>HTTP:</strong> ' + (ConvertTo-HtmlEncoded $result.HttpStatusCode) +
            '; <strong>duration:</strong> ' + (ConvertTo-HtmlEncoded $result.DurationMilliseconds) + ' ms</p>')

        [void]$builder.AppendLine('<h3>Structural failures</h3>')
        Add-HtmlList -Builder $builder -Items @($result.StructuralFailures)
        [void]$builder.AppendLine('<h3>Response</h3>')
        if ($null -eq $result.Response) {
            [void]$builder.AppendLine('<p class="muted">No reviewable response was captured.</p>')
        }
        else {
            [void]$builder.AppendLine('<p><strong>Actual:</strong> ' + (ConvertTo-HtmlEncoded $result.Response.Resolution) + ' / ' + (ConvertTo-HtmlEncoded $result.Response.ResponseLocale) + '</p>')
            [void]$builder.AppendLine('<pre>' + (ConvertTo-HtmlEncoded $result.Response.Answer) + '</pre>')
            [void]$builder.AppendLine('<h4>Sources</h4><table><thead><tr><th>Topic</th><th>Chunk</th><th>Heading</th></tr></thead><tbody>')
            foreach ($source in @($result.Response.Sources)) {
                [void]$builder.AppendLine('<tr><td>' + (ConvertTo-HtmlEncoded $source.TopicId) + '</td><td>' + (ConvertTo-HtmlEncoded $source.ChunkId) + '</td><td>' + (ConvertTo-HtmlEncoded $source.Heading) + '</td></tr>')
            }
            [void]$builder.AppendLine('</tbody></table>')
        }

        [void]$builder.AppendLine('<h3>Required facts - review manually</h3>')
        Add-HtmlList -Builder $builder -Items @($result.RequiredFacts)
        [void]$builder.AppendLine('<h3>Forbidden claims - review manually</h3>')
        Add-HtmlList -Builder $builder -Items @($result.ForbiddenClaims)
        [void]$builder.AppendLine('</article>')
    }
    [void]$builder.AppendLine('</main></body></html>')
    [IO.File]::WriteAllText($htmlPath, $builder.ToString(), $utf8WithoutBom)

    return [pscustomobject]@{
        JsonPath = $jsonPath
        HtmlPath = $htmlPath
    }
}

$endpoint = Resolve-ApiEndpoint -BaseUrl $ApiBaseUrl
$resolvedCasesPath = (Resolve-Path -LiteralPath $CasesPath -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $resolvedCasesPath -PathType Leaf)) {
    throw "CasesPath does not point to a file: $resolvedCasesPath"
}

# Read UTF-8 explicitly because Windows PowerShell 5.1 treats BOM-less files as ANSI.
$caseJson = [IO.File]::ReadAllText($resolvedCasesPath, [Text.Encoding]::UTF8)
if ([string]::IsNullOrWhiteSpace($caseJson) -or -not $caseJson.TrimStart().StartsWith('[')) {
    throw 'CasesPath must contain a non-empty JSON array.'
}
try {
    $parsedCases = $caseJson | ConvertFrom-Json
}
catch {
    throw "CasesPath contains invalid JSON: $($_.Exception.Message)"
}
$rawCases = @($parsedCases)
if ($rawCases.Count -eq 0) {
    throw 'No answer evaluation cases were found.'
}

$normalizedCases = @()
for ($index = 0; $index -lt $rawCases.Count; $index++) {
    $normalizedCases += ConvertTo-NormalizedCase -Case $rawCases[$index] -Index $index
}
$duplicateIds = @($normalizedCases | Group-Object -Property Id -CaseSensitive | Where-Object Count -gt 1)
if ($duplicateIds.Count -ne 0) {
    throw 'Evaluation case ids must be unique: ' + (($duplicateIds | ForEach-Object Name) -join ', ')
}

if (-not [string]::IsNullOrWhiteSpace($CaseId)) {
    $selectedCases = @($normalizedCases | Where-Object { $_.Id -ceq $CaseId.Trim() })
    if ($selectedCases.Count -ne 1) {
        throw "CaseId '$CaseId' did not match exactly one evaluation case."
    }
}
else {
    $selectedCases = @($normalizedCases)
}

$resolvedOutputDirectory = Resolve-OutputDirectory -Path $OutputDirectory
$results = @()
$httpClient = $null
$httpHandler = $null

if ($ValidateOnly) {
    foreach ($case in $selectedCases) {
        $results += New-CaseReportResult `
            -Case $case `
            -ExecutionStatus 'not-run-validate-only' `
            -HttpStatusCode $null `
            -DurationMilliseconds 0 `
            -ClientInteractionId $null `
            -StructuralPassed $true `
            -StructuralFailures @() `
            -Response $null `
            -TraceId $null `
            -EnvelopeMessage $null `
            -EnvelopeErrorCode $null `
            -SemanticReviewStatus 'not-run'
    }
}
else {
    $bearerToken = [Environment]::GetEnvironmentVariable($TokenEnvironmentVariable, [EnvironmentVariableTarget]::Process)
    if ([string]::IsNullOrWhiteSpace($bearerToken)) {
        throw "The process environment variable '$TokenEnvironmentVariable' is not set."
    }
    if ($bearerToken.StartsWith('Bearer ', [StringComparison]::OrdinalIgnoreCase)) {
        throw "The process environment variable '$TokenEnvironmentVariable' must contain the raw token without the Bearer prefix."
    }

    Add-Type -AssemblyName System.Net.Http
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $httpHandler = [System.Net.Http.HttpClientHandler]::new()
    $httpHandler.AllowAutoRedirect = $false
    $httpClient = [System.Net.Http.HttpClient]::new($httpHandler)
    $httpClient.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
    $httpClient.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $bearerToken)
    $httpClient.DefaultRequestHeaders.Accept.Add([System.Net.Http.Headers.MediaTypeWithQualityHeaderValue]::new('application/json'))
    $bearerToken = $null

    try {
        foreach ($case in $selectedCases) {
            Write-Host ("Running answer evaluation case: {0}" -f $case.Id)
            $results += Invoke-AnswerCase -Case $case -Endpoint $endpoint -HttpClient $httpClient
        }
    }
    finally {
        if ($null -ne $httpClient) {
            $httpClient.Dispose()
        }
        if ($null -ne $httpHandler) {
            $httpHandler.Dispose()
        }
    }
}

$structuralPassed = @($results | Where-Object StructuralPassed).Count
$structuralFailed = @($results | Where-Object { -not $_.StructuralPassed }).Count
$semanticReviewsPending = @($results | Where-Object { $_.SemanticReviewStatus -eq 'pending-human-review' }).Count
$report = [pscustomobject][ordered]@{
    SchemaVersion = '1.0'
    GeneratedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    Mode = if ($ValidateOnly) { 'validate-only' } else { 'authenticated-evaluation' }
    ApiEndpoint = $endpoint.AbsoluteUri
    CasesPath = $resolvedCasesPath
    CaseFilter = if ([string]::IsNullOrWhiteSpace($CaseId)) { $null } else { $CaseId.Trim() }
    TokenIncluded = $false
    SemanticChecksAutomated = $false
    Summary = [pscustomobject][ordered]@{
        CorpusCases = $normalizedCases.Count
        SelectedCases = $selectedCases.Count
        ExecutedCases = if ($ValidateOnly) { 0 } else { $results.Count }
        StructuralPassed = $structuralPassed
        StructuralFailed = $structuralFailed
        SemanticReviewsPending = $semanticReviewsPending
    }
    Results = @($results)
}

$reportPaths = Write-EvaluationReports -Report $report -Directory $resolvedOutputDirectory
Write-Host ("CorpusCases={0} SelectedCases={1} ExecutedCases={2} StructuralPassed={3} StructuralFailed={4} HumanReviewsPending={5}" -f `
    $report.Summary.CorpusCases,
    $report.Summary.SelectedCases,
    $report.Summary.ExecutedCases,
    $report.Summary.StructuralPassed,
    $report.Summary.StructuralFailed,
    $report.Summary.SemanticReviewsPending)
Write-Host ("JSON report: {0}" -f $reportPaths.JsonPath)
Write-Host ("HTML report: {0}" -f $reportPaths.HtmlPath)

if ($structuralFailed -ne 0) {
    exit 1
}
