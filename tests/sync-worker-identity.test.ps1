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
$fixture = Join-Path $temporaryRoot ('ind-sync-worker-identity-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    Get-ChildItem -LiteralPath $binaryDirectory -Filter '*.dll' -File | Copy-Item -Destination $fixture
    $references = @()
    foreach ($name in @('System.Web.Http', 'System.Net.Http.Formatting', 'Newtonsoft.Json',
        'System.IdentityModel.Tokens.Jwt', 'Microsoft.IdentityModel.Tokens', 'Microsoft.IdentityModel.Logging',
        'Microsoft.IdentityModel.JsonWebTokens', 'Microsoft.IdentityModel.Abstractions')) {
        $candidate = Join-Path $binaryDirectory ($name + '.dll')
        if (Test-Path -LiteralPath $candidate) { $references += '/reference:' + $candidate }
    }
    $references += @('/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Net.Http.dll',
        '/reference:System.Configuration.dll')
    # Compile the identity, JWT and authorization sources against the built dependencies; no AX, no live settings.
    $sources = @('Helpers/SyncWorkerIdentity.cs', 'Helpers/AppSettingsHelper.cs', 'Helpers/IndRequestDiagnosticsHelper.cs',
        'Services/JwtService.cs', 'Services/Interfaces/IJwtService.cs', 'App_Start/SyncWorkerAuthorizeAttribute.cs',
        'Models/Responses/INDApiResponse.cs', 'Models/Responses/INDErrorCodes.cs', 'Models/Responses/INDValidationError.cs') |
        ForEach-Object { Join-Path $repository $_ }
    $executable = Join-Path $fixture 'SyncWorkerIdentityTests.exe'
    # Web API references an older Newtonsoft identity; mirror only the installed assembly binding, never live settings.
    $jsonVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $binaryDirectory 'Newtonsoft.Json.dll')).Version.ToString()
    # Test-only JWT settings; machine variables of a configured host take precedence through AppSettingsHelper.
    [IO.File]::WriteAllText($executable + '.config', @"
<configuration>
<appSettings>
<add key="JwtSettings:Issuer" value="IND_CRM_API_TEST" />
<add key="JwtSettings:Audience" value="IND_CRM_API_TEST_USERS" />
<add key="JwtSettings:SecretKey" value="sync-worker-identity-test-secret-key-0123456789" />
<add key="JwtSettings:ExpirationMinutes" value="5" />
<add key="SyncWorker:User" value="SYNCWORKERTEST" />
</appSettings>
<runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1"><dependentAssembly>
<assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
<bindingRedirect oldVersion="0.0.0.0-$jsonVersion" newVersion="$jsonVersion" />
</dependentAssembly></assemblyBinding></runtime></configuration>
"@)
    & $compiler /nologo /target:exe /platform:x86 /langversion:7.3 /nowarn:0436 "/out:$executable" @references @sources `
        (Join-Path $PSScriptRoot 'fixtures/sync-worker-identity.cs.fixture')
    if ($LASTEXITCODE -ne 0) { throw 'Sync worker identity fixture compilation failed.' }
    & $executable
    if ($LASTEXITCODE -ne 0) { throw 'Sync worker identity tests failed.' }
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith($temporaryRoot + '\ind-sync-worker-identity-', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside the isolated sync worker test workspace.'
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
