<#
.SYNOPSIS
Creates private CSV and HTML CRM help reports from metrics-only NDJSON files.
#>
param(
    [string]$AnalyticsPath = 'C:\INDData\CRMHelpAnalytics',

    [ValidateSet('Weekly', 'Monthly')]
    [string]$Period = 'Weekly',

    [datetime]$AsOfUtc = [datetime]::UtcNow,

    [string]$OutputDirectory,

    [switch]$IncludeReviewQueue
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath($AnalyticsPath)
$eventsDirectory = Join-Path $root 'events'
if (-not (Test-Path -LiteralPath $eventsDirectory)) {
    throw "Metrics directory not found: $eventsDirectory"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'aggregates'
}
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null

$endUtc = $AsOfUtc.ToUniversalTime()
if ($Period -eq 'Monthly') {
    $endUtc = [datetime]::SpecifyKind((Get-Date -Year $endUtc.Year -Month $endUtc.Month -Day 1), 'Utc')
    $startUtc = $endUtc.AddMonths(-1)
}
else {
    $startUtc = $endUtc.AddDays(-7)
}

$events = [System.Collections.Generic.List[object]]::new()
$invalidLines = 0
foreach ($file in Get-ChildItem -LiteralPath $eventsDirectory -Filter 'help-metrics-*.ndjson' -File) {
    foreach ($line in Get-Content -LiteralPath $file.FullName -Encoding UTF8) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try {
            $event = $line | ConvertFrom-Json
            $occurred = ([datetime]$event.occurredAtUtc).ToUniversalTime()
            if ($occurred -ge $startUtc -and $occurred -lt $endUtc) {
                $events.Add($event)
            }
        }
        catch {
            $invalidLines++
        }
    }
}

$interactions = @($events | Where-Object eventType -eq 'interaction')
$feedback = @($events | Where-Object eventType -eq 'feedback')
$rows = [System.Collections.Generic.List[object]]::new()

foreach ($group in $interactions | Group-Object resolution) {
    $rows.Add([pscustomobject]@{ Section='resolution'; Key=$group.Name; Count=$group.Count; Percent=[math]::Round(100 * $group.Count / [math]::Max(1, $interactions.Count), 2) })
}
foreach ($group in $interactions | Group-Object responseLocale) {
    $rows.Add([pscustomobject]@{ Section='locale'; Key=$group.Name; Count=$group.Count; Percent=[math]::Round(100 * $group.Count / [math]::Max(1, $interactions.Count), 2) })
}
$topicIds = foreach ($event in $interactions) { foreach ($topicId in @($event.topicIds)) { [string]$topicId } }
foreach ($group in $topicIds | Group-Object | Sort-Object Count -Descending) {
    $rows.Add([pscustomobject]@{ Section='topic'; Key=$group.Name; Count=$group.Count; Percent=[math]::Round(100 * $group.Count / [math]::Max(1, $interactions.Count), 2) })
}
$candidateTopicIds = foreach ($event in $interactions) { foreach ($topicId in @($event.candidateTopicIds)) { [string]$topicId } }
foreach ($group in $candidateTopicIds | Group-Object | Sort-Object Count -Descending) {
    $rows.Add([pscustomobject]@{ Section='ambiguous_candidate'; Key=$group.Name; Count=$group.Count; Percent=[math]::Round(100 * $group.Count / [math]::Max(1, $interactions.Count), 2) })
}
$helpfulCount = @($feedback | Where-Object helpful -eq $true).Count
$rows.Add([pscustomobject]@{ Section='feedback'; Key='helpful'; Count=$helpfulCount; Percent=[math]::Round(100 * $helpfulCount / [math]::Max(1, $feedback.Count), 2) })
foreach ($group in $feedback | Where-Object { -not $_.helpful -and $_.reason } | Group-Object reason) {
    $rows.Add([pscustomobject]@{ Section='negative_reason'; Key=$group.Name; Count=$group.Count; Percent=[math]::Round(100 * $group.Count / [math]::Max(1, $feedback.Count), 2) })
}
$quickCount = @($interactions | Where-Object quickAnswerUsed -eq $true).Count
$rows.Add([pscustomobject]@{ Section='delivery'; Key='quick_answer'; Count=$quickCount; Percent=[math]::Round(100 * $quickCount / [math]::Max(1, $interactions.Count), 2) })
$cachedInteractionCount = @($interactions | Where-Object { [int]$_.cachedInputTokens -gt 0 }).Count
$rows.Add([pscustomobject]@{ Section='delivery'; Key='prompt_cache_hit'; Count=$cachedInteractionCount; Percent=[math]::Round(100 * $cachedInteractionCount / [math]::Max(1, $interactions.Count), 2) })
$rows.Add([pscustomobject]@{ Section='quality'; Key='invalid_ndjson_lines'; Count=$invalidLines; Percent=0 })

$stamp = '{0:yyyyMMdd}-{1:yyyyMMdd}' -f $startUtc, $endUtc
$baseName = 'help-{0}-{1}' -f $Period.ToLowerInvariant(), $stamp
$csvPath = Join-Path $output ($baseName + '.csv')
$htmlPath = Join-Path $output ($baseName + '.html')
$rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8

