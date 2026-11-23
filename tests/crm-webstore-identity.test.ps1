param([string]$AssemblyPath)

# Compiles the web store token, authorization and data pass-through sources with the test fixture
# against the dependencies of a built API and runs the checks. No AX, no COM, no service changes.
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
$fixture = Join-Path $temporaryRoot ('ind-crm-webstore-identity-' + [Guid]::NewGuid().ToString('N'))
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
    $sources = @('Services/CrmWebStoreTokenService.cs', 'App_Start/CrmWebStoreAuthorizeAttribute.cs',
        'Services/CrmWebStoreContainerBuilder.cs', 'Services/CrmWebStoreMessageValidator.cs', 'Services/CrmWebStoreResultReader.cs',
        'Contracts/Requests/WebStoreRequests.cs', 'Contracts/Responses/WebStoreDtos.cs',
        'Services/JwtService.cs', 'Services/Interfaces/IJwtService.cs',
        'Helpers/AppSettingsHelper.cs', 'Helpers/IndRequestDiagnosticsHelper.cs',
        'Models/Responses/INDApiResponse.cs', 'Models/Responses/INDErrorCodes.cs', 'Models/Responses/INDValidationError.cs',
        'Models/Responses/CrmWebStoreErrorCodes.cs') |
        ForEach-Object { Join-Path $repository $_ }
    $executable = Join-Path $fixture 'CrmWebStoreIdentityTests.exe'
    # Web API references an older Newtonsoft identity; mirror only the installed assembly binding.
    $jsonVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $binaryDirectory 'Newtonsoft.Json.dll')).Version.ToString()
    # Test-only legacy JWT settings; machine variables of a configured host take precedence through
    # AppSettingsHelper and are used consistently by both sides of the check. Never printed.
    [IO.File]::WriteAllText($executable + '.config', @"
<configuration>
<appSettings>
<add key="JwtSettings:Issuer" value="IND_CRM_API_TEST" />
<add key="JwtSettings:Audience" value="IND_CRM_API_TEST_USERS" />
<add key="JwtSettings:SecretKey" value="crm-webstore-identity-test-secret-key-0123456789" />
<add key="JwtSettings:ExpirationMinutes" value="5" />
</appSettings>
<runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1"><dependentAssembly>
<assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
<bindingRedirect oldVersion="0.0.0.0-$jsonVersion" newVersion="$jsonVersion" />
</dependentAssembly></assemblyBinding></runtime></configuration>
"@)
    & $compiler /nologo /target:exe /platform:x86 /langversion:7.3 /nowarn:0436,1591 "/out:$executable" @references @sources `
        (Join-Path $PSScriptRoot 'fixtures/crm-webstore-identity.cs.fixture')
    if ($LASTEXITCODE -ne 0) { throw 'Web store identity fixture compilation failed.' }
    & $executable
    if ($LASTEXITCODE -ne 0) { throw 'Web store identity tests failed.' }
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith($temporaryRoot + '\ind-crm-webstore-identity-', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside the isolated web store test workspace.'
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
