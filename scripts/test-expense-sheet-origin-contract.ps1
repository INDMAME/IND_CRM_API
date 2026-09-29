param([string]$AssemblyPath)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path $repositoryRoot 'bin\x86\Debug\IND_CRM_API.exe'
}
$AssemblyPath = [IO.Path]::GetFullPath($AssemblyPath)
if (-not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) { throw "Missing assembly: $AssemblyPath" }

# Exercise the real mapper in the API's required architecture without creating a COM session.
if ([IntPtr]::Size -ne 4) {
    $x86PowerShell = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
    & $x86PowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -AssemblyPath $AssemblyPath
    exit $LASTEXITCODE
}

$assemblyDirectory = Split-Path -Parent $AssemblyPath
$interopPath = Join-Path $assemblyDirectory 'Interop.AxaptaCOMConnector.dll'
$null = [Reflection.Assembly]::LoadFrom($interopPath)
$apiAssembly = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$controllerType = $apiAssembly.GetType('IND_CRM_API.Controllers.CRM.CrmExpenseSheetsController', $true)
$mapper = $controllerType.GetMethod('MapExpenseSheetDetail', [Reflection.BindingFlags]'Static,NonPublic')
if ($null -eq $mapper) { throw 'Expense sheet mapper is missing.' }

Add-Type -ReferencedAssemblies $interopPath -TypeDefinition @'
using System;
using AxaptaCOMConnector;

// Supplies container values to the real mapper without invoking Axapta or changing data.
public sealed class OriginContractContainer : IAxaptaContainer
{
    private readonly object[] values;
    public OriginContractContainer(object[] values) { this.values = values; }
    public int Length() { return values.Length; }
    public object Peek(int index) { return values[index - 1]; }
    public void Delete(int index, int count) { throw new NotSupportedException(); }
    public int Find(object a, object b, object c, object d, object e, object f) { throw new NotSupportedException(); }
    public int FindEx(AxaptaParameterList args) { throw new NotSupportedException(); }
    public void Insert(int index, object a, object b, object c, object d, object e, object f) { throw new NotSupportedException(); }
    public void InsertEx(int index, AxaptaParameterList args) { throw new NotSupportedException(); }
    public void Poke(int index, object a, object b, object c, object d, object e, object f) { throw new NotSupportedException(); }
    public void PokeEx(int index, AxaptaParameterList args) { throw new NotSupportedException(); }
    public void Append(object a, object b, object c, object d, object e, object f) { throw new NotSupportedException(); }
    public void AppendEx(AxaptaParameterList args) { throw new NotSupportedException(); }
    public void DeleteAll() { throw new NotSupportedException(); }
}
'@

# Fail on shifted amounts, lost identity, or guessed origin in an older row shape.
function Assert-Equal($Actual, $Expected, [string]$Label) {
    if ($Actual -cne $Expected) { throw "$Label expected [$Expected], actual [$Actual]" }
}

function Read-Line([object[]]$Values) {
    $row = [OriginContractContainer]::new($Values)
    $lines = [OriginContractContainer]::new([object[]]@($row))
    $header = [Collections.Generic.List[string]]::new()
    $header.AddRange([string[]]@('S1','Sheet','0','','owner','USD','10.5','100.125','0','PROJECT','','09.09.2026'))
    $detail = $mapper.Invoke($null, [object[]]@($header, $lines))
    Assert-Equal $detail.HojaGastosId 'S1' 'Header identity'
    Assert-Equal $detail.Lines.Count 1 'Line count'
    return $detail.Lines[0]
}

$modern = [object[]]@('101','09.09.2026','1','Expense','0','FILE1','5.25','2','10.5','PROJECT','0','USD','10.49','100.125','10.49')
$cases = @(
    @{ Name = 'Digital origin without paper flag'; Values = $modern + @('0','1'); Expected = $false; Origin = $true },
    @{ Name = 'Manual origin with paper flag'; Values = $modern + @('1','0'); Expected = $true; Origin = $false },
    @{ Name = 'Unknown digital origin'; Values = $modern + @('1',''); Expected = $true; Origin = $null },
    @{ Name = 'Invalid digital origin enum'; Values = $modern + @('0','2'); Expected = $false; Origin = $null },
    @{ Name = 'Invalid digital origin text'; Values = $modern + @('0','true'); Expected = $false; Origin = $null },
    @{ Name = 'Legacy 16 paper flag'; Values = $modern + @('1'); Expected = $true },
    @{ Name = 'Legacy 16 no paper flag'; Values = $modern + @('0'); Expected = $false },
    @{ Name = 'Unknown paper flag'; Values = $modern + @(''); Expected = $null },
    @{ Name = 'Invalid paper enum'; Values = $modern + @('2'); Expected = $null },
    @{ Name = 'Invalid paper text'; Values = $modern + @('true'); Expected = $null },
    @{ Name = 'Legacy 15'; Values = $modern; Expected = $null },
    @{ Name = 'Legacy 14'; Values = $modern[0..13]; Expected = $null },
    @{ Name = 'Legacy 10'; Values = $modern[0..9]; Expected = $null },
    @{ Name = 'Legacy 9'; Values = $modern[0..5] + @('2','10.5','PROJECT'); Expected = $null }
)
foreach ($case in $cases) {
    $line = Read-Line $case.Values
    if ($null -eq $line.GetType().GetProperty('Ticket')) { throw 'Ticket property is missing.' }
    if ($null -eq $line.GetType().GetProperty('CreatedFromTicket')) { throw 'CreatedFromTicket property is missing.' }
    Assert-Equal $line.Ticket $case.Expected $case.Name
    Assert-Equal $line.CreatedFromTicket $case.Origin "$($case.Name) persisted origin"
    Assert-Equal $line.RecId '101' "$($case.Name) RecId"
    Assert-Equal $line.FileId 'FILE1' "$($case.Name) FileId"
    Assert-Equal $line.Qty ([decimal]2) "$($case.Name) Qty"
    Assert-Equal $line.Amount ([decimal]10.5) "$($case.Name) Amount"
    Assert-Equal $line.ProjId 'PROJECT' "$($case.Name) ProjId"
    if ($case.Values.Length -ge 10) { Assert-Equal $line.Price ([decimal]5.25) "$($case.Name) Price" }
    else { Assert-Equal $line.Price $null "$($case.Name) absent Price" }
    if ($case.Values.Length -ge 14) {
        Assert-Equal $line.AmountMST ([decimal]10.49) "$($case.Name) AmountMST"
        Assert-Equal $line.ExchRate ([decimal]100.125) "$($case.Name) ExchRate"
    }
    if ($case.Values.Length -ge 15) {
        Assert-Equal $line.ReimbursableAmount ([decimal]10.49) "$($case.Name) ReimbursableAmount"
    }
    Write-Host "PASS $($case.Name)"
}
Write-Host "Origin contract: $($cases.Count) real-mapper cases passed; no AX calls."
