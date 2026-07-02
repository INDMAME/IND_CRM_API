[CmdletBinding()]
param(
    [ValidateSet("svg", "png", "both")]
    [string]$Format = "svg",

    [string]$Theme = "default",

    [string]$BackgroundColor = "white",

    [int]$Width = 1600,

    [int]$Height = 1000,

    [switch]$Png
)

$ErrorActionPreference = "Stop"

# Exports Mermaid sources without touching production code.
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$assetPath = Join-Path $root "assets"

New-Item -ItemType Directory -Force -Path $assetPath | Out-Null

$mmdc = Get-Command mmdc -ErrorAction SilentlyContinue
$npx = Get-Command npx -ErrorAction SilentlyContinue

if ($mmdc) {
    $mmdcPath = if ($mmdc.Source) { $mmdc.Source } else { $mmdc.Path }
    $useNpx = $false
}
elseif ($npx) {
    $npxPath = if ($npx.Source) { $npx.Source } else { $npx.Path }
    $useNpx = $true
}
else {
    throw "Mermaid CLI was not found. Install it with: npm install -g @mermaid-js/mermaid-cli"
}

if ($Format -eq "both") {
    $targets = @("svg", "png")
}
elseif ($Png -and $Format -eq "svg") {
    $targets = @("svg", "png")
}
else {
    $targets = @($Format)
}

$groups = @(
    @{
        Name = "technical"
        SourcePath = Join-Path (Join-Path $root "diagrams") "technical"
        OutputPath = Join-Path $assetPath "technical"
    },
    @{
        Name = "user"
        SourcePath = Join-Path (Join-Path $root "diagrams") "user"
        OutputPath = Join-Path $assetPath "user"
    }
)

$foundAny = $false
foreach ($group in $groups) {
    if (-not (Test-Path -LiteralPath $group.SourcePath)) {
        Write-Warning "Diagram folder not found: $($group.SourcePath)"
        continue
    }

    $sourceRoot = (Resolve-Path -LiteralPath $group.SourcePath).Path.TrimEnd('\')
    $sources = Get-ChildItem -LiteralPath $group.SourcePath -Filter "*.mmd" -Recurse | Sort-Object FullName
    if (-not $sources) {
        Write-Warning "No Mermaid source files were found in $($group.SourcePath)"
        continue
    }

    $foundAny = $true
    foreach ($source in $sources) {
        $relativeDir = $source.Directory.FullName.Substring($sourceRoot.Length).TrimStart('\')
        $outputDir = if ($relativeDir) {
            Join-Path $group.OutputPath $relativeDir
        }
        else {
            $group.OutputPath
        }
        New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

        foreach ($target in $targets) {
            $output = Join-Path $outputDir ($source.BaseName + "." + $target)
            Write-Host "Exporting [$($group.Name)] $($source.Name) -> $output"
            if ($useNpx) {
                & $npxPath `
                    -y "@mermaid-js/mermaid-cli" `
                    -i $source.FullName `
                    -o $output `
                    -t $Theme `
                    -b $BackgroundColor `
                    -w $Width `
                    -H $Height
            }
            else {
                & $mmdcPath `
                    -i $source.FullName `
                    -o $output `
                    -t $Theme `
                    -b $BackgroundColor `
                    -w $Width `
                    -H $Height
            }
        }
    }
}

if (-not $foundAny) {
    Write-Warning "No Mermaid source files were found."
    return
}
