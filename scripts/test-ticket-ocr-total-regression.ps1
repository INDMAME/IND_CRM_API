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

# The API assembly is x86 because it interoperates with the Axapta COM connector.
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

function Get-AuthoritativeVndFlag {
    param(
        [Parameter(Mandatory = $true)]
        $Analysis
    )

    $propertyFlags = [Reflection.BindingFlags]"Instance,NonPublic,Public"
    $property = $Analysis.GetType().GetProperty("HasAuthoritativeVndTotal", $propertyFlags)
    if ($null -eq $property) {
        throw "HasAuthoritativeVndTotal was not found."
    }

    return [bool]$property.GetValue($Analysis)
}

function Get-DraftLineTotal {
    param($Line)

    if ($null -eq $Line -or $null -eq $Line.price) {
        return [decimal]0
    }

    $qty = if ($null -eq $Line.qty) { [decimal]0 } else { [decimal]$Line.qty }
    $price = [decimal]$Line.price
    if ($qty -eq 0 -and $price -lt 0) {
        return $price
    }

    return $qty * $price
}

function Get-DraftLinesTotal {
    param($Draft)

    $total = [decimal]0
    foreach ($line in $Draft.lines) {
        $total += Get-DraftLineTotal -Line $line
    }

    return $total
}

function New-DraftFromJson {
    param(
        [Parameter(Mandatory = $true)]
        [Type]$NormalizerType,
        [Parameter(Mandatory = $true)]
        [Reflection.BindingFlags]$Flags,
        [Parameter(Mandatory = $true)]
        [decimal]$Price,
        [Parameter(Mandatory = $true)]
        [decimal]$ModelTotal,
        [string]$CurrencyCode = "EUR"
    )

    $parseMethod = $NormalizerType.GetMethod("TryParseExpenseDraft", $Flags)
    if ($null -eq $parseMethod) {
        throw "TryParseExpenseDraft was not found."
    }

    [string]$payload = @{
        mode = 0
        description = "Ticket regression"
        currencyCode = $CurrencyCode
        gastoType = 8
        totalAmount = $ModelTotal
        transDate = "01.08.2026"
        ticketDate = "01.08.2026"
        ticketTime = $null
        exchRate = $null
        projId = $null
        confidence = 1
        warnings = @()
        rawCurrency = $CurrencyCode
        merchant = "Regression"
        lines = @(
            @{
                transDate = "01.08.2026"
                typeValue = 8
                description = "Base imponible"
                internacional = $false
                fileId = $null
                qty = 1
                price = $Price
                lineTotal = $Price
                projId = $null
            }
        )
    } | ConvertTo-Json -Depth 8 -Compress

    return $parseMethod.Invoke($null, [object[]]@([string]$payload))
}

function New-AzureTotalAnalysis {
    param(
        [Parameter(Mandatory = $true)]
        [Type]$AnalyzerType,
        [Parameter(Mandatory = $true)]
        [Reflection.BindingFlags]$Flags,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$TotalContent,
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [object]$StructuredAmount,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$StructuredCurrency,
        [string]$StructuredCurrencySymbol,
        [Parameter(Mandatory = $true)]
        [string]$ReceiptContent,
        [bool]$LeadingNullDocument = $false
    )

    $buildAnalysisMethod = $AnalyzerType.GetMethod("TryBuildAnalysisResult", $Flags)
    if ($null -eq $buildAnalysisMethod) {
        throw "TryBuildAnalysisResult was not found."
    }

    $valueCurrency = @{}
    if ($null -ne $StructuredAmount) {
        $valueCurrency.amount = [decimal]$StructuredAmount
    }
    if (-not [string]::IsNullOrWhiteSpace($StructuredCurrency)) {
        $valueCurrency.currencyCode = $StructuredCurrency
    }
    if (-not [string]::IsNullOrWhiteSpace($StructuredCurrencySymbol)) {
        $valueCurrency.currencySymbol = $StructuredCurrencySymbol
    }

    $totalDocument = @{
        docType = "receipt.generic"
        fields = @{
            Total = @{
                content = $TotalContent
                valueCurrency = $valueCurrency
            }
        }
    }
    [object[]]$documents = @($totalDocument)
    if ($LeadingNullDocument) {
        $documents = @($null, $totalDocument)
    }

    [string]$payload = @{
        analyzeResult = @{
            modelId = "prebuilt-receipt"
            content = $ReceiptContent
            documents = $documents
        }
    } | ConvertTo-Json -Depth 10 -Compress

    return $buildAnalysisMethod.Invoke($null, [object[]]@([string]$payload))
}

