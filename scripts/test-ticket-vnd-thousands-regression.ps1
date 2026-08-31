param(
    [string]$AssemblyPath
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path $repoRoot "bin\x86\Debug\IND_CRM_API.exe"
}

$AssemblyPath = [System.IO.Path]::GetFullPath($AssemblyPath)
if (-not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) {
    throw "Assembly not found: $AssemblyPath"
}

# The API assembly must be loaded by a 32-bit process because it references the AX COM connector.
if ([IntPtr]::Size -ne 4) {
    $x86PowerShell = Join-Path $env:WINDIR "SysWOW64\WindowsPowerShell\v1.0\powershell.exe"
    if (-not (Test-Path -LiteralPath $x86PowerShell -PathType Leaf)) {
        throw "32-bit PowerShell not found: $x86PowerShell"
    }

    & $x86PowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -AssemblyPath $AssemblyPath
    exit $LASTEXITCODE
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        $Actual,
        [Parameter(Mandatory = $true)]
        $Expected,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ($Actual -ne $Expected) {
        throw "$Message Expected=[$Expected] Actual=[$Actual]"
    }
}

function Assert-Null {
    param(
        [AllowNull()]
        $Actual,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ($null -ne $Actual) {
        throw "$Message Expected=[null] Actual=[$Actual]"
    }
}

function New-AzureReceiptJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReceiptText,
        [Parameter(Mandatory = $true)]
        [string]$TotalContent,
        [Parameter(Mandatory = $true)]
        [decimal]$StructuredAmount,
        [string]$StructuredCurrencyCode
    )

    $valueCurrency = @{
        amount = $StructuredAmount
    }
    if (-not [string]::IsNullOrWhiteSpace($StructuredCurrencyCode)) {
        $valueCurrency.currencyCode = $StructuredCurrencyCode
    }

    return @{
        analyzeResult = @{
            modelId = "prebuilt-receipt"
            content = $ReceiptText
            documents = @(
                @{
                    docType = "receipt.generic"
                    fields = @{
                        Total = @{
                            content = $TotalContent
                            valueCurrency = $valueCurrency
                        }
                    }
                }
            )
        }
    } | ConvertTo-Json -Depth 10 -Compress
}

function Get-ReceiptAnalysis {
    param(
        [Parameter(Mandatory = $true)]
        [Reflection.MethodInfo]$BuildAnalysisMethod,
        [Parameter(Mandatory = $true)]
        [string]$ReceiptText,
        [Parameter(Mandatory = $true)]
        [string]$TotalContent,
        [Parameter(Mandatory = $true)]
        [decimal]$StructuredAmount,
        [string]$StructuredCurrencyCode
    )

    $json = New-AzureReceiptJson `
        -ReceiptText $ReceiptText `
        -TotalContent $TotalContent `
        -StructuredAmount $StructuredAmount `
        -StructuredCurrencyCode $StructuredCurrencyCode

    return $BuildAnalysisMethod.Invoke($null, [object[]]@([string]$json))
}

function New-SingleLineDraft {
    param(
        [Parameter(Mandatory = $true)]
        [Type]$DraftType,
        [Parameter(Mandatory = $true)]
        [Type]$LineType,
        [Parameter(Mandatory = $true)]
        [decimal]$Price,
        [decimal]$Qty = 1,
        [string]$CurrencyCode = "VND"
    )

    $draft = [Activator]::CreateInstance($DraftType)
    $draft.description = "Grab ride"
    $draft.currencyCode = $CurrencyCode
    $draft.gastoType = 14

    $line = [Activator]::CreateInstance($LineType)
    $line.transDate = "10.08.2026"
    $line.typeValue = 14
    $line.description = "Grab ride"
    $line.internacional = $true
    $line.qty = $Qty
    $line.price = $Price

    $listType = [System.Collections.Generic.List``1].MakeGenericType([Type[]]@($LineType))
    $lines = [Activator]::CreateInstance($listType)
    $lines.Add($line)
    $draft.lines = $lines
    return $draft
}

