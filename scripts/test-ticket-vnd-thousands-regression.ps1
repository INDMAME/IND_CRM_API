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
        [string]$StructuredCurrencyCode,
        [string]$StructuredCurrencySymbol
    )

    $valueCurrency = @{
        amount = $StructuredAmount
    }
    if (-not [string]::IsNullOrWhiteSpace($StructuredCurrencyCode)) {
        $valueCurrency.currencyCode = $StructuredCurrencyCode
    }
    if (-not [string]::IsNullOrWhiteSpace($StructuredCurrencySymbol)) {
        $valueCurrency.currencySymbol = $StructuredCurrencySymbol
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
        [string]$StructuredCurrencyCode,
        [string]$StructuredCurrencySymbol
    )

    $json = New-AzureReceiptJson `
        -ReceiptText $ReceiptText `
        -TotalContent $TotalContent `
        -StructuredAmount $StructuredAmount `
        -StructuredCurrencyCode $StructuredCurrencyCode `
        -StructuredCurrencySymbol $StructuredCurrencySymbol

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

function Get-DraftLinesTotal {
    param(
        [Parameter(Mandatory = $true)]
        $Draft
    )

    [decimal]$total = 0
    foreach ($line in @($Draft.lines)) {
        if ($null -eq $line) {
            continue
        }

        [decimal]$qty = if ($null -eq $line.qty) { 0 } else { $line.qty }
        [decimal]$price = if ($null -eq $line.price) { 0 } else { $line.price }
        $total += if ($qty -eq 0 -and $price -lt 0) { $price } else { $qty * $price }
    }

    return $total
}

$assemblyDirectory = Split-Path -Parent $AssemblyPath
Push-Location $assemblyDirectory
try {
    $assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
    $analyzerType = $assembly.GetType("IND_CRM_API.Services.AzureReceiptAnalyzerService", $true)
    $normalizerType = $assembly.GetType("IND_CRM_API.Services.IND_OpenAiExpenseTicketDraftService", $true)
    $controllerType = $assembly.GetType("IND_CRM_API.Controllers.CRM.CrmExpenseSheetTicketsController", $true)
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

    $buildUpdateRequestMethod = $controllerType.GetMethod("BuildQuickCreateUpdateRequestFromDraft", $privateStatic)
    if ($null -eq $buildUpdateRequestMethod) {
        throw "BuildQuickCreateUpdateRequestFromDraft was not found."
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

    $splitGlobalVndAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText ("Total Paid`n82.000`n{0}`n`$ Cash" -f $dongSymbol) `
        -TotalContent "82.000" `
        -StructuredAmount ([decimal]82)
    Assert-Equal -Actual ([decimal]$splitGlobalVndAnalysis.CorrectedGroupedVndTotalAmount) -Expected ([decimal]82000) -Message "Receipt-wide VND evidence must qualify only the grouped amount from Total.content."
    Assert-Equal -Actual $splitGlobalVndAnalysis.CurrencyCode -Expected "VND" -Message "Receipt-wide dong evidence must resolve to VND."

    $globalDifferentNumberAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText "Total Paid 91.000 VND" `
        -TotalContent "82.000" `
        -StructuredAmount ([decimal]91)
    Assert-Equal -Actual ([decimal]$globalDifferentNumberAnalysis.CorrectedGroupedVndTotalAmount) -Expected ([decimal]82000) -Message "The corrected number must come only from Total.content."

    $metadataSymbolAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText "Total Paid 82.000" `
        -TotalContent "82.000" `
        -StructuredAmount ([decimal]82) `
        -StructuredCurrencySymbol $dongSymbol
    Assert-Equal -Actual ([decimal]$metadataSymbolAnalysis.CorrectedGroupedVndTotalAmount) -Expected ([decimal]82000) -Message "The Total currencySymbol metadata must qualify VND."

    $zeroStructuredAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText "Total Paid 82.000 VND" `
        -TotalContent "82.000 VND" `
        -StructuredAmount ([decimal]0)
    Assert-Equal -Actual ([decimal]$zeroStructuredAnalysis.CorrectedGroupedVndTotalAmount) -Expected ([decimal]82000) -Message "A non-authoritative structured zero must not block the Total.content value."

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
    $applyCurrencyFallbackMethod.Invoke($null, [object[]]@($vndDraft, $splitGlobalVndAnalysis)) | Out-Null
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $vndDraft -Analysis $splitGlobalVndAnalysis
    Assert-Equal -Actual $vndDraft.currencyCode -Expected "VND" -Message "The authoritative correction must replace the model currency."
    Assert-Equal -Actual $vndDraft.lines.Count -Expected 2 -Message "VND reconciliation must add at most one deterministic adjustment line."
    Assert-Equal -Actual ([decimal]$vndDraft.lines[0].qty) -Expected ([decimal]1) -Message "VND reconciliation must preserve the original quantity."
    Assert-Equal -Actual ([decimal]$vndDraft.lines[0].price) -Expected ([decimal]82) -Message "VND reconciliation must not scale the original line."
    Assert-Equal -Actual ([decimal]$vndDraft.lines[1].price) -Expected ([decimal]81918) -Message "The VND adjustment must reconcile the exact grouped total."
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $vndDraft) -Expected ([decimal]82000) -Message "The reconciled VND lines must sum to the authoritative total."

    $vndWarningCount = @($vndDraft.Warnings).Count
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $vndDraft -Analysis $splitGlobalVndAnalysis
    Assert-Equal -Actual $vndDraft.lines.Count -Expected 2 -Message "Repeated VND reconciliation must not duplicate the adjustment line."
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $vndDraft) -Expected ([decimal]82000) -Message "Repeated VND reconciliation must be idempotent."
    Assert-Equal -Actual @($vndDraft.Warnings).Count -Expected $vndWarningCount -Message "Repeated VND reconciliation must not duplicate warnings."

    $buildUpdateArguments = [object[]]@($vndDraft, "", "ticket.jpg", "jpg", "Ticket regression", "VND", "", $null, $false)
    $vndUpdateRequest = $buildUpdateRequestMethod.Invoke($null, $buildUpdateArguments)
    Assert-Equal -Actual ([decimal]$vndUpdateRequest.totalAmount) -Expected ([decimal]82000) -Message "UpdateExpenseSheetTicketFromIARequest must use the reconciled VND total."
    Assert-Equal -Actual $vndUpdateRequest.lines.Count -Expected 2 -Message "The update request must preserve the deterministic VND adjustment."

    $zeroStructuredDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]82) -CurrencyCode "EUR"
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $zeroStructuredDraft -Analysis $zeroStructuredAnalysis
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $zeroStructuredDraft) -Expected ([decimal]82000) -Message "VND reconciliation must not depend on the structured Azure amount."
    Assert-Equal -Actual $zeroStructuredDraft.currencyCode -Expected "VND" -Message "The authoritative Total.content correction must set VND."

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
    Assert-Equal -Actual ([decimal]$normalizedVndDraft.lines[0].price) -Expected ([decimal]82) -Message "Normalized JSON must preserve the original VND line."
    Assert-Equal -Actual ([decimal]$normalizedVndDraft.lines[1].price) -Expected ([decimal]81918) -Message "Normalized JSON must preserve the VND adjustment."

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
    Assert-Null -Actual $eurAnalysis.CorrectedGroupedVndTotalAmount -Message "EUR must never receive the VND correction."

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
    Assert-Equal -Actual ([decimal]$conflictingCurrencyAnalysis.CorrectedGroupedVndTotalAmount) -Expected ([decimal]82000) -Message "Explicit VND in Total.content must take precedence over lower-level metadata."
    Assert-Equal -Actual $conflictingCurrencyAnalysis.CurrencyCode -Expected "VND" -Message "Total.content currency must remain authoritative."

    foreach ($invalidStructuredCurrency in @("XXX", "VND EUR", "VND XXX")) {
        $invalidStructuredCurrencyAnalysis = Get-ReceiptAnalysis `
            -BuildAnalysisMethod $buildAnalysisMethod `
            -ReceiptText ("TOTAL 82.000 {0}" -f $dongSymbol) `
            -TotalContent "82.000" `
            -StructuredAmount ([decimal]82) `
            -StructuredCurrencyCode $invalidStructuredCurrency
        Assert-Equal -Actual ([decimal]$invalidStructuredCurrencyAnalysis.TotalAmount) -Expected ([decimal]82) -Message "An invalid structured currency must preserve the source amount."
        Assert-Null -Actual $invalidStructuredCurrencyAnalysis.CorrectedGroupedVndTotalAmount -Message "An invalid structured currency must disable correction."
    }

    $unknownCodeWithDongSymbolAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText ("TOTAL 82.000 {0}" -f $dongSymbol) `
        -TotalContent "82.000" `
        -StructuredAmount ([decimal]82) `
        -StructuredCurrencyCode "XXX" `
        -StructuredCurrencySymbol $dongSymbol
    Assert-Null -Actual $unknownCodeWithDongSymbolAnalysis.CorrectedGroupedVndTotalAmount -Message "Unknown Total currency metadata must block a VND fallback."

    $vndCodeWithUnknownSymbolAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText "TOTAL 82.000 VND" `
        -TotalContent "82.000" `
        -StructuredAmount ([decimal]82) `
        -StructuredCurrencyCode "VND" `
        -StructuredCurrencySymbol ([char]0x00A4).ToString()
    Assert-Null -Actual $vndCodeWithUnknownSymbolAnalysis.CorrectedGroupedVndTotalAmount -Message "Unknown Total currency symbols must block a VND correction."

    $contaminatedDongSymbolAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText ("TOTAL 82.000 {0}" -f $dongSymbol) `
        -TotalContent "82.000" `
        -StructuredAmount ([decimal]82) `
        -StructuredCurrencySymbol ("{0} {1}" -f $dongSymbol, ([char]0x00A4))
    Assert-Null -Actual $contaminatedDongSymbolAnalysis.CorrectedGroupedVndTotalAmount -Message "Contaminated Total currency metadata must fail closed."

    $vndCodeWithEurSymbolAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText "TOTAL 82.000 VND" `
        -TotalContent "82.000" `
        -StructuredAmount ([decimal]82) `
        -StructuredCurrencyCode "VND" `
        -StructuredCurrencySymbol ([char]0x20AC).ToString()
    Assert-Null -Actual $vndCodeWithEurSymbolAnalysis.CorrectedGroupedVndTotalAmount -Message "Contradictory Total currency metadata must block a VND correction."

    $globalCurrencyConflicts = @(
        @{ Receipt = "TOTAL 82.000 VND EUR"; Name = "EUR" },
        @{ Receipt = "TOTAL 82.000 VND USD"; Name = "USD" },
        @{ Receipt = "TOTAL 82.000 VND US`$"; Name = "US dollar" },
        @{ Receipt = "TOTAL 82.000 VND `$"; Name = "standalone dollar" },
        @{ Receipt = "TOTAL 82.000 VND `$ CARD"; Name = "dollar card marker" },
        @{ Receipt = ("TOTAL 82.000 VND {0}" -f ([char]0x00A4)); Name = "unknown currency symbol" }
    )
    foreach ($case in $globalCurrencyConflicts) {
        $analysis = Get-ReceiptAnalysis `
            -BuildAnalysisMethod $buildAnalysisMethod `
            -ReceiptText $case.Receipt `
            -TotalContent "82.000" `
            -StructuredAmount ([decimal]82)
        Assert-Null -Actual $analysis.CorrectedGroupedVndTotalAmount -Message ("A global {0} conflict must block the VND fallback." -f $case.Name)
    }

    foreach ($mixedTotalContent in @("82.000 VND EUR", "82.000 VND USD")) {
        $analysis = Get-ReceiptAnalysis `
            -BuildAnalysisMethod $buildAnalysisMethod `
            -ReceiptText ("TOTAL {0}" -f $mixedTotalContent) `
            -TotalContent $mixedTotalContent `
            -StructuredAmount ([decimal]82)
        Assert-Null -Actual $analysis.CorrectedGroupedVndTotalAmount -Message "Competing currencies inside Total.content must block the correction."
    }

    foreach ($lowerPriorityCurrency in @("EUR", "USD")) {
        $analysis = Get-ReceiptAnalysis `
            -BuildAnalysisMethod $buildAnalysisMethod `
            -ReceiptText ("TOTAL 82.000 VND {0}" -f $lowerPriorityCurrency) `
            -TotalContent "82.000 VND" `
            -StructuredAmount ([decimal]82)
        Assert-Equal -Actual ([decimal]$analysis.CorrectedGroupedVndTotalAmount) -Expected ([decimal]82000) -Message "Total.content must outrank non-dollar global currency evidence."
    }

    $metadataPriorityAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText "TOTAL 82.000 VND EUR" `
        -TotalContent "82.000" `
        -StructuredAmount ([decimal]82) `
        -StructuredCurrencyCode "VND"
    Assert-Equal -Actual ([decimal]$metadataPriorityAnalysis.CorrectedGroupedVndTotalAmount) -Expected ([decimal]82000) -Message "Valid Total metadata must outrank non-dollar global currency evidence."

    foreach ($ambiguousDollarReceipt in @("TOTAL 82.000 VND US`$", "TOTAL 82.000 VND `$", "TOTAL 82.000 VND `$ CARD")) {
        $analysis = Get-ReceiptAnalysis `
            -BuildAnalysisMethod $buildAnalysisMethod `
            -ReceiptText $ambiguousDollarReceipt `
            -TotalContent "82.000 VND" `
            -StructuredAmount ([decimal]82)
        Assert-Null -Actual $analysis.CorrectedGroupedVndTotalAmount -Message "Ambiguous global dollar evidence must block even explicit VND in Total.content."
    }

    foreach ($totalContentWithDollar in @("`$82.000 VND", "82.000 VND `$", "82.000 VND `$ CARD", "US`$ 82.000 VND")) {
        $analysis = Get-ReceiptAnalysis `
            -BuildAnalysisMethod $buildAnalysisMethod `
            -ReceiptText ("TOTAL {0}" -f $totalContentWithDollar) `
            -TotalContent $totalContentWithDollar `
            -StructuredAmount ([decimal]82)
        Assert-Null -Actual $analysis.CorrectedGroupedVndTotalAmount -Message "Dollar evidence in Total.content must always block the VND correction."
    }

    $nonLocalCurrencyAnalysis = Get-ReceiptAnalysis `
        -BuildAnalysisMethod $buildAnalysisMethod `
        -ReceiptText ("Currency {0}`nTOTAL 82.000" -f $dongSymbol) `
        -TotalContent "82.000" `
        -StructuredAmount ([decimal]82)
    Assert-Equal -Actual ([decimal]$nonLocalCurrencyAnalysis.TotalAmount) -Expected ([decimal]82) -Message "The shared OCR result must preserve the structured amount."
    Assert-Equal -Actual ([decimal]$nonLocalCurrencyAnalysis.CorrectedGroupedVndTotalAmount) -Expected ([decimal]82000) -Message "Unambiguous receipt-wide VND evidence may qualify Total.content as a last resort."

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
    Assert-Null -Actual $usdAnalysis.CorrectedGroupedVndTotalAmount -Message "USD must never receive the VND correction."

    $ambiguousCases = @(
        @{ Content = ("1.234,567 {0}" -f $dongSymbol); Source = [decimal]1234.567; Name = "mixed separators" },
        @{ Content = ("82.000,50 {0}" -f $dongSymbol); Source = [decimal]82000.50; Name = "decimal suffix" },
        @{ Content = ("-82.000 {0}" -f $dongSymbol); Source = [decimal]82; Name = "signed amount" },
        @{ Content = ("+82.000 {0}" -f $dongSymbol); Source = [decimal]82; Name = "positive sign" },
        @{ Content = ("(82.000) {0}" -f $dongSymbol); Source = [decimal]82; Name = "accounting format" },
        @{ Content = ("082.000 {0}" -f $dongSymbol); Source = [decimal]82; Name = "leading zero" },
        @{ Content = ("82 000 {0}" -f $dongSymbol); Source = [decimal]82; Name = "space grouping" },
        @{ Content = ("82.000 {0} 91" -f $dongSymbol); Source = [decimal]82; Name = "additional digits" },
        @{ Content = ("82.000 91.000 {0}" -f $dongSymbol); Source = [decimal]82; Name = "multiple grouped amounts" }
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
    Assert-Equal -Actual ([decimal]$ratioMismatchAnalysis.TotalAmount) -Expected ([decimal]83) -Message "The structured amount remains telemetry and must not supply the corrected number."
    Assert-Equal -Actual ([decimal]$ratioMismatchAnalysis.CorrectedGroupedVndTotalAmount) -Expected ([decimal]82000) -Message "The authoritative number must come exclusively from Total.content."
    $isRatioMismatchEligible = [bool]$groupedVndEligibilityMethod.Invoke($null, [object[]]@($ratioMismatchAnalysis, $quickCreateProfile))
    Assert-Equal -Actual $isRatioMismatchEligible -Expected $true -Message "Structured-number disagreement must not override authoritative Total.content."
    $ratioMismatchPrompt = [string]$buildStructuredPromptMethod.Invoke($null, [object[]]@($quickCreateProfile, $isRatioMismatchEligible))
    if (-not $ratioMismatchPrompt.Contains("totals.total ya contiene el total VND corregido")) {
        throw "An authoritative VND case must retain the defensive prompt rule."
    }
    $ratioMismatchOcrJson = [string]$buildStructuredOcrJsonMethod.Invoke($null, [object[]]@($ratioMismatchAnalysis, $quickCreateProfile))
    $ratioMismatchOcr = $ratioMismatchOcrJson | ConvertFrom-Json
    Assert-Equal -Actual ([decimal]$ratioMismatchOcr.totals.total.amount) -Expected ([decimal]82000) -Message "Quick-create must receive the authoritative number from Total.content."

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
    Assert-Equal -Actual $quickCreateMissingPrice.lines.Count -Expected 1 -Message "Invalid model lines must be replaced by one authoritative total line."
    Assert-Equal -Actual ([decimal]$quickCreateMissingPrice.lines[0].price) -Expected ([decimal]82000) -Message "A missing model price must reconcile to the authoritative VND total."
    Assert-Equal -Actual $quickCreateMissingPrice.currencyCode -Expected "VND" -Message "An authoritative total must set VND."

    $quantityMissingPriceDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]0) -Qty ([decimal]2) -CurrencyCode "EUR"
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $quantityMissingPriceDraft -Analysis $vndAnalysis
    Assert-Equal -Actual $quantityMissingPriceDraft.lines.Count -Expected 1 -Message "An invalid quantity line must be replaced by one authoritative total line."
    Assert-Equal -Actual ([decimal]$quantityMissingPriceDraft.lines[0].price) -Expected ([decimal]82000) -Message "A missing price with quantity two must reconcile to the VND total."
    Assert-Equal -Actual $quantityMissingPriceDraft.currencyCode -Expected "VND" -Message "The reconciled draft must use VND."

    $multiInvalidDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]0) -CurrencyCode "EUR"
    $secondInvalidLine = [Activator]::CreateInstance($lineType)
    $secondInvalidLine.description = "Second invalid line"
    $secondInvalidLine.qty = [decimal]1
    $secondInvalidLine.price = [decimal]0
    $multiInvalidDraft.lines.Add($secondInvalidLine)
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $multiInvalidDraft -Analysis $vndAnalysis
    Assert-Equal -Actual $multiInvalidDraft.lines.Count -Expected 1 -Message "Multiple invalid lines must become one authoritative total line."
    Assert-Equal -Actual ([decimal]$multiInvalidDraft.lines[0].price) -Expected ([decimal]82000) -Message "Multiple invalid model lines must reconcile to the VND total."
    Assert-Equal -Actual $multiInvalidDraft.currencyCode -Expected "VND" -Message "The reconciled invalid draft must use VND."

    $itemizedDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]82) -CurrencyCode "EUR"
    $vndAnalysis.ItemCount = 1
    $isItemizedEligible = [bool]$groupedVndEligibilityMethod.Invoke($null, [object[]]@($vndAnalysis, $quickCreateProfile))
    Assert-Equal -Actual $isItemizedEligible -Expected $true -Message "Item presence must not suppress an authoritative Total.content correction."
    $itemizedPrompt = [string]$buildStructuredPromptMethod.Invoke($null, [object[]]@($quickCreateProfile, $isItemizedEligible))
    if (-not $itemizedPrompt.Contains("totals.total ya contiene el total VND corregido")) {
        throw "An itemized authoritative VND receipt must include the guarded prompt rule."
    }
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $itemizedDraft -Analysis $vndAnalysis
    Assert-Equal -Actual $itemizedDraft.lines.Count -Expected 2 -Message "Itemized lines must be preserved and reconciled with one adjustment."
    Assert-Equal -Actual ([decimal]$itemizedDraft.lines[0].price) -Expected ([decimal]82) -Message "The itemized line must not be multiplied."
    Assert-Equal -Actual ([decimal]$itemizedDraft.lines[1].price) -Expected ([decimal]81918) -Message "The itemized difference must be explicit."
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $itemizedDraft) -Expected ([decimal]82000) -Message "The itemized lines must reconcile exactly."
    Assert-Equal -Actual $itemizedDraft.currencyCode -Expected "VND" -Message "The itemized authoritative receipt must use VND."

    $itemizedMissingPrice = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]0) -CurrencyCode "EUR"
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $itemizedMissingPrice -Analysis $vndAnalysis
    Assert-Equal -Actual $itemizedMissingPrice.lines.Count -Expected 1 -Message "An invalid itemized line must become one total line."
    Assert-Equal -Actual ([decimal]$itemizedMissingPrice.lines[0].price) -Expected ([decimal]82000) -Message "An invalid itemized line must use the authoritative VND total."
    Assert-Equal -Actual $itemizedMissingPrice.currencyCode -Expected "VND" -Message "The itemized fallback must use VND."
    $vndAnalysis.ItemCount = 0

    $quantityDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]82) -Qty ([decimal]2) -CurrencyCode "EUR"
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $quantityDraft -Analysis $vndAnalysis
    Assert-Equal -Actual $quantityDraft.lines.Count -Expected 2 -Message "A non-unit quantity must be preserved with one adjustment."
    Assert-Equal -Actual ([decimal]$quantityDraft.lines[0].price) -Expected ([decimal]82) -Message "The quantity line price must not be multiplied."
    Assert-Equal -Actual ([decimal]$quantityDraft.lines[0].qty) -Expected ([decimal]2) -Message "The VND correction must not rewrite an explicit quantity."
    Assert-Equal -Actual ([decimal]$quantityDraft.lines[1].price) -Expected ([decimal]81836) -Message "The quantity line difference must reconcile exactly."
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $quantityDraft) -Expected ([decimal]82000) -Message "The quantity draft must sum to the authoritative total."

    $unrelatedAmountDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]83) -CurrencyCode "EUR"
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $unrelatedAmountDraft -Analysis $vndAnalysis
    Assert-Equal -Actual $unrelatedAmountDraft.lines.Count -Expected 2 -Message "An unrelated line must be preserved with one deterministic adjustment."
    Assert-Equal -Actual ([decimal]$unrelatedAmountDraft.lines[0].price) -Expected ([decimal]83) -Message "An unrelated model amount must not be multiplied."
    Assert-Equal -Actual ([decimal]$unrelatedAmountDraft.lines[1].price) -Expected ([decimal]81917) -Message "The unrelated-line difference must be explicit."
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $unrelatedAmountDraft) -Expected ([decimal]82000) -Message "The unrelated-line draft must reconcile exactly."
    Assert-Equal -Actual $unrelatedAmountDraft.currencyCode -Expected "VND" -Message "The reconciled draft must use VND."

    $multiLineDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]40)
    $secondLine = [Activator]::CreateInstance($lineType)
    $secondLine.description = "Second line"
    $secondLine.qty = [decimal]1
    $secondLine.price = [decimal]42
    $multiLineDraft.lines.Add($secondLine)
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $multiLineDraft -Analysis $vndAnalysis
    Assert-Equal -Actual $multiLineDraft.lines.Count -Expected 3 -Message "A multi-line VND draft must preserve both lines and add one adjustment."
    Assert-Equal -Actual ([decimal]$multiLineDraft.lines[0].price) -Expected ([decimal]40) -Message "The first VND line must not be scaled."
    Assert-Equal -Actual ([decimal]$multiLineDraft.lines[1].price) -Expected ([decimal]42) -Message "The second VND line must not be scaled."
    Assert-Equal -Actual ([decimal]$multiLineDraft.lines[2].price) -Expected ([decimal]81918) -Message "The multi-line difference must be explicit."
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $multiLineDraft) -Expected ([decimal]82000) -Message "The multi-line draft must reconcile exactly."

    $multiLineWarningCount = @($multiLineDraft.Warnings).Count
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $multiLineDraft -Analysis $vndAnalysis
    Assert-Equal -Actual $multiLineDraft.lines.Count -Expected 3 -Message "Repeated multi-line reconciliation must replace, not duplicate, its adjustment."
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $multiLineDraft) -Expected ([decimal]82000) -Message "Repeated multi-line reconciliation must remain exact."
    Assert-Equal -Actual @($multiLineDraft.Warnings).Count -Expected $multiLineWarningCount -Message "Repeated multi-line reconciliation must keep warning state stable."

    $overTotalDraft = New-SingleLineDraft -DraftType $draftType -LineType $lineType -Price ([decimal]82000.01) -CurrencyCode "EUR"
    Invoke-TotalFallback -Method $applyTotalFallbackMethod -Draft $overTotalDraft -Analysis $vndAnalysis
    Assert-Equal -Actual $overTotalDraft.lines.Count -Expected 2 -Message "An over-total draft must add one signed adjustment."
    Assert-Equal -Actual ([decimal]$overTotalDraft.lines[1].qty) -Expected ([decimal]0) -Message "A negative adjustment must use zero quantity."
    Assert-Equal -Actual ([decimal]$overTotalDraft.lines[1].price) -Expected ([decimal]-0.01) -Message "A negative adjustment must preserve its signed difference."
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $overTotalDraft) -Expected ([decimal]82000) -Message "The signed adjustment must reconcile exactly."

    $controllerSourcePath = Join-Path $repoRoot "Controllers\CRM\CrmExpenseSheetTicketsController.cs"
    $controllerSource = [System.IO.File]::ReadAllText($controllerSourcePath)
    if (-not $controllerSource.Contains("amountMST = Math.Round(totalAmount * providerResult.Rate, 2, MidpointRounding.AwayFromZero);")) {
        throw "The production amountMST calculation changed unexpectedly."
    }
    if (-not $controllerSource.Contains("if (!amountMST.HasValue || amountMST.Value <= 0m)")) {
        throw "The strict amountMST > 0 validation changed unexpectedly."
    }
    if (-not $controllerSource.Contains("amountMST calculado debe ser mayor que cero.")) {
        throw "The strict amountMST validation message changed unexpectedly."
    }
    $sampleAmountMst = [Math]::Round(([decimal]82000 * [decimal]0.000033), 2, [MidpointRounding]::AwayFromZero)
    Assert-Equal -Actual $sampleAmountMst -Expected ([decimal]2.71) -Message "A positive VND conversion must remain positive after rounding."

    Write-Host "PASS ticket VND thousands regression"
}
finally {
    Pop-Location
}
