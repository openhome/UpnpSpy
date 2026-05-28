#requires -Version 5.1
<#
    Publishes UpnpSpy self-contained for win-x64 and wraps the output in an
    Inno Setup installer. Run from anywhere; paths are resolved relative to
    the repo root.

    Examples:
        .\installer\build-installer.ps1
        .\installer\build-installer.ps1 -Version 0.2.0
        .\installer\build-installer.ps1 -SkipPublish    # re-pack only
#>
[CmdletBinding()]
param(
    [string] $Version       = '0.1.0',
    [string] $Configuration = 'Release',
    [string] $Runtime       = 'win-x64',
    [string] $IsccPath,
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'

if (-not $IsccPath) {
    # Inno Setup installs to one of these depending on per-machine vs per-user.
    $candidates = @(
        "$env:ProgramFiles\Inno Setup 6\iscc.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\iscc.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\iscc.exe"
    )
    $IsccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $IsccPath) {
        $onPath = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
        if ($onPath) { $IsccPath = $onPath }
    }
}

$repoRoot   = Resolve-Path (Join-Path $PSScriptRoot '..')
$project    = Join-Path $repoRoot 'src\UpnpSpy.App\UpnpSpy.App.csproj'
$publishDir = Join-Path $repoRoot "artifacts\publish\$Runtime"
$outputDir  = Join-Path $repoRoot 'artifacts\installer'
$issFile    = Join-Path $PSScriptRoot 'UpnpSpy.iss'

if (-not (Test-Path $IsccPath)) {
    throw "Inno Setup compiler not found at '$IsccPath'. Install Inno Setup 6 from https://jrsoftware.org/isdl.php or pass -IsccPath."
}

if (-not $SkipPublish) {
    # WindowsAppSDK self-contained mode rejects AnyCPU, so map the runtime
    # onto the matching MSBuild Platform value declared in UpnpSpy.App.csproj.
    $platform = switch ($Runtime) {
        'win-x64'   { 'x64' }
        'win-arm64' { 'ARM64' }
        default     { throw "Unsupported runtime '$Runtime'. Use win-x64 or win-arm64." }
    }
    Write-Host "==> dotnet publish ($Configuration, $Runtime, Platform=$platform, self-contained)" -ForegroundColor Cyan
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    & dotnet publish $project `
        -c $Configuration `
        -r $Runtime `
        -p:Platform=$platform `
        --self-contained true `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
}

if (-not (Test-Path (Join-Path $publishDir 'UpnpSpy.exe'))) {
    throw "Publish output is missing UpnpSpy.exe at '$publishDir'. Re-run without -SkipPublish."
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

Write-Host "==> iscc $issFile" -ForegroundColor Cyan
& $IsccPath `
    "/DAppVersion=$Version" `
    "/DPublishDir=$publishDir" `
    "/DOutputDir=$outputDir" `
    $issFile
if ($LASTEXITCODE -ne 0) { throw "iscc failed (exit $LASTEXITCODE)." }

$setupExe = Join-Path $outputDir "UpnpSpy-Setup-$Version-x64.exe"
Write-Host "==> Done: $setupExe" -ForegroundColor Green
