param([string]$AssemblyPath)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path $PSScriptRoot '..\bin\x86\Release\IND_CRM_API.exe'
}
$assemblyFile = (Resolve-Path -LiteralPath $AssemblyPath).Path
$assemblyDirectory = Split-Path -Parent $assemblyFile
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$compiler = & $vswhere -latest -prerelease -products '*' -find 'MSBuild\**\Roslyn\csc.exe' |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($compiler)) { throw 'The Visual Studio Roslyn C# compiler was not found.' }
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$fixture = Join-Path $temporaryRoot ('ind-expense-delete-auth-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    # Execute the actual permission policy and legacy AX mappers without loading startup or live credentials.
    Get-ChildItem -LiteralPath $assemblyDirectory -Filter '*.dll' -File | Copy-Item -Destination $fixture
    Copy-Item -LiteralPath $assemblyFile -Destination $fixture
    $executable = Join-Path $fixture 'DeletionAuthorizationTests.exe'
    $references = @(
        '/reference:' + $assemblyFile
        '/reference:' + (Join-Path $assemblyDirectory 'Interop.AxaptaCOMConnector.dll')
        '/reference:' + (Join-Path $assemblyDirectory 'System.Web.Http.dll')
        '/reference:System.dll'
        '/reference:System.Core.dll'
        '/reference:System.Net.Http.dll'
    )
    & $compiler /nologo /target:exe /platform:x86 /langversion:7.3 "/out:$executable" @references `
        (Join-Path $PSScriptRoot 'fixtures\expense-sheet-deletion-authorization.cs.fixture')
    if ($LASTEXITCODE -ne 0) { throw "Deletion authorization test compilation failed: $LASTEXITCODE" }
    & $executable
    if ($LASTEXITCODE -ne 0) { throw "Deletion authorization behavior tests failed: $LASTEXITCODE" }
}
finally {
    # Remove only the exact isolated directory created by this test invocation.
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith($temporaryRoot + '\ind-expense-delete-auth-', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside the deletion authorization test workspace.'
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
