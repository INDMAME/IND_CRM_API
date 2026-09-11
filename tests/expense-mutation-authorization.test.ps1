param([string]$AssemblyPath, [string]$BaselineReference)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) { $AssemblyPath = Join-Path $repository 'bin/x86/Release/IND_CRM_API.exe' }
$assemblyFile = (Resolve-Path -LiteralPath $AssemblyPath).Path
$binaryDirectory = Split-Path -Parent $assemblyFile
$compiler = & (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe') -latest -prerelease -products '*' -find 'MSBuild/**/Roslyn/csc.exe' | Select-Object -First 1
if (-not $compiler) { throw 'The Visual Studio C# compiler was not found.' }
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$fixture = Join-Path $temporaryRoot ('ind-expense-mutation-auth-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    Get-ChildItem -LiteralPath $binaryDirectory -Filter '*.dll' -File | Copy-Item -Destination $fixture
    Copy-Item -LiteralPath $assemblyFile -Destination $fixture
    $references = @('/reference:' + $assemblyFile)
    Get-ChildItem -LiteralPath $fixture -Filter '*.dll' -File | ForEach-Object { $references += '/reference:' + $_.FullName }
    $references += @('/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Net.Http.dll',
        '/reference:System.Web.dll', '/reference:System.Configuration.dll', '/reference:System.Xml.Linq.dll', '/reference:System.Runtime.Caching.dll')
    # Execute current controllers, policy and mappers; only AX transport, settings and logging are replaced.
    $sources = @('Controllers/CRM/BaseCrmController.cs', 'Controllers/CRM/CrmExpenseSheetsController.cs',
        'Controllers/CRM/CrmExpenseSheetTicketsController.cs', 'Controllers/System/AuthController.cs',
        'Helpers/UserCompanyAccessCache.cs', 'Helpers/UserContextTokenService.cs', 'Helpers/CurrencyCodeHelper.cs',
        'Services/ExpenseMutationPolicy.cs', 'Services/ExpenseMutationAuthorizationService.cs',
        'Services/Interfaces/IAxaptaSessionManager.cs', 'Services/IAxLogger.cs',
        'Services/ExpenseTicketBlobStorageService.cs', 'Services/ExchangeRateService.cs',
        'Services/EcbExchangeRateProvider.cs', 'Services/FrankfurterExchangeRateProvider.cs',
        'Services/OpenErApiExchangeRateProvider.cs') | ForEach-Object { Join-Path $repository $_ }
    if (-not [string]::IsNullOrWhiteSpace($BaselineReference)) {
        # Baseline changes mapper visibility only so the same policy test can compile against old actions.
        $sources = $sources | ForEach-Object {
            $source = $_
            if ($source -match '[\\/]Controllers[\\/]CRM[\\/](BaseCrmController|CrmExpenseSheetsController|CrmExpenseSheetTicketsController)\.cs$') {
                $relative = $source.Substring($repository.Length + 1).Replace('\', '/')
                $previous = & git -C $repository show ($BaselineReference + ':' + $relative)
                if ($LASTEXITCODE -ne 0) { throw 'Could not read the requested controller baseline.' }
                $content = ($previous -join "`r`n") -replace 'private static (ExpenseSheet(?:Ticket)?DetailDto MapExpenseSheet(?:Ticket)?Detail)', 'internal static $1'
                $source = Join-Path $fixture ([IO.Path]::GetFileName($source))
                [IO.File]::WriteAllText($source, $content, [Text.UTF8Encoding]::new($false))
            }
            $source
        }
    }
    $executable = Join-Path $fixture 'ExpenseMutationAuthorizationTests.exe'
    $jsonVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $binaryDirectory 'Newtonsoft.Json.dll')).Version.ToString()
    [IO.File]::WriteAllText($executable + '.config', @"
<configuration><runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1"><dependentAssembly>
<assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
<bindingRedirect oldVersion="0.0.0.0-$jsonVersion" newVersion="$jsonVersion" />
</dependentAssembly></assemblyBinding></runtime></configuration>
"@)
    & $compiler /nologo /target:exe /platform:x86 /langversion:7.3 /nowarn:0436 "/out:$executable" @references @sources (Join-Path $PSScriptRoot 'fixtures/expense-mutation-authorization.cs.fixture')
    if ($LASTEXITCODE -ne 0) { throw 'Expense mutation authorization fixture compilation failed.' }
    & $executable
    if ($LASTEXITCODE -ne 0) { throw 'Expense mutation authorization tests failed.' }
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith($temporaryRoot + '\ind-expense-mutation-auth-', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside the isolated expense authorization test workspace.'
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