$latencies = @($interactions | ForEach-Object { [double]$_.latencyMilliseconds } | Sort-Object)
$p95 = if ($latencies.Count -gt 0) { $latencies[[math]::Min($latencies.Count - 1, [math]::Floor($latencies.Count * 0.95))] } else { 0 }
$inputTokens = ($interactions | Measure-Object inputTokens -Sum).Sum
$outputTokens = ($interactions | Measure-Object outputTokens -Sum).Sum
$cachedTokens = ($interactions | Measure-Object cachedInputTokens -Sum).Sum
$table = $rows | ConvertTo-Html -Fragment
$html = @"
<!doctype html><html lang="es"><head><meta charset="utf-8"><title>CRM Help Analytics</title>
<style>body{font:14px system-ui;background:#101318;color:#e8edf3;margin:32px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #39414d;padding:8px;text-align:left}th{background:#202630}.cards{display:flex;gap:12px;flex-wrap:wrap}.card{background:#191e25;padding:14px;border-radius:8px}</style></head><body>
<h1>CRM Help Analytics - $Period</h1><p>UTC: $($startUtc.ToString('u')) - $($endUtc.ToString('u'))</p>
<div class="cards"><div class="card">Interactions: $($interactions.Count)</div><div class="card">Feedback: $($feedback.Count)</div><div class="card">Latency p95: $p95 ms</div><div class="card">Tokens in/out/cached: $inputTokens / $outputTokens / $cachedTokens</div></div>
<h2>Aggregates</h2>$table
<p>No question text, identity, IP, email, company, or conversation content is included in this report.</p>
</body></html>
"@
[System.IO.File]::WriteAllText($htmlPath, $html, [System.Text.UTF8Encoding]::new($false))
Write-Host "CSV: $csvPath"
Write-Host "HTML: $htmlPath"

if ($IncludeReviewQueue) {
    function Protect-CsvCell {
        param([AllowNull()][object]$Value)
        if ($null -eq $Value) { return $null }
        $text = [string]$Value
        if ($text -match '^[=+\-@]') { return "'$text" }
        return $text
    }

    $reviewDirectory = Join-Path $root 'review'
    $reviewRows = [System.Collections.Generic.List[object]]::new()
    if (Test-Path -LiteralPath $reviewDirectory) {
        foreach ($file in Get-ChildItem -LiteralPath $reviewDirectory -Filter 'help-review-*.ndjson' -File) {
            foreach ($line in Get-Content -LiteralPath $file.FullName -Encoding UTF8) {
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                try {
                    $event = $line | ConvertFrom-Json
                    $occurred = ([datetime]$event.occurredAtUtc).ToUniversalTime()
                    if ($occurred -lt $startUtc -or $occurred -ge $endUtc) { continue }
                    $reviewRows.Add([pscustomobject]@{
                        OccurredAtUtc = Protect-CsvCell ($occurred.ToString('o'))
                        InteractionId = Protect-CsvCell $event.interactionId
                        EventType = Protect-CsvCell $event.eventType
                        SampleReason = Protect-CsvCell $event.sampleReason
                        Resolution = Protect-CsvCell $event.resolution
                        TopicIds = Protect-CsvCell (@($event.topicIds) -join '|')
                        CandidateTopicIds = Protect-CsvCell (@($event.candidateTopicIds) -join '|')
                        FeedbackReason = Protect-CsvCell $event.reason
                        RedactedQuestion = Protect-CsvCell $event.redactedQuestion
                        RedactedComment = Protect-CsvCell $event.redactedComment
                    })
                }
                catch {
                    $invalidLines++
                }
            }
        }
    }

    $reviewCsvPath = Join-Path $output ($baseName + '-review.csv')
    $reviewHtmlPath = Join-Path $output ($baseName + '-review.html')
    $reviewRows | Export-Csv -LiteralPath $reviewCsvPath -NoTypeInformation -Encoding UTF8
    $reviewTable = $reviewRows | ConvertTo-Html -Fragment
    $reviewHtml = @"
<!doctype html><html lang="es"><head><meta charset="utf-8"><title>CRM Help Editorial Review</title>
<style>body{font:14px system-ui;background:#101318;color:#e8edf3;margin:32px}table{border-collapse:collapse;width:100%;table-layout:fixed}th,td{border:1px solid #39414d;padding:8px;text-align:left;overflow-wrap:anywhere}th{background:#202630}</style></head><body>
<h1>CRM Help Editorial Review - $Period</h1><p>UTC: $($startUtc.ToString('u')) - $($endUtc.ToString('u'))</p>
<p>Only server-redacted problem cases and the configured success sample are included. Review before promoting any text to documentation.</p>$reviewTable
</body></html>
"@
    [System.IO.File]::WriteAllText($reviewHtmlPath, $reviewHtml, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Review CSV: $reviewCsvPath"
    Write-Host "Review HTML: $reviewHtmlPath"
}
