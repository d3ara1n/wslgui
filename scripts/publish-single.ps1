param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "WslGui.csproj"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\publish\$RuntimeIdentifier-single"
}

$resolvedOutputParent = Split-Path -Parent $OutputDirectory
if (-not (Test-Path $resolvedOutputParent)) {
    New-Item -ItemType Directory -Path $resolvedOutputParent | Out-Null
}

if (Test-Path $OutputDirectory) {
    $resolvedOutput = Resolve-Path $OutputDirectory
    if (-not $resolvedOutput.Path.StartsWith($repoRoot.Path, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean output outside repository: $resolvedOutput"
    }

    Remove-Item -LiteralPath $resolvedOutput.Path -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputDirectory | Out-Null

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $OutputDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishTrimmed=false

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Get-ChildItem -LiteralPath $OutputDirectory -File -Filter "*.pdb" | Remove-Item -Force

$files = Get-ChildItem -LiteralPath $OutputDirectory -File
$exeFiles = $files | Where-Object { $_.Extension -ieq ".exe" }

if ($exeFiles.Count -ne 1 -or $files.Count -ne 1) {
    $fileList = ($files | ForEach-Object { $_.Name }) -join ", "
    throw "Publish output is not a single executable. Files: $fileList"
}

Write-Host "Published single-file executable:"
Write-Host $exeFiles[0].FullName
Write-Host ("Size: {0:N2} MB" -f ($exeFiles[0].Length / 1MB))