function Invoke-TotalFallback {
    param(
        [Parameter(Mandatory = $true)]
        [Reflection.MethodInfo]$Method,
        [Parameter(Mandatory = $true)]
        $Draft,
        [Parameter(Mandatory = $true)]
        $Analysis,
        [bool]$AllowGroupedVndCorrection = $true
    )

    if ($Method.GetParameters().Count -eq 3) {
        $Method.Invoke($null, [object[]]@($Draft, $Analysis, $AllowGroupedVndCorrection)) | Out-Null
        return
    }

    $Method.Invoke($null, [object[]]@($Draft, $Analysis)) | Out-Null
}

$assemblyDirectory = Split-Path -Parent $AssemblyPath
Push-Location $assemblyDirectory
try {
    $assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
    $analyzerType = $assembly.GetType("IND_CRM_API.Services.AzureReceiptAnalyzerService", $true)
    $normalizerType = $assembly.GetType("IND_CRM_API.Services.IND_OpenAiExpenseTicketDraftService", $true)
    $currencyHelperType = $assembly.GetType("IND_CRM_API.Helpers.CurrencyCodeHelper", $true)
    $draftType = $assembly.GetType("IND_CRM_API.Contracts.Responses.ExpenseSheetDraftResponse", $true)
    $lineType = $assembly.GetType("IND_CRM_API.Contracts.Requests.CreateExpenseSheetLineRequest", $true)
    $privateStatic = [Reflection.BindingFlags]"NonPublic,Static"
    $publicStatic = [Reflection.BindingFlags]"Public,Static"
    $dongSymbol = ([char]0x20AB).ToString()

    $buildAnalysisMethod = $analyzerType.GetMethod("TryBuildAnalysisResult", $privateStatic)
    if ($null -eq $buildAnalysisMethod) {
        throw "TryBuildAnalysisResult was not found."
    }

    $applyTotalFallbackMethod = $normalizerType.GetMethod("ApplySingleLineTotalFallbackFromOcr", $privateStatic)
    if ($null -eq $applyTotalFallbackMethod) {
        throw "ApplySingleLineTotalFallbackFromOcr was not found."
    }

    $applyCurrencyFallbackMethod = $normalizerType.GetMethod("ApplyCurrencyFallbackFromOcr", $privateStatic)
    if ($null -eq $applyCurrencyFallbackMethod) {
        throw "ApplyCurrencyFallbackFromOcr was not found."
    }

    $buildStructuredOcrJsonMethod = $normalizerType.GetMethod("BuildStructuredOcrJsonForProfile", $privateStatic)
    if ($null -eq $buildStructuredOcrJsonMethod) {
        throw "BuildStructuredOcrJsonForProfile was not found."
    }

    $groupedVndEligibilityMethod = $normalizerType.GetMethod("IsGroupedVndQuickCreateCorrectionEligible", $privateStatic)
    if ($null -eq $groupedVndEligibilityMethod) {
        throw "IsGroupedVndQuickCreateCorrectionEligible was not found."
    }

    $buildStructuredPromptMethod = $normalizerType.GetMethod("BuildStructuredOcrPayloadPromptText", $privateStatic)
    if ($null -eq $buildStructuredPromptMethod) {
        throw "BuildStructuredOcrPayloadPromptText was not found."
    }

    $normalizeCurrencyMethod = $currencyHelperType.GetMethod("NormalizeToIso4217", $publicStatic)
    if ($null -eq $normalizeCurrencyMethod) {
        throw "NormalizeToIso4217 was not found."
    }

    $dongCurrency = [string]$normalizeCurrencyMethod.Invoke($null, [object[]]@([string]$dongSymbol))
    Assert-Equal -Actual $dongCurrency -Expected "VND" -Message "The dong symbol must resolve to VND."

    $vndAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText ("Total Paid`n82.000 {0}" -f $dongSymbol) `
        -TotalContent ("82.000 {0}" -f $dongSymbol) `
        -StructuredAmount ([decimal]82)
    Assert-Equal -Actual $vndAnalysis.CurrencyCode -Expected "VND" -Message "The receipt currency must resolve to VND."
    Assert-Equal -Actual ([decimal]$vndAnalysis.TotalAmount) -Expected ([decimal]82) -Message "The shared OCR result must preserve the structured Azure amount."
    Assert-Equal -Actual ([decimal]$vndAnalysis.CorrectedGroupedVndTotalAmount) -Expected ([decimal]82000) -Message "The grouped VND correction must be carried separately."
    Assert-Equal -Actual ([decimal]$vndAnalysis.CorrectedGroupedVndSourceAmount) -Expected ([decimal]82) -Message "The VND correction must retain its structured source amount."

    $vndPrompt = $vndAnalysis.PromptJson | ConvertFrom-Json
    Assert-Equal -Actual ([decimal]$vndPrompt.totals.total.amount) -Expected ([decimal]82) -Message "The shared prompt must preserve the structured Azure total."

    $profileType = $assembly.GetType("IND_CRM_API.Services.Interfaces.ExpenseTicketDraftProfile", $true)
    $quickCreateProfile = [Enum]::Parse($profileType, "QuickCreate")
    $fullDraftProfile = [Enum]::Parse($profileType, "FullDraft")
    $isVndQuickCreateEligible = [bool]$groupedVndEligibilityMethod.Invoke($null, [object[]]@($vndAnalysis, $quickCreateProfile))
    Assert-Equal -Actual $isVndQuickCreateEligible -Expected $true -Message "The proven VND case must be eligible in quick-create."
    $acceptedVndPrompt = [string]$buildStructuredPromptMethod.Invoke($null, [object[]]@($quickCreateProfile, $isVndQuickCreateEligible))
    if (-not $acceptedVndPrompt.Contains("totals.total ya contiene el total VND corregido")) {
        throw "The accepted quick-create prompt must include the guarded VND rule."
    }

    $isVndFullDraftEligible = [bool]$groupedVndEligibilityMethod.Invoke($null, [object[]]@($vndAnalysis, $fullDraftProfile))
    Assert-Equal -Actual $isVndFullDraftEligible -Expected $false -Message "The grouped VND correction must not be eligible in full-draft."
    $fullDraftPrompt = [string]$buildStructuredPromptMethod.Invoke($null, [object[]]@($fullDraftProfile, $isVndFullDraftEligible))
    if ($fullDraftPrompt.Contains("totals.total ya contiene el total VND corregido")) {
        throw "The full-draft prompt must not include the grouped VND rule."
    }
    $quickCreateOcrJson = [string]$buildStructuredOcrJsonMethod.Invoke($null, [object[]]@($vndAnalysis, $quickCreateProfile))
    $quickCreateOcr = $quickCreateOcrJson | ConvertFrom-Json
    Assert-Equal -Actual $quickCreateOcr.currencyCode -Expected "VND" -Message "The quick-create OCR input must use VND."
    Assert-Equal -Actual ([decimal]$quickCreateOcr.totals.total.amount) -Expected ([decimal]82000) -Message "The quick-create OCR input must receive the corrected VND total."
    $fullDraftOcrJson = [string]$buildStructuredOcrJsonMethod.Invoke($null, [object[]]@($vndAnalysis, $fullDraftProfile))
    $fullDraftOcr = $fullDraftOcrJson | ConvertFrom-Json
    Assert-Equal -Actual ([decimal]$fullDraftOcr.totals.total.amount) -Expected ([decimal]82) -Message "The full-draft OCR input must retain the structured Azure total."

    $vndDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]82) -CurrencyCode "EUR"
    $applyCurrencyFallbackMethod.Invoke($null, [object[]]@($vndDraft, $vndAnalysis)) | Out-Null
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $vndDraft -Analysis $vndAnalysis
    Assert-Equal -Actual $vndDraft.currencyCode -Expected "VND" -Message "The authoritative correction must replace the model currency."
    Assert-Equal -Actual $vndDraft.lines.Count -Expected 1 -Message "The VND correction must keep a single draft line."
    Assert-Equal -Actual ([decimal]$vndDraft.lines[0].qty) -Expected ([decimal]1) -Message "The corrected VND line must use quantity one."
    Assert-Equal -Actual ([decimal]$vndDraft.lines[0].price) -Expected ([decimal]82000) -Message "The corrected VND line must use the authoritative total."

    $alreadyCorrectedDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]82000) -CurrencyCode "EUR"
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $alreadyCorrectedDraft -Analysis $vndAnalysis
    Assert-Equal -Actual $alreadyCorrectedDraft.currencyCode -Expected "VND" -Message "An already-corrected model amount must still receive the authoritative currency."
    Assert-Equal -Actual ([decimal]$alreadyCorrectedDraft.lines[0].price) -Expected ([decimal]82000) -Message "An already-corrected model amount must remain unchanged."
    Assert-Null -Actual $alreadyCorrectedDraft.Warnings -Message "An already-corrected model amount must not add a scaling warning."

    $buildNormalizedJsonMethod = $normalizerType.GetMethod("BuildNormalizedDraftJson", $privateStatic)
    if ($null -eq $buildNormalizedJsonMethod) {
        throw "BuildNormalizedDraftJson was not found."
    }
    $normalizedVndJson = [string]$buildNormalizedJsonMethod.Invoke($null, [object[]]@($vndDraft, $quickCreateProfile))
    $normalizedVndDraft = $normalizedVndJson | ConvertFrom-Json
    Assert-Equal -Actual $normalizedVndDraft.currencyCode -Expected "VND" -Message "Normalized JSON must preserve VND."
    Assert-Equal -Actual ([decimal]$normalizedVndDraft.lines[0].price) -Expected ([decimal]82000) -Message "Normalized JSON must preserve the corrected VND price."

    $commaAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText "Total Paid 82,000 VND" `
        -TotalContent "82,000 VND" `
        -StructuredAmount ([decimal]82) `
        -StructuredCurrencyCode "VND"
    Assert-Equal -Actual ([decimal]$commaAnalysis.CorrectedGroupedVndTotalAmount) -Expected ([decimal]82000) -Message "A comma-grouped VND total must be normalized."

    $multiGroupAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText ("Total Paid 1.234.567 {0}" -f $dongSymbol) `
        -TotalContent ("1.234.567 {0}" -f $dongSymbol) `
        -StructuredAmount ([decimal]1234.567)
    Assert-Equal -Actual ([decimal]$multiGroupAnalysis.CorrectedGroupedVndTotalAmount) -Expected ([decimal]1234567) -Message "Multiple VND thousands groups must be normalized."

    $alreadyCorrectAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText ("Total Paid 58.000 {0}" -f $dongSymbol) `
        -TotalContent ("58.000 {0}" -f $dongSymbol) `
        -StructuredAmount ([decimal]58000)
    Assert-Equal -Actual ([decimal]$alreadyCorrectAnalysis.TotalAmount) -Expected ([decimal]58000) -Message "An already correct VND amount must remain unchanged."
    Assert-Null -Actual $alreadyCorrectAnalysis.CorrectedGroupedVndTotalAmount -Message "An already correct VND amount must not carry a correction marker."

    $eurAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText "TOTAL 1.234,56 EUR" `
        -TotalContent "1.234,56 EUR" `
        -StructuredAmount ([decimal]1234.56) `
        -StructuredCurrencyCode "EUR"
    Assert-Equal -Actual ([decimal]$eurAnalysis.TotalAmount) -Expected ([decimal]1234.56) -Message "EUR decimal formatting must remain unchanged."

    $noCurrencyAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText "TOTAL 82.000" `
        -TotalContent "82.000" `
        -StructuredAmount ([decimal]82)
    Assert-Equal -Actual ([decimal]$noCurrencyAnalysis.TotalAmount) -Expected ([decimal]82) -Message "A grouped value without VND evidence must not be scaled."
    Assert-Null -Actual $noCurrencyAnalysis.CorrectedGroupedVndTotalAmount -Message "A grouped value without VND evidence must not carry a correction marker."

    $structuredVndAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText "TOTAL 82.000" `
        -TotalContent "82.000" `
        -StructuredAmount ([decimal]82) `
        -StructuredCurrencyCode "VND"
    Assert-Equal -Actual ([decimal]$structuredVndAnalysis.CorrectedGroupedVndTotalAmount) -Expected ([decimal]82000) -Message "A structured VND code on Total is sufficient local currency evidence."

    $conflictingCurrencyAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText ("TOTAL 82.000 {0}" -f $dongSymbol) `
        -TotalContent ("82.000 {0}" -f $dongSymbol) `
        -StructuredAmount ([decimal]82) `
        -StructuredCurrencyCode "EUR"
    Assert-Equal -Actual ([decimal]$conflictingCurrencyAnalysis.TotalAmount) -Expected ([decimal]82) -Message "A conflicting structured currency must disable the VND correction."
    Assert-Null -Actual $conflictingCurrencyAnalysis.CorrectedGroupedVndTotalAmount -Message "A conflicting structured currency must not carry a correction marker."
    Assert-Equal -Actual $conflictingCurrencyAnalysis.CurrencyCode -Expected "EUR" -Message "A conflicting structured currency must remain authoritative."
    Assert-Equal -Actual $conflictingCurrencyAnalysis.RawCurrency -Expected "EUR" -Message "A conflicting raw currency must remain authoritative."
    Assert-Equal -Actual ([string]::Join("|", $conflictingCurrencyAnalysis.CurrencyHints)) -Expected "EUR" -Message "A rejected VND hint must not compete with the structured currency."

    foreach ($invalidStructuredCurrency in @("XXX", "VND EUR")) {
        $invalidStructuredCurrencyAnalysis = Get-ReceiptAnalysis `
            -BuildAnalysisMethod $buildAnalysisMethod `
            -ReceiptText ("TOTAL 82.000 {0}" -f $dongSymbol) `
            -TotalContent ("82.000 {0}" -f $dongSymbol) `
            -StructuredAmount ([decimal]82) `
            -StructuredCurrencyCode $invalidStructuredCurrency
        Assert-Equal -Actual ([decimal]$invalidStructuredCurrencyAnalysis.TotalAmount) -Expected ([decimal]82) -Message "An invalid structured currency must preserve the source amount."
        Assert-Null -Actual $invalidStructuredCurrencyAnalysis.CorrectedGroupedVndTotalAmount -Message "An invalid structured currency must disable correction."
    }

    $nonLocalCurrencyAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText ("Currency {0}`nTOTAL 82.000" -f $dongSymbol) `
        -TotalContent "82.000" `
        -StructuredAmount ([decimal]82)
    Assert-Equal -Actual ([decimal]$nonLocalCurrencyAnalysis.TotalAmount) -Expected ([decimal]82) -Message "VND evidence outside Total.content must not trigger scaling."
    Assert-Null -Actual $nonLocalCurrencyAnalysis.CorrectedGroupedVndTotalAmount -Message "Non-local VND evidence must not carry a correction marker."

    $nonLocalStructuredEurAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText ("Currency {0}`nTOTAL 82.000" -f $dongSymbol) `
        -TotalContent "82.000" `
        -StructuredAmount ([decimal]82) `
        -StructuredCurrencyCode "EUR"
    Assert-Equal -Actual ([decimal]$nonLocalStructuredEurAnalysis.TotalAmount) -Expected ([decimal]82) -Message "Non-local VND evidence must preserve the structured amount."
    Assert-Null -Actual $nonLocalStructuredEurAnalysis.CorrectedGroupedVndTotalAmount -Message "Non-local VND evidence must not authorize correction over structured EUR."
    Assert-Equal -Actual $nonLocalStructuredEurAnalysis.CurrencyCode -Expected "EUR" -Message "Structured EUR must win over a non-local VND hint."
    Assert-Equal -Actual $nonLocalStructuredEurAnalysis.RawCurrency -Expected "EUR" -Message "Structured raw EUR must win over a non-local VND hint."
    Assert-Equal -Actual ([string]::Join("|", $nonLocalStructuredEurAnalysis.CurrencyHints)) -Expected "EUR" -Message "A non-local VND hint must be removed when structured EUR is authoritative."

    $laterDocumentJson = @{
        analyzeResult = @{
            modelId = "prebuilt-receipt"
            content = ("TOTAL 82.000 {0}" -f $dongSymbol)
            documents = @(
                $null,
                @{
                    docType = "receipt.generic"
                    fields = @{
                        Total = @{
                            content = ("82.000 {0}" -f $dongSymbol)
                            valueCurrency = @{ amount = [decimal]82 }
                        }
                    }
                }
            )
        }
    } | ConvertTo-Json -Depth 10 -Compress
    $laterDocumentAnalysis = $buildAnalysisMethod.Invoke($null, [object[]]@([string]$laterDocumentJson))
    Assert-Equal -Actual ([decimal]$laterDocumentAnalysis.TotalAmount) -Expected ([decimal]82) -Message "The correction must not skip an invalid first semantic document."
    Assert-Null -Actual $laterDocumentAnalysis.CorrectedGroupedVndTotalAmount -Message "A later semantic document must not authorize correction."

    $usdAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText "TOTAL 82.50 USD" `
        -TotalContent "82.50 USD" `
        -StructuredAmount ([decimal]82.50) `
        -StructuredCurrencyCode "USD"
    Assert-Equal -Actual ([decimal]$usdAnalysis.TotalAmount) -Expected ([decimal]82.50) -Message "USD decimal formatting must remain unchanged."

    $ambiguousCases = @(
        @{ Content = ("1.234,567 {0}" -f $dongSymbol); Source = [decimal]1234.567; Name = "mixed separators" },
        @{ Content = ("82.000,50 {0}" -f $dongSymbol); Source = [decimal]82000.50; Name = "decimal suffix" },
        @{ Content = ("-82.000 {0}" -f $dongSymbol); Source = [decimal]82; Name = "signed amount" },
        @{ Content = ("082.000 {0}" -f $dongSymbol); Source = [decimal]82; Name = "leading zero" },
        @{ Content = ("82 000 {0}" -f $dongSymbol); Source = [decimal]82; Name = "space grouping" }
    )
    foreach ($case in $ambiguousCases) {
        $analysis = Get-ReceiptAnalysis `
            -BuildAnalysisMethod $buildAnalysisMethod `
            -ReceiptText ("TOTAL {0}" -f $case.Content) `
            -TotalContent $case.Content `
            -StructuredAmount $case.Source
        Assert-Equal -Actual ([decimal]$analysis.TotalAmount) -Expected ([decimal]$case.Source) -Message ("An ambiguous VND {0} must not be scaled." -f $case.Name)
        Assert-Null -Actual $analysis.CorrectedGroupedVndTotalAmount -Message ("An ambiguous VND {0} must not carry a correction marker." -f $case.Name)
    }

    $ratioMismatchAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText ("TOTAL 82.000 {0}" -f $dongSymbol) `
        -TotalContent ("82.000 {0}" -f $dongSymbol) `
        -StructuredAmount ([decimal]83)
    Assert-Equal -Actual ([decimal]$ratioMismatchAnalysis.TotalAmount) -Expected ([decimal]83) -Message "A non-power-of-1000 mismatch must not be corrected."
    Assert-Null -Actual $ratioMismatchAnalysis.CorrectedGroupedVndTotalAmount -Message "A ratio mismatch must not carry a correction marker."
    $isRatioMismatchEligible = [bool]$groupedVndEligibilityMethod.Invoke($null, [object[]]@($ratioMismatchAnalysis, $quickCreateProfile))
    Assert-Equal -Actual $isRatioMismatchEligible -Expected $false -Message "A ratio mismatch must not enable the VND prompt rule."
    $ratioMismatchPrompt = [string]$buildStructuredPromptMethod.Invoke($null, [object[]]@($quickCreateProfile, $isRatioMismatchEligible))
    if ($ratioMismatchPrompt.Contains("totals.total ya contiene el total VND corregido")) {
        throw "A rejected VND case must not include the grouped VND prompt rule."
    }
    $ratioMismatchOcrJson = [string]$buildStructuredOcrJsonMethod.Invoke($null, [object[]]@($ratioMismatchAnalysis, $quickCreateProfile))
    $ratioMismatchOcr = $ratioMismatchOcrJson | ConvertFrom-Json
    Assert-Equal -Actual ([decimal]$ratioMismatchOcr.totals.total.amount) -Expected ([decimal]83) -Message "A rejected VND case must retain its original prompt total."

    $fullDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]82) -CurrencyCode "EUR"
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $fullDraft -Analysis $vndAnalysis -AllowGroupedVndCorrection $false
    Assert-Equal -Actual ([decimal]$fullDraft.lines[0].price) -Expected ([decimal]82) -Message "The deterministic correction must remain disabled outside quick-create."
    Assert-Equal -Actual $fullDraft.currencyCode -Expected "EUR" -Message "The deterministic correction must not change full-draft currency."

    $fullDraftMissingPrice = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]0) -CurrencyCode "EUR"
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $fullDraftMissingPrice -Analysis $vndAnalysis -AllowGroupedVndCorrection $false
    Assert-Equal -Actual ([decimal]$fullDraftMissingPrice.lines[0].price) -Expected ([decimal]82) -Message "A full-draft fallback must use the original structured amount."
    Assert-Equal -Actual $fullDraftMissingPrice.currencyCode -Expected "EUR" -Message "A full-draft fallback must retain its currency."

    $quickCreateMissingPrice = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]0) -CurrencyCode "EUR"
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $quickCreateMissingPrice -Analysis $vndAnalysis
    Assert-Equal -Actual ([decimal]$quickCreateMissingPrice.lines[0].price) -Expected ([decimal]82) -Message "A missing model price must not authorize VND scaling."
    Assert-Equal -Actual $quickCreateMissingPrice.currencyCode -Expected "EUR" -Message "A missing model price must retain the draft currency."

    $quantityMissingPriceDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]0) -Qty ([decimal]2) -CurrencyCode "EUR"
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $quantityMissingPriceDraft -Analysis $vndAnalysis
    Assert-Equal -Actual ([decimal]$quantityMissingPriceDraft.lines[0].price) -Expected ([decimal]82) -Message "A missing price with quantity two must not authorize VND scaling."
    Assert-Equal -Actual $quantityMissingPriceDraft.currencyCode -Expected "EUR" -Message "A missing price with quantity two must retain the draft currency."

    $multiInvalidDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]0) -CurrencyCode "EUR"
    $secondInvalidLine = [Activator]::CreateInstance($lineType)
    $secondInvalidLine.description = "Second invalid line"
    $secondInvalidLine.qty = [decimal]1
    $secondInvalidLine.price = [decimal]0
    $multiInvalidDraft.lines.Add($secondInvalidLine)
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $multiInvalidDraft -Analysis $vndAnalysis
    Assert-Equal -Actual $multiInvalidDraft.lines.Count -Expected 1 -Message "The legacy fallback may still collapse invalid lines."
    Assert-Equal -Actual ([decimal]$multiInvalidDraft.lines[0].price) -Expected ([decimal]82) -Message "Multiple invalid model lines must not authorize VND scaling."
    Assert-Equal -Actual $multiInvalidDraft.currencyCode -Expected "EUR" -Message "Multiple invalid model lines must retain the draft currency."

    $itemizedDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]82) -CurrencyCode "EUR"
    $vndAnalysis.ItemCount = 1
    $isItemizedEligible = [bool]$groupedVndEligibilityMethod.Invoke($null, [object[]]@($vndAnalysis, $quickCreateProfile))
    Assert-Equal -Actual $isItemizedEligible -Expected $false -Message "An itemized receipt must not enable the VND correction."
    $itemizedPrompt = [string]$buildStructuredPromptMethod.Invoke($null, [object[]]@($quickCreateProfile, $isItemizedEligible))
    if ($itemizedPrompt.Contains("totals.total ya contiene el total VND corregido")) {
        throw "An itemized receipt prompt must not include the grouped VND rule."
    }
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $itemizedDraft -Analysis $vndAnalysis
    Assert-Equal -Actual ([decimal]$itemizedDraft.lines[0].price) -Expected ([decimal]82) -Message "An itemized receipt must not receive the single-line correction."
    Assert-Equal -Actual $itemizedDraft.currencyCode -Expected "EUR" -Message "An itemized receipt must retain the model currency."

    $itemizedMissingPrice = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]0) -CurrencyCode "EUR"
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $itemizedMissingPrice -Analysis $vndAnalysis
    Assert-Equal -Actual ([decimal]$itemizedMissingPrice.lines[0].price) -Expected ([decimal]82) -Message "An itemized fallback must use the original structured amount."
    Assert-Equal -Actual $itemizedMissingPrice.currencyCode -Expected "EUR" -Message "An itemized fallback must retain its currency."
    $vndAnalysis.ItemCount = 0

    $quantityDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]82) -Qty ([decimal]2) -CurrencyCode "EUR"
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $quantityDraft -Analysis $vndAnalysis
    Assert-Equal -Actual ([decimal]$quantityDraft.lines[0].price) -Expected ([decimal]82) -Message "A line with quantity other than one must not be corrected."
    Assert-Equal -Actual ([decimal]$quantityDraft.lines[0].qty) -Expected ([decimal]2) -Message "The VND correction must not rewrite an explicit quantity."

    $unrelatedAmountDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]83) -CurrencyCode "EUR"
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $unrelatedAmountDraft -Analysis $vndAnalysis
    Assert-Equal -Actual ([decimal]$unrelatedAmountDraft.lines[0].price) -Expected ([decimal]83) -Message "An unrelated model amount must not be corrected."
    Assert-Equal -Actual $unrelatedAmountDraft.currencyCode -Expected "EUR" -Message "An unrelated model amount must retain its currency."

    $multiLineDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]40)
    $secondLine = [Activator]::CreateInstance($lineType)
    $secondLine.description = "Second line"
    $secondLine.qty = [decimal]1
    $secondLine.price = [decimal]42
    $multiLineDraft.lines.Add($secondLine)
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $multiLineDraft -Analysis $vndAnalysis
    Assert-Equal -Actual $multiLineDraft.lines.Count -Expected 2 -Message "A multi-line VND draft must not be collapsed automatically."
    Assert-Equal -Actual ([decimal]$multiLineDraft.lines[0].price) -Expected ([decimal]40) -Message "A multi-line VND draft must not be scaled automatically."

    Write-Host "PASS ticket VND thousands regression"
}
finally {
    Pop-Location
}
