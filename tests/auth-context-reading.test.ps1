param([string]$AssemblyPath, [string]$BaselineRef)

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
$fixture = Join-Path $temporaryRoot ('ind-auth-context-reading-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    Get-ChildItem -LiteralPath $binaryDirectory -Filter '*.dll' -File | Copy-Item -Destination $fixture
    Copy-Item -LiteralPath $assemblyFile -Destination $fixture
    $references = @('/reference:' + $assemblyFile)
    foreach ($name in @('Interop.AxaptaCOMConnector', 'System.Web.Http', 'Swashbuckle.Core', 'Newtonsoft.Json',
        'System.IdentityModel.Tokens.Jwt', 'Microsoft.IdentityModel.Tokens', 'Microsoft.IdentityModel.Logging',
        'Microsoft.IdentityModel.JsonWebTokens', 'Microsoft.IdentityModel.Abstractions')) {
        $candidate = Join-Path $binaryDirectory ($name + '.dll')
        if (Test-Path -LiteralPath $candidate) { $references += '/reference:' + $candidate }
    }
    $references += @('/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Net.Http.dll',
        '/reference:System.Configuration.dll')
    # Execute the current endpoint and signed cache against fake COM and isolated settings; no live service is loaded.
    $sources = @('Controllers/System/AuthController.cs', 'Helpers/UserCompanyAccessCache.cs',
        'Helpers/UserContextTokenService.cs', 'Services/Interfaces/IAxaptaSessionManager.cs', 'Services/IAxLogger.cs') |
        ForEach-Object { Join-Path $repository $_ }
    if (-not [string]::IsNullOrWhiteSpace($BaselineRef)) {
        # An explicit immutable ref proves that the same behavioral fixture detects the original regression.
        $baseline = & git -C $repository show ($BaselineRef + ':Controllers/System/AuthController.cs')
        if ($LASTEXITCODE -ne 0) { throw 'The requested baseline controller could not be read.' }
        $baselineFile = Join-Path $fixture 'BaselineAuthController.cs'
        [IO.File]::WriteAllText($baselineFile, ($baseline -join [Environment]::NewLine))
        $sources[0] = $baselineFile
    }
    $executable = Join-Path $fixture 'AuthContextReadingTests.exe'
    # Web API references an older Newtonsoft identity; mirror only the installed assembly binding, never live settings.
    $jsonVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $binaryDirectory 'Newtonsoft.Json.dll')).Version.ToString()
    [IO.File]::WriteAllText($executable + '.config', @"
<configuration><runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1"><dependentAssembly>
<assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
<bindingRedirect oldVersion="0.0.0.0-$jsonVersion" newVersion="$jsonVersion" />
</dependentAssembly></assemblyBinding></runtime></configuration>
"@)
    & $compiler /nologo /target:exe /platform:x86 /langversion:7.3 /nowarn:0436 "/out:$executable" @references @sources `
        (Join-Path $PSScriptRoot 'fixtures/auth-context-reading.cs.fixture')
    if ($LASTEXITCODE -ne 0) { throw 'Auth context reading fixture compilation failed.' }
    if ([string]::IsNullOrWhiteSpace($BaselineRef)) {
        & $executable
        if ($LASTEXITCODE -ne 0) { throw 'Auth context reading tests failed.' }
    }
    else {
        $baselineOutput = & $executable 2>&1
        if ($LASTEXITCODE -eq 0 -or ($baselineOutput -join "`n") -notmatch 'FAILED: Failure envelope') {
            throw 'The baseline did not fail at the expected transient-read response boundary.'
        }
        Write-Output 'PASS: baseline rejects the new transient-read regression test at the expected response boundary'
    }
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith($temporaryRoot + '\ind-auth-context-reading-', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside the isolated auth context test workspace.'
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
