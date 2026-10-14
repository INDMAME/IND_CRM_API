param(
    [string]$AssemblyPath
)

$ErrorActionPreference = "Stop"
$canonicalScript = Join-Path $PSScriptRoot "test-ticket-ocr-total-regression.ps1"

if (-not (Test-Path -LiteralPath $canonicalScript -PathType Leaf)) {
    throw "Canonical ticket OCR regression script not found: $canonicalScript"
}

& $canonicalScript @PSBoundParameters
