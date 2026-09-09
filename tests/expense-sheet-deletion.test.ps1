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
if ([string]::IsNullOrWhiteSpace($compiler)) {
    throw 'The Visual Studio Roslyn C# compiler was not found.'
}
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$fixture = Join-Path $temporaryRoot ('ind-expense-deletion-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    # Copy the built x86 dependencies without loading API startup or accessing live configuration.
    Get-ChildItem -LiteralPath $assemblyDirectory -Filter '*.dll' -File |
        Copy-Item -Destination $fixture
    Copy-Item -LiteralPath $assemblyFile -Destination $fixture
    $executable = Join-Path $fixture 'DeletionTests.exe'
    $references = @(
        '/reference:' + $assemblyFile
        '/reference:' + (Join-Path $assemblyDirectory 'Newtonsoft.Json.dll')
        '/reference:' + (Join-Path $assemblyDirectory 'Microsoft.Azure.Storage.Blob.dll')
        '/reference:' + (Join-Path $assemblyDirectory 'Microsoft.Azure.Storage.Common.dll')
        '/reference:System.dll'
        '/reference:System.Core.dll'
        '/reference:System.Xml.dll'
        '/reference:System.Net.Http.dll'
    )
    & $compiler /nologo /target:exe /platform:x86 /langversion:7.3 "/out:$executable" @references `
        (Join-Path $PSScriptRoot 'fixtures\expense-sheet-deletion.cs.fixture')
    if ($LASTEXITCODE -ne 0) { throw "Deletion test compilation failed: $LASTEXITCODE" }
    & $executable (Join-Path $fixture 'journals')
    if ($LASTEXITCODE -ne 0) { throw "Deletion behavior tests failed: $LASTEXITCODE" }
}
finally {
    # Delete only the exact temporary workspace created by this invocation, including its test junction.
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith($temporaryRoot + '\ind-expense-deletion-', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside the deletion test workspace.'
    }
    if (Test-Path -LiteralPath $resolvedFixture) {
        Get-ChildItem -LiteralPath $resolvedFixture -Recurse -Directory |
            Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } |
            ForEach-Object { [IO.Directory]::Delete($_.FullName, $false) }
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}