$assemblyDirectory = Split-Path -Parent $AssemblyPath
Push-Location $assemblyDirectory
try {
    $assembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
    $analyzerType = $assembly.GetType("IND_CRM_API.Services.AzureReceiptAnalyzerService", $true)
    $normalizerType = $assembly.GetType("IND_CRM_API.Services.IND_OpenAiExpenseTicketDraftService", $true)
    $analysisType = $assembly.GetType("IND_CRM_API.Services.Interfaces.AzureReceiptAnalysisResult", $true)
    $flags = [Reflection.BindingFlags]"NonPublic,Static"

    $extractMethod = $analyzerType.GetMethod("TryExtractTotalAmountFromReceiptContent", $flags)
    if ($null -eq $extractMethod) {
        throw "TryExtractTotalAmountFromReceiptContent was not found."
    }

    $buildAnalysisMethod = $analyzerType.GetMethod("TryBuildAnalysisResult", $flags)
    if ($null -eq $buildAnalysisMethod) {
        throw "TryBuildAnalysisResult was not found."
    }

    $vndSymbol = [char]0x20AB
    $vndSymbolAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "82.000 $vndSymbol" `
        -StructuredAmount 82 `
        -StructuredCurrency "EUR" `
        -ReceiptContent "TOTAL 91.000 EUR"
    Assert-Equal -Actual ([decimal]$vndSymbolAnalysis.TotalAmount) -Expected ([decimal]82000) -Message "The VND symbol in Total.content must make a grouped integer authoritative."
    Assert-Equal -Actual $vndSymbolAnalysis.CurrencyCode -Expected "VND" -Message "The VND symbol must resolve explicitly to ISO-4217 VND."
    Assert-Equal -Actual (Get-AuthoritativeVndFlag -Analysis $vndSymbolAnalysis) -Expected $true -Message "A grouped VND amount from Total.content must be marked authoritative."

    $vndCodeAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "82,000 VND" `
        -StructuredAmount 82 `
        -StructuredCurrency "USD" `
        -ReceiptContent "MERCHANT"
    Assert-Equal -Actual ([decimal]$vndCodeAnalysis.TotalAmount) -Expected ([decimal]82000) -Message "The VND code in Total.content must support comma-grouped integers."
    Assert-Equal -Actual $vndCodeAnalysis.CurrencyCode -Expected "VND" -Message "The VND code in Total.content must be authoritative over structured currency metadata."

    $splitVndAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "82.000" `
        -StructuredAmount 17 `
        -StructuredCurrency "VND" `
        -ReceiptContent "TOTAL 91.000 EUR"
    Assert-Equal -Actual ([decimal]$splitVndAnalysis.TotalAmount) -Expected ([decimal]82000) -Message "VND metadata on the Total field must qualify its grouped Total.content integer."
    Assert-Equal -Actual $splitVndAnalysis.CurrencyCode -Expected "VND" -Message "VND metadata on the Total field must take priority over conflicting receipt-wide currency evidence."
    Assert-Equal -Actual (Get-AuthoritativeVndFlag -Analysis $splitVndAnalysis) -Expected $true -Message "VND Total metadata must authorize only the grouped number from Total.content."

    $splitVndSymbolAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "82.000" `
        -StructuredAmount 91 `
        -StructuredCurrency "" `
        -StructuredCurrencySymbol $vndSymbol `
        -ReceiptContent "TOTAL 82.000"
    Assert-Equal -Actual ([decimal]$splitVndSymbolAnalysis.TotalAmount) -Expected ([decimal]82000) -Message "The VND symbol metadata on the Total field must qualify its grouped Total.content integer."
    Assert-Equal -Actual $splitVndSymbolAnalysis.RawCurrency -Expected $vndSymbol -Message "The Total field VND symbol must remain the raw currency evidence."

    $globalVndSymbolAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "82.000" `
        -StructuredAmount 82 `
        -StructuredCurrency "" `
        -ReceiptContent "TOTAL PAID`n82.000`n$vndSymbol`n`$ CASH"
    Assert-Equal -Actual ([decimal]$globalVndSymbolAnalysis.TotalAmount) -Expected ([decimal]82000) -Message "A unique receipt-level VND symbol must qualify the grouped first-document Total.content integer."
    Assert-Equal -Actual $globalVndSymbolAnalysis.CurrencyCode -Expected "VND" -Message "A unique receipt-level VND symbol must resolve explicitly to ISO-4217 VND."
    Assert-Equal -Actual $globalVndSymbolAnalysis.RawCurrency -Expected $vndSymbol -Message "The qualifying receipt-level VND symbol must remain the raw currency evidence."
    Assert-Equal -Actual (Get-AuthoritativeVndFlag -Analysis $globalVndSymbolAnalysis) -Expected $true -Message "A standalone Cash marker must not invalidate otherwise unique receipt-wide VND evidence."

    $globalVndCodeAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "82.000" `
        -StructuredAmount 82 `
        -StructuredCurrency "" `
        -ReceiptContent "TOTAL PAID 91.000 VND"
    Assert-Equal -Actual ([decimal]$globalVndCodeAnalysis.TotalAmount) -Expected ([decimal]82000) -Message "A unique receipt-level VND code may qualify the first-document Total.content integer but must not supply its number."

    $globalVndNumericDistractionAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "" `
        -StructuredAmount 82 `
        -StructuredCurrency "" `
        -ReceiptContent "TOTAL 91000 VND"
    Assert-Equal -Actual ([decimal]$globalVndNumericDistractionAnalysis.TotalAmount) -Expected ([decimal]82) -Message "Receipt-level VND evidence must never replace the numeric value projected by the first-document Total field."
    Assert-Equal -Actual (Get-AuthoritativeVndFlag -Analysis $globalVndNumericDistractionAnalysis) -Expected $false -Message "A structured Total amount without numeric Total.content must not be marked as an authoritative VND correction."

    $globalVndWithoutTotalAmountAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "" `
        -StructuredAmount $null `
        -StructuredCurrency "" `
        -ReceiptContent "TOTAL 91000 VND"
    Assert-Equal -Actual ($null -eq $globalVndWithoutTotalAmountAnalysis.TotalAmount) -Expected $true -Message "Receipt-level VND evidence must not supply a number when the first-document Total field has none."
    Assert-Equal -Actual (Get-AuthoritativeVndFlag -Analysis $globalVndWithoutTotalAmountAnalysis) -Expected $false -Message "Receipt-level VND evidence alone must not create an authoritative VND correction."

    $totalContentEurOverGlobalVndAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "12,10 EUR" `
        -StructuredAmount $null `
        -StructuredCurrency "VND" `
        -ReceiptContent "CURRENCY VND`nTOTAL 12,10 EUR"
    Assert-Equal -Actual ([decimal]$totalContentEurOverGlobalVndAnalysis.TotalAmount) -Expected ([decimal]12.10) -Message "Clear EUR evidence in Total.content must preserve the existing receipt-text total fallback."
    Assert-Equal -Actual $totalContentEurOverGlobalVndAnalysis.CurrencyCode -Expected "EUR" -Message "Total.content currency must take priority over conflicting Total metadata and receipt-wide VND evidence."

    $totalMetadataEurOverGlobalVndAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "12,10" `
        -StructuredAmount $null `
        -StructuredCurrency "EUR" `
        -ReceiptContent "CURRENCY VND`nTOTAL 12,10"
    Assert-Equal -Actual ([decimal]$totalMetadataEurOverGlobalVndAnalysis.TotalAmount) -Expected ([decimal]12.10) -Message "Clear EUR Total metadata must preserve the existing receipt-text total fallback."
    Assert-Equal -Actual $totalMetadataEurOverGlobalVndAnalysis.CurrencyCode -Expected "EUR" -Message "Total metadata currency must take priority over receipt-wide VND evidence."

    $totalContentUsdOverGlobalVndAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "12.10 USD" `
        -StructuredAmount $null `
        -StructuredCurrency "" `
        -ReceiptContent "CURRENCY VND`nTOTAL 12.10 USD"
    Assert-Equal -Actual ([decimal]$totalContentUsdOverGlobalVndAnalysis.TotalAmount) -Expected ([decimal]12.10) -Message "Clear USD evidence in Total.content must preserve the existing receipt-text total fallback."
    Assert-Equal -Actual $totalContentUsdOverGlobalVndAnalysis.CurrencyCode -Expected "USD" -Message "Total.content USD must take priority over receipt-wide VND evidence."

    $totalMetadataUsdSymbolOverGlobalVndAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "12.10" `
        -StructuredAmount $null `
        -StructuredCurrency "USD" `
        -StructuredCurrencySymbol '$' `
        -ReceiptContent "CURRENCY VND`nTOTAL 12.10"
    Assert-Equal -Actual ([decimal]$totalMetadataUsdSymbolOverGlobalVndAnalysis.TotalAmount) -Expected ([decimal]12.10) -Message "Clear USD Total metadata must preserve the existing receipt-text total fallback even with its dollar symbol."
    Assert-Equal -Actual $totalMetadataUsdSymbolOverGlobalVndAnalysis.CurrencyCode -Expected "USD" -Message "Explicit USD Total metadata must take priority over receipt-wide VND evidence."

    foreach ($blockedGlobalVndCase in @(
        @{ Name = "conflicting global currency"; Metadata = ""; ReceiptContent = "TOTAL 91000 VND EUR" },
        @{ Name = "unknown Total metadata"; Metadata = "unknown"; ReceiptContent = "TOTAL 91000 VND" },
        @{ Name = "ambiguous global dollar"; Metadata = ""; ReceiptContent = 'TOTAL 91000 VND $' }
    )) {
        $blockedGlobalVndAnalysis = New-AzureTotalAnalysis `
            -AnalyzerType $analyzerType `
            -Flags $flags `
            -TotalContent "" `
            -StructuredAmount $null `
            -StructuredCurrency $blockedGlobalVndCase.Metadata `
            -ReceiptContent $blockedGlobalVndCase.ReceiptContent
        Assert-Equal -Actual ($null -eq $blockedGlobalVndAnalysis.TotalAmount) -Expected $true -Message "$($blockedGlobalVndCase.Name) must block receipt-wide text from supplying a numeric VND total."
        Assert-Equal -Actual (Get-AuthoritativeVndFlag -Analysis $blockedGlobalVndAnalysis) -Expected $false -Message "$($blockedGlobalVndCase.Name) must not create an authoritative VND correction."
    }

    foreach ($unknownCurrencyToken in @("XXX", "xxx")) {
        $unknownTotalCurrencyAnalysis = New-AzureTotalAnalysis `
            -AnalyzerType $analyzerType `
            -Flags $flags `
            -TotalContent "82.000 $unknownCurrencyToken" `
            -StructuredAmount 82 `
            -StructuredCurrency "" `
            -ReceiptContent "CURRENCY VND"
        Assert-Equal -Actual ([decimal]$unknownTotalCurrencyAnalysis.TotalAmount) -Expected ([decimal]82) -Message "An unknown currency-like token in Total.content must block global VND qualification."
        Assert-Equal -Actual (Get-AuthoritativeVndFlag -Analysis $unknownTotalCurrencyAnalysis) -Expected $false -Message "An unknown Total.content currency must not create an authoritative VND correction."
    }

    $extractVndGroupedTotalMethod = $analyzerType.GetMethod("TryExtractUnambiguousVndGroupedTotal", $flags)
    if ($null -eq $extractVndGroupedTotalMethod) {
        throw "TryExtractUnambiguousVndGroupedTotal was not found."
    }
    foreach ($ambiguousDollarEvidence in @('TOTAL $82.000 VND', 'TOTAL 82.000 VND $', 'PAYMENT $ CARD VND')) {
        $ambiguousDollarResult = $extractVndGroupedTotalMethod.Invoke(
            $null,
            [object[]]@("82.000", $ambiguousDollarEvidence, $null))
        Assert-Equal -Actual ($null -eq $ambiguousDollarResult) -Expected $true -Message "A global dollar sign outside the Cash payment marker must block VND grouped-integer normalization."
    }

    $conflictingTotalMetadataAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "82.000" `
        -StructuredAmount 82 `
        -StructuredCurrency "VND" `
        -StructuredCurrencySymbol '$' `
        -ReceiptContent "MERCHANT"
    Assert-Equal -Actual ([decimal]$conflictingTotalMetadataAnalysis.TotalAmount) -Expected ([decimal]82) -Message "Conflicting Total currency metadata must not activate VND grouped-integer normalization."

    foreach ($concatenatedVndContent in @("82.000VND", "VND82.000")) {
        $concatenatedVndAnalysis = New-AzureTotalAnalysis `
            -AnalyzerType $analyzerType `
            -Flags $flags `
            -TotalContent $concatenatedVndContent `
            -StructuredAmount 82 `
            -StructuredCurrency "EUR" `
            -ReceiptContent "MERCHANT"
        Assert-Equal -Actual ([decimal]$concatenatedVndAnalysis.TotalAmount) -Expected ([decimal]82000) -Message "OCR-concatenated VND codes must remain explicit currency evidence."
    }

    $ignoredSecondDocumentAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "82.000 VND" `
        -StructuredAmount 82 `
        -StructuredCurrency "VND" `
        -ReceiptContent "MERCHANT" `
        -LeadingNullDocument $true
    Assert-Equal -Actual ($null -eq $ignoredSecondDocumentAnalysis.TotalAmount) -Expected $true -Message "Only analyzeResult.documents[0] may authorize the OCR total."
    Assert-Equal -Actual (Get-AuthoritativeVndFlag -Analysis $ignoredSecondDocumentAnalysis) -Expected $false -Message "A later document must never create an authoritative VND correction."

    $multiGroupVndAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "1.234.567 VND" `
        -StructuredAmount 1234.567 `
        -StructuredCurrency "VND" `
        -ReceiptContent "MERCHANT"
    Assert-Equal -Actual ([decimal]$multiGroupVndAnalysis.TotalAmount) -Expected ([decimal]1234567) -Message "VND normalization must support repeated thousands groups."

    foreach ($unchangedCase in @(
        @{ Name = "EUR"; TotalContent = "82.000 EUR"; Currency = "EUR"; ReceiptContent = "MERCHANT" },
        @{ Name = "USD"; TotalContent = "82,000 USD"; Currency = "USD"; ReceiptContent = "MERCHANT" },
        @{ Name = "EUR content with VND metadata"; TotalContent = "82.000 EUR"; Currency = "VND"; ReceiptContent = "MERCHANT" },
        @{ Name = "Total EUR metadata overrides global VND"; TotalContent = "82.000"; Currency = "EUR"; ReceiptContent = "TOTAL 82.000 VND" },
        @{ Name = "global EUR only"; TotalContent = "82.000"; Currency = ""; ReceiptContent = "TOTAL 82.000 EUR" },
        @{ Name = "global USD only"; TotalContent = "82.000"; Currency = ""; ReceiptContent = "TOTAL 82.000 USD" },
        @{ Name = "global VND with EUR"; TotalContent = "82.000"; Currency = ""; ReceiptContent = "TOTAL 82.000 VND EUR" },
        @{ Name = "global VND with USD"; TotalContent = "82.000"; Currency = ""; ReceiptContent = "TOTAL 82.000 VND USD" },
        @{ Name = "global VND with explicit US dollar symbol"; TotalContent = "82.000"; Currency = ""; ReceiptContent = 'TOTAL 82.000 VND US$' },
        @{ Name = "global VND with bare dollar"; TotalContent = "82.000"; Currency = ""; ReceiptContent = 'CURRENCY VND $' },
        @{ Name = "global VND with dollar card marker"; TotalContent = "82.000"; Currency = ""; ReceiptContent = 'PAYMENT VND $ CARD' },
        @{ Name = "global VND with unknown Total metadata"; TotalContent = "82.000"; Currency = "unknown"; ReceiptContent = "TOTAL 82.000 VND" },
        @{ Name = "ambiguous Total metadata"; TotalContent = "82.000"; Currency = "VND EUR"; ReceiptContent = "MERCHANT" },
        @{ Name = "no currency evidence"; TotalContent = "82.000"; Currency = ""; ReceiptContent = "MERCHANT" },
        @{ Name = "global VND with ambiguous amount"; TotalContent = "82.000,00"; Currency = ""; ReceiptContent = "CURRENCY VND" },
        @{ Name = "mixed separators"; TotalContent = "82.000,000 VND"; Currency = "VND"; ReceiptContent = "MERCHANT" },
        @{ Name = "competing currencies"; TotalContent = "82.000 VND EUR"; Currency = "EUR"; ReceiptContent = "MERCHANT" },
        @{ Name = "extra numeric evidence"; TotalContent = "82.000 VND 2"; Currency = "VND"; ReceiptContent = "MERCHANT" },
        @{ Name = "signed amount"; TotalContent = "-82.000 VND"; Currency = "VND"; ReceiptContent = "MERCHANT" },
        @{ Name = "spaced signed amount"; TotalContent = "- 82.000 VND"; Currency = "VND"; ReceiptContent = "MERCHANT" },
        @{ Name = "accounting amount"; TotalContent = "(82.000) VND"; Currency = "VND"; ReceiptContent = "MERCHANT" },
        @{ Name = "trailing signed amount"; TotalContent = "82.000 VND-"; Currency = "VND"; ReceiptContent = "MERCHANT" }
    )) {
        $analysisCase = New-AzureTotalAnalysis `
            -AnalyzerType $analyzerType `
            -Flags $flags `
            -TotalContent $unchangedCase.TotalContent `
            -StructuredAmount 82 `
            -StructuredCurrency $unchangedCase.Currency `
            -ReceiptContent $unchangedCase.ReceiptContent
        Assert-Equal -Actual ([decimal]$analysisCase.TotalAmount) -Expected ([decimal]82) -Message "$($unchangedCase.Name) must not activate VND grouped-integer normalization."
        Assert-Equal -Actual (Get-AuthoritativeVndFlag -Analysis $analysisCase) -Expected $false -Message "$($unchangedCase.Name) must not be marked as an authoritative VND correction."
    }

    $alreadyCorrectVndAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "82.000" `
        -StructuredAmount 82000 `
        -StructuredCurrency "" `
        -ReceiptContent "TOTAL 82.000 VND"
    Assert-Equal -Actual ([decimal]$alreadyCorrectVndAnalysis.TotalAmount) -Expected ([decimal]82000) -Message "An already-correct amount qualified by unique receipt-level VND evidence must not be multiplied again."
    Assert-Equal -Actual (Get-AuthoritativeVndFlag -Analysis $alreadyCorrectVndAnalysis) -Expected $true -Message "An already-correct grouped VND total remains authoritative and idempotent."

    $ungroupedCorrectVndAnalysis = New-AzureTotalAnalysis `
        -AnalyzerType $analyzerType `
        -Flags $flags `
        -TotalContent "82000 VND" `
        -StructuredAmount 82000 `
        -StructuredCurrency "VND" `
        -ReceiptContent "MERCHANT"
    Assert-Equal -Actual ([decimal]$ungroupedCorrectVndAnalysis.TotalAmount) -Expected ([decimal]82000) -Message "An ungrouped correct VND amount must remain unchanged."
    Assert-Equal -Actual (Get-AuthoritativeVndFlag -Analysis $ungroupedCorrectVndAnalysis) -Expected $false -Message "An ungrouped VND amount must not be marked as a grouped-integer correction."

    $receiptText = "IMPORTE BASE IMPONIBLE 10,00`nIVA 21% 2,10`nTOTAL 12,10"
    $extractedTotal = [decimal]$extractMethod.Invoke($null, [object[]]@($receiptText))
    Assert-Equal -Actual $extractedTotal -Expected ([decimal]12.10) -Message "Explicit receipt total must win over the taxable base."

    $taxReceiptText = "IMPORTE IVA 2,10`nIMPORTE 10,00`nTOTAL A PAGAR 12,10"
    $taxReceiptTotal = [decimal]$extractMethod.Invoke($null, [object[]]@($taxReceiptText))
    Assert-Equal -Actual $taxReceiptTotal -Expected ([decimal]12.10) -Message "Explicit receipt total must win over tax and generic amount labels."

    $derivedTotalsText = "TOTAL A PAGAR 12,10`nTOTAL DESCUENTOS 2,10`nTOTAL AHORRO 2,10"
    $derivedTotalsResult = [decimal]$extractMethod.Invoke($null, [object[]]@($derivedTotalsText))
    Assert-Equal -Actual $derivedTotalsResult -Expected ([decimal]12.10) -Message "Derived discount and savings totals must not replace the payable total."

    $italianInvoiceText = "IMPONIBILE 10,00`nIVA 2,10`nTOTALE DA PAGARE 12,10"
    $italianInvoiceTotal = [decimal]$extractMethod.Invoke($null, [object[]]@($italianInvoiceText))
    Assert-Equal -Actual $italianInvoiceTotal -Expected ([decimal]12.10) -Message "Italian payable-total labels must resolve the gross invoice total."

    $misclassifiedAzureJson = @{
        analyzeResult = @{
            modelId = "prebuilt-receipt"
            content = $receiptText
            documents = @(
                @{
                    docType = "receipt.generic"
                    fields = @{
                        Total = @{
                            content = "10,00 EUR"
                            valueCurrency = @{
                                amount = 10
                                currencyCode = "EUR"
                            }
                        }
                        Subtotal = @{
                            content = "10,00 EUR"
                            valueCurrency = @{
                                amount = 10
                                currencyCode = "EUR"
                            }
                        }
                        TotalTax = @{
                            content = "2,10 EUR"
                            valueCurrency = @{
                                amount = 2.10
                                currencyCode = "EUR"
                            }
                        }
                    }
                }
            )
        }
    } | ConvertTo-Json -Depth 10 -Compress
    $misclassifiedAnalysis = $buildAnalysisMethod.Invoke($null, [object[]]@([string]$misclassifiedAzureJson))
    Assert-Equal -Actual ([decimal]$misclassifiedAnalysis.TotalAmount) -Expected ([decimal]12.10) -Message "An explicit OCR total must override an Azure Total field that contains the taxable base."
    $misclassifiedPrompt = $misclassifiedAnalysis.PromptJson | ConvertFrom-Json
    Assert-Equal -Actual ([decimal]$misclassifiedPrompt.totals.total.amount) -Expected ([decimal]12.10) -Message "The normalization prompt must receive the corrected gross total."

    $brandAzurePayload = $misclassifiedAzureJson | ConvertFrom-Json
    $brandAzurePayload.analyzeResult.content = "TOTALENERGIES 1234`nTOTAL CARE 8,00"
    $brandAzurePayload.analyzeResult.documents[0].fields.Total.valueCurrency.amount = 12.10
    $brandAzureJson = $brandAzurePayload | ConvertTo-Json -Depth 10 -Compress
    $brandAnalysis = $buildAnalysisMethod.Invoke($null, [object[]]@([string]$brandAzureJson))
    Assert-Equal -Actual ([decimal]$brandAnalysis.TotalAmount) -Expected ([decimal]12.10) -Message "Merchant and product names containing total must not replace a valid structured total."

    $reconcileMethod = $normalizerType.GetMethod("ReconcileDraftTotalFromOcr", $flags)
    if ($null -eq $reconcileMethod) {
        throw "ReconcileDraftTotalFromOcr was not found."
    }

    $currencyFallbackMethod = $normalizerType.GetMethod("ApplyCurrencyFallbackFromOcr", $flags)
    if ($null -eq $currencyFallbackMethod) {
        throw "ApplyCurrencyFallbackFromOcr was not found."
    }

    $vndDraft = New-DraftFromJson -NormalizerType $normalizerType -Flags $flags -Price 82 -ModelTotal 82 -CurrencyCode "EUR"
    $currencyFallbackMethod.Invoke($null, [object[]]@($vndDraft, $globalVndSymbolAnalysis)) | Out-Null
    Assert-Equal -Actual $vndDraft.currencyCode -Expected "VND" -Message "An authoritative OCR VND total must override a conflicting model currency before reconciliation."
    $reconcileMethod.Invoke($null, [object[]]@($vndDraft, $globalVndSymbolAnalysis)) | Out-Null
    Assert-Equal -Actual ([decimal]$vndDraft.totalAmount) -Expected ([decimal]82000) -Message "The corrected VND OCR total must replace the model total."
    Assert-Equal -Actual $vndDraft.lines.Count -Expected 2 -Message "VND reconciliation must add one deterministic adjustment line."
    Assert-Equal -Actual ([decimal]$vndDraft.lines[0].price) -Expected ([decimal]82) -Message "VND reconciliation must not multiply every source line."
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $vndDraft) -Expected ([decimal]82000) -Message "VND draft lines must reconcile to the corrected OCR total."

    $vndLineCountAfterFirstReconciliation = $vndDraft.lines.Count
    $reconcileMethod.Invoke($null, [object[]]@($vndDraft, $globalVndSymbolAnalysis)) | Out-Null
    Assert-Equal -Actual $vndDraft.lines.Count -Expected $vndLineCountAfterFirstReconciliation -Message "Repeating VND reconciliation must be idempotent."
    Assert-Equal -Actual ([decimal]$vndDraft.lines[0].price) -Expected ([decimal]82) -Message "Idempotent reconciliation must preserve the original line amount."
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $vndDraft) -Expected ([decimal]82000) -Message "Idempotent VND reconciliation must preserve the corrected total."

    $controllerType = $assembly.GetType("IND_CRM_API.Controllers.CRM.CrmExpenseSheetTicketsController", $true)
    $buildUpdateRequestMethod = $controllerType.GetMethod("BuildQuickCreateUpdateRequestFromDraft", $flags)
    if ($null -eq $buildUpdateRequestMethod) {
        throw "BuildQuickCreateUpdateRequestFromDraft was not found."
    }

    $buildUpdateArguments = [object[]]@($vndDraft, "", "ticket.jpg", "jpg", "Ticket regression", "VND", "", $null, $false)
    $vndUpdateRequest = $buildUpdateRequestMethod.Invoke($null, $buildUpdateArguments)
    Assert-Equal -Actual ([decimal]$vndUpdateRequest.totalAmount) -Expected ([decimal]82000) -Message "UpdateExpenseSheetTicketFromIARequest must use the reconciled VND line total."
    Assert-Equal -Actual $vndUpdateRequest.lines.Count -Expected 2 -Message "The update request must preserve the deterministic VND reconciliation."

    $vndToleranceDraft = New-DraftFromJson -NormalizerType $normalizerType -Flags $flags -Price 81999.99 -ModelTotal 81999.99 -CurrencyCode "VND"
    $reconcileMethod.Invoke($null, [object[]]@($vndToleranceDraft, $globalVndSymbolAnalysis)) | Out-Null
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $vndToleranceDraft) -Expected ([decimal]82000) -Message "Authoritative VND reconciliation must not retain the legacy two-cent tolerance."
    $vndToleranceArguments = [object[]]@($vndToleranceDraft, "", "ticket.jpg", "jpg", "Ticket regression", "VND", "", $null, $false)
    $vndToleranceRequest = $buildUpdateRequestMethod.Invoke($null, $vndToleranceArguments)
    Assert-Equal -Actual ([decimal]$vndToleranceRequest.totalAmount) -Expected ([decimal]82000) -Message "The VND update request must exactly match the corrected OCR total."

    $profileType = $assembly.GetType("IND_CRM_API.Services.Interfaces.ExpenseTicketDraftProfile", $true)
    $quickCreateProfile = [Enum]::Parse($profileType, "QuickCreate")
    $structuredPromptMethod = $normalizerType.GetMethod("BuildStructuredOcrPayloadPromptText", $flags)
    $structuredPrompt = [string]$structuredPromptMethod.Invoke($null, [object[]]@($quickCreateProfile))
    Assert-Equal -Actual $structuredPrompt.Contains("VND") -Expected $true -Message "The OCR prompt must include the VND semantic defense."
    Assert-Equal -Actual $structuredPrompt.Contains("separadores de miles") -Expected $true -Message "The OCR prompt must explain grouped VND integers."
    Assert-Equal -Actual $structuredPrompt.Contains("no apliques esta regla a otras monedas") -Expected $true -Message "The OCR prompt must explicitly limit grouped-integer semantics to VND."

    $analysis = [Activator]::CreateInstance($analysisType)
    $analysis.TotalAmount = [decimal]121

    $mismatchedDraft = New-DraftFromJson -NormalizerType $normalizerType -Flags $flags -Price 100 -ModelTotal 999
    $reconcileMethod.Invoke($null, [object[]]@($mismatchedDraft, $analysis)) | Out-Null
    Assert-Equal -Actual ([decimal]$mismatchedDraft.totalAmount) -Expected ([decimal]121) -Message "OCR total must override the model total."
    Assert-Equal -Actual $mismatchedDraft.lines.Count -Expected 2 -Message "A mismatched valid draft must receive one adjustment line."
    Assert-Equal -Actual $mismatchedDraft.lines[1].description -Expected "AJUSTE AL TOTAL OCR" -Message "The adjustment line description is part of the regression contract."
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $mismatchedDraft) -Expected ([decimal]121) -Message "Adjusted lines must add up to the OCR total."

    $fullProfile = [Enum]::Parse($profileType, "FullDraft")
    $normalizedJsonMethod = $normalizerType.GetMethod("BuildNormalizedDraftJson", $flags)
    $normalizedJson = [string]$normalizedJsonMethod.Invoke($null, [object[]]@($mismatchedDraft, $fullProfile))
    $normalizedDraft = $normalizedJson | ConvertFrom-Json
    Assert-Equal -Actual ([decimal]$normalizedDraft.totalAmount) -Expected ([decimal]121) -Message "Normalized JSON must preserve the authoritative OCR total."

    $schemaMethod = $normalizerType.GetMethod("BuildResponseSchema", $flags)
    foreach ($profileName in @("FullDraft", "QuickCreate")) {
        $profile = [Enum]::Parse($profileType, $profileName)
        $schemaJson = [string]$schemaMethod.Invoke($null, [object[]]@($profile)).ToString()
        $schema = $schemaJson | ConvertFrom-Json
        Assert-Equal -Actual ($null -ne $schema.properties.totalAmount) -Expected $true -Message "$profileName schema must define totalAmount."
        Assert-Equal -Actual ($schema.required -contains "totalAmount") -Expected $true -Message "$profileName schema must require totalAmount."
    }

    $matchingDraft = New-DraftFromJson -NormalizerType $normalizerType -Flags $flags -Price 121 -ModelTotal 999
    $reconcileMethod.Invoke($null, [object[]]@($matchingDraft, $analysis)) | Out-Null
    Assert-Equal -Actual ([decimal]$matchingDraft.totalAmount) -Expected ([decimal]121) -Message "Matching draft must retain the OCR total."
    Assert-Equal -Actual $matchingDraft.lines.Count -Expected 1 -Message "Matching lines must not receive an adjustment."

    $mixedDraft = New-DraftFromJson -NormalizerType $normalizerType -Flags $flags -Price 90 -ModelTotal 999
    $lineType = $assembly.GetType("IND_CRM_API.Contracts.Requests.CreateExpenseSheetLineRequest", $true)
    $invalidLine = [Activator]::CreateInstance($lineType)
    $invalidLine.description = "Invalid OCR line"
    $invalidLine.qty = [decimal]1
    $invalidLine.price = $null
    $mixedDraft.lines.Add($invalidLine)
    $mixedAnalysis = [Activator]::CreateInstance($analysisType)
    $mixedAnalysis.TotalAmount = [decimal]100
    $reconcileMethod.Invoke($null, [object[]]@($mixedDraft, $mixedAnalysis)) | Out-Null
    Assert-Equal -Actual $mixedDraft.lines.Count -Expected 2 -Message "An invalid OCR line must be removed before adding the total adjustment."
    Assert-Equal -Actual $mixedDraft.lines[1].description -Expected "AJUSTE AL TOTAL OCR" -Message "The mixed draft must keep only the valid line and its adjustment."
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $mixedDraft) -Expected ([decimal]100) -Message "Mixed valid and invalid lines must still reconcile to the OCR total."

    $invalidDraft = New-DraftFromJson -NormalizerType $normalizerType -Flags $flags -Price 0 -ModelTotal 999
    $reconcileMethod.Invoke($null, [object[]]@($invalidDraft, $analysis)) | Out-Null
    Assert-Equal -Actual $invalidDraft.lines.Count -Expected 1 -Message "A draft without valid lines must receive one OCR total line."
    Assert-Equal -Actual ([decimal]$invalidDraft.lines[0].qty) -Expected ([decimal]1) -Message "The OCR total fallback must be a positive-quantity line."
    Assert-Equal -Actual ([decimal]$invalidDraft.lines[0].price) -Expected ([decimal]121) -Message "The OCR total fallback line must use the gross total."

    $overstatedDraft = New-DraftFromJson -NormalizerType $normalizerType -Flags $flags -Price 130 -ModelTotal 999
    $reconcileMethod.Invoke($null, [object[]]@($overstatedDraft, $analysis)) | Out-Null
    Assert-Equal -Actual $overstatedDraft.lines.Count -Expected 2 -Message "An overstated draft must receive one negative adjustment."
    Assert-Equal -Actual ([decimal]$overstatedDraft.lines[1].qty) -Expected ([decimal]0) -Message "A negative OCR adjustment must use zero quantity."
    Assert-Equal -Actual ([decimal]$overstatedDraft.lines[1].price) -Expected ([decimal]-9) -Message "A negative OCR adjustment must preserve its sign."
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $overstatedDraft) -Expected ([decimal]121) -Message "Negative adjustment must reconcile the lines to the OCR total."

    Write-Host "PASS ticket OCR total regression"
}
finally {
    Pop-Location
}
