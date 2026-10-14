[CmdletBinding()]
param(
    [string]$AssemblyPath
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($AssemblyPath))
{
    # Default to the same Release artifact used by the DEV deployment workflow.
    $AssemblyPath = Join-Path $PSScriptRoot "..\bin\x86\Release\IND_CRM_API.exe"
}

# The API assembly targets x86, so reflection must run in a 32-bit process.
if ([IntPtr]::Size -ne 4)
{
    $powerShellX86 = Join-Path $env:WINDIR "SysWOW64\WindowsPowerShell\v1.0\powershell.exe"
    if (-not (Test-Path -LiteralPath $powerShellX86 -PathType Leaf))
    {
        throw "A 32-bit Windows PowerShell host is required to load the x86 API assembly."
    }

    & $powerShellX86 -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $PSCommandPath -AssemblyPath $AssemblyPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "The 32-bit currency snapshot harness failed with exit code $LASTEXITCODE."
    }

    return
}

$resolvedAssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$assemblyDirectory = Split-Path -Parent $resolvedAssemblyPath

# Resolve private API dependencies from the selected build output directory.
$assemblyResolver = [ResolveEventHandler] {
    param($sender, $eventArgs)

    $assemblyName = New-Object Reflection.AssemblyName($eventArgs.Name)
    $candidatePath = Join-Path $assemblyDirectory ($assemblyName.Name + ".dll")
    if (Test-Path -LiteralPath $candidatePath -PathType Leaf)
    {
        return [Reflection.Assembly]::LoadFrom($candidatePath)
    }

    return $null
}

[AppDomain]::CurrentDomain.add_AssemblyResolve($assemblyResolver)

try
{
    $assembly = [Reflection.Assembly]::LoadFrom($resolvedAssemblyPath)
    $controllerType = $assembly.GetType(
        "IND_CRM_API.Controllers.CRM.CrmExpenseSheetTicketsController",
        $true
    )
    $ticketType = $assembly.GetType(
        "IND_CRM_API.Contracts.Responses.ExpenseSheetTicketDetailDto",
        $true
    )
    $resolverMethod = $controllerType.GetMethod(
        "TryResolveLinkedTicketCurrencyFields",
        [Reflection.BindingFlags]"NonPublic,Static"
    )

    if ($null -eq $resolverMethod)
    {
        throw "The linked-ticket currency resolver contract was not found in the API assembly."
    }

    # Invokes the real pure resolver and returns every output value for assertions.
    function Resolve-CurrencySnapshot
    {
        param(
            [AllowNull()][string]$CurrencyCode,
            [AllowNull()][object]$TotalAmountCurrency,
            [AllowNull()][object]$TotalAmountMST,
            [AllowNull()][object]$ExchRate
        )

        $ticket = [Activator]::CreateInstance($ticketType)
        $ticket.CurrencyCode = $CurrencyCode
        if ($null -ne $TotalAmountCurrency)
        {
            $ticket.TotalAmountCurrency = [decimal]$TotalAmountCurrency
        }
        if ($null -ne $TotalAmountMST)
        {
            $ticket.TotalAmountMST = [decimal]$TotalAmountMST
        }
        if ($null -ne $ExchRate)
        {
            $ticket.ExchRate = [decimal]$ExchRate
        }

        $arguments = [object[]]@(
            $ticket,
            [decimal]0,
            $null,
            $null,
            $null,
            $null
        )
        $success = [bool]$resolverMethod.Invoke($null, $arguments)

        return [pscustomobject]@{
            Success = $success
            TotalAmountCurrency = $arguments[1]
            CurrencyCode = $arguments[2]
            AmountMST = $arguments[3]
            ExchRate = $arguments[4]
            Message = $arguments[5]
        }
    }

    # Fails the script immediately while keeping successful cases easy to audit.
    function Assert-CurrencySnapshot
    {
        param(
            [bool]$Condition,
            [string]$CaseName
        )

        if (-not $Condition)
        {
            throw "FAILED: $CaseName"
        }

        Write-Output "PASS: $CaseName"
    }

    $strictValid = Resolve-CurrencySnapshot " brl " 11.50 1.90 605.26
    Assert-CurrencySnapshot (
        $strictValid.Success -and
        $strictValid.CurrencyCode -eq "BRL" -and
        $strictValid.TotalAmountCurrency -eq [decimal]11.50 -and
        $strictValid.AmountMST -eq [decimal]1.90 -and
        $strictValid.ExchRate -eq [decimal]605.26
    ) "strict valid BRL"

    $quickCreateValid = Resolve-CurrencySnapshot "brl" 11.50 1.90 605.26
    Assert-CurrencySnapshot (
        $quickCreateValid.Success -and
        $quickCreateValid.AmountMST -eq [decimal]1.90 -and
        $quickCreateValid.ExchRate -eq [decimal]605.26
    ) "quick-create finalized BRL stays real"

    $localCurrency = Resolve-CurrencySnapshot "eur" 11.50 $null $null
    Assert-CurrencySnapshot (
        $localCurrency.Success -and
        $localCurrency.CurrencyCode -eq "EUR" -and
        $null -eq $localCurrency.AmountMST -and
        $null -eq $localCurrency.ExchRate
    ) "EUR uses optional placeholders"

    $missingAmountMST = Resolve-CurrencySnapshot "BRL" 11.50 $null 605.26
    Assert-CurrencySnapshot (-not $missingAmountMST.Success) "strict rejects missing AmountMST"

    $missingExchRate = Resolve-CurrencySnapshot "BRL" 11.50 1.90 $null
    Assert-CurrencySnapshot (-not $missingExchRate.Success) "strict rejects missing ExchRate"

    $emptyStrictCurrency = Resolve-CurrencySnapshot "  " 11.50 $null $null
    Assert-CurrencySnapshot (-not $emptyStrictCurrency.Success) "strict rejects empty currency"

    $zeroOriginalAmount = Resolve-CurrencySnapshot "BRL" 0 1.90 605.26
    Assert-CurrencySnapshot (-not $zeroOriginalAmount.Success) "strict rejects non-positive original amount"

    $negativeOriginalAmount = Resolve-CurrencySnapshot "BRL" -1 1.90 605.26
    Assert-CurrencySnapshot (
        -not $negativeOriginalAmount.Success -and
        -not [string]::IsNullOrWhiteSpace($negativeOriginalAmount.Message)
    ) "strict rejects negative original amount"

    $missingBoth = Resolve-CurrencySnapshot "BRL" 11.50 $null $null
    Assert-CurrencySnapshot (
        -not $missingBoth.Success -and
        $missingBoth.Message -match "AmountMST and ExchRate"
    ) "strict rejects a fully incomplete foreign snapshot"
}
finally
{
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($assemblyResolver)
}
