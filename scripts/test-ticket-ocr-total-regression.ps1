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
        [decimal]$ModelTotal
    )

    $parseMethod = $NormalizerType.GetMethod("TryParseExpenseDraft", $Flags)
    if ($null -eq $parseMethod) {
        throw "TryParseExpenseDraft was not found."
    }

    [string]$payload = @{
        mode = 0
        description = "Ticket regression"
        currencyCode = "EUR"
        gastoType = 8
        totalAmount = $ModelTotal
        transDate = "01.08.2026"
        ticketDate = "01.08.2026"
        ticketTime = $null
        exchRate = $null
        projId = $null
        confidence = 1
        warnings = @()
        rawCurrency = "EUR"
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

    $analysis = [Activator]::CreateInstance($analysisType)
    $analysis.TotalAmount = [decimal]121

    $mismatchedDraft = New-DraftFromJson -NormalizerType $normalizerType -Flags $flags -Price 100 -ModelTotal 999
    $reconcileMethod.Invoke($null, [object[]]@($mismatchedDraft, $analysis)) | Out-Null
    Assert-Equal -Actual ([decimal]$mismatchedDraft.totalAmount) -Expected ([decimal]121) -Message "OCR total must override the model total."
    Assert-Equal -Actual $mismatchedDraft.lines.Count -Expected 2 -Message "A mismatched valid draft must receive one adjustment line."
    Assert-Equal -Actual $mismatchedDraft.lines[1].description -Expected "AJUSTE AL TOTAL OCR" -Message "The adjustment line description is part of the regression contract."
    Assert-Equal -Actual (Get-DraftLinesTotal -Draft $mismatchedDraft) -Expected ([decimal]121) -Message "Adjusted lines must add up to the OCR total."

    $profileType = $assembly.GetType("IND_CRM_API.Services.Interfaces.ExpenseTicketDraftProfile", $true)
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
