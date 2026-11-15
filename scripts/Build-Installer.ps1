[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.2',
    [string]$CompilerPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent

if (-not $CompilerPath) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compilerCommand) {
        $CompilerPath = $compilerCommand.Source
    } else {
        $CompilerPath = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Inno Setup 6 が必要です。https://jrsoftware.org/isdl.php からインストール後、再実行してください。'
}

$dotnetHomeBefore = $env:DOTNET_CLI_HOME
$nugetBefore = $env:NUGET_PACKAGES
$nugetHttpCacheBefore = $env:NUGET_HTTP_CACHE_PATH
try {
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot 'artifacts/dotnet'
    $env:NUGET_PACKAGES = Join-Path $projectRoot 'artifacts/nuget'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot 'artifacts/nuget-http-cache'
    $publishDir = Join-Path $projectRoot ('artifacts/installer-payload/' + [guid]::NewGuid().ToString('N'))
    & dotnet publish (Join-Path $projectRoot 'src/MangaViewer/MangaViewer.csproj') `
        -c Release -r win-x64 --self-contained false -o $publishDir "-p:Version=$Version" --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

    & $CompilerPath "/DPublishDir=$publishDir" "/DAppVersion=$Version" (Join-Path $projectRoot 'installer/MangaViewer.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed: $LASTEXITCODE" }
    Write-Output (Join-Path $projectRoot 'artifacts/installer/Komado-Setup.exe')
} finally {
    $env:DOTNET_CLI_HOME = $dotnetHomeBefore
    $env:NUGET_PACKAGES = $nugetBefore
    $env:NUGET_HTTP_CACHE_PATH = $nugetHttpCacheBefore
}
