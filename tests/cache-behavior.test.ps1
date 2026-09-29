param([string]$AssemblyPath)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path $PSScriptRoot '..\bin\x86\Release\IND_CRM_API.exe'
}
$assemblyFile = (Resolve-Path -LiteralPath $AssemblyPath).Path
$binaryDirectory = Split-Path -Parent $assemblyFile
$repository = Split-Path -Parent $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$compiler = & $vswhere -latest -prerelease -products '*' -find 'MSBuild\**\Roslyn\csc.exe' | Select-Object -First 1
if (-not $compiler) { throw 'The Visual Studio C# compiler was not found.' }
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$fixture = Join-Path $temporaryRoot ('ind-cache-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    Get-ChildItem -LiteralPath $binaryDirectory -Filter '*.dll' -File | Copy-Item -Destination $fixture
    Copy-Item -LiteralPath $assemblyFile -Destination $fixture
    $references = @('/reference:' + $assemblyFile)
    foreach ($name in @('Interop.AxaptaCOMConnector', 'System.Web.Http', 'Newtonsoft.Json', 'System.IdentityModel.Tokens.Jwt', 'Microsoft.IdentityModel.Tokens', 'Microsoft.IdentityModel.Logging', 'Microsoft.IdentityModel.JsonWebTokens', 'Microsoft.IdentityModel.Abstractions')) {
        $candidate = Join-Path $binaryDirectory ($name + '.dll')
        if (Test-Path -LiteralPath $candidate) { $references += '/reference:' + $candidate }
    }
    $references += @('/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Net.Http.dll', '/reference:System.Runtime.Caching.dll', '/reference:System.Xml.Linq.dll')
    # Compile production cache sources with isolated settings and a silent logger; no live configuration or COM is loaded.
    $sources = @('Helpers/UserCompanyAccessCache.cs', 'Helpers/UserContextTokenService.cs',
        'App_Start/IND_OpenAiRateLimitHandler.cs', 'Services/ExchangeRateService.cs', 'Services/EcbExchangeRateProvider.cs',
        'Services/ExchangeRateResult.cs', 'Services/ExchangeRateProviderErrorCodes.cs',
        'Services/Interfaces/IExchangeRateProvider.cs', 'Services/Interfaces/IRawExchangeRateProvider.cs', 'Services/IAxLogger.cs',
        'Services/HelpPrivacyServices.cs', 'Services/HelpKnowledgeStore.cs', 'Services/AxaptaAuthenticationCache.cs') | ForEach-Object { Join-Path $repository $_ }
    $executable = Join-Path $fixture 'CacheBehaviorTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /langversion:7.3 /nowarn:0436 "/out:$executable" @references @sources (Join-Path $PSScriptRoot 'fixtures/cache-behavior.cs.fixture')
    if ($LASTEXITCODE -ne 0) { throw 'Cache behavior fixture compilation failed.' }
    & $executable $repository
    if ($LASTEXITCODE -ne 0) { throw 'Cache behavior tests failed.' }
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith($temporaryRoot + '\ind-cache-tests-', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside the isolated cache test workspace.'
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
