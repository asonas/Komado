[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$IdentityName,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Publisher,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$PublisherDisplayName,
    [ValidateNotNullOrEmpty()][string]$DisplayName = 'Komado',
    [ValidatePattern('^\d+\.\d+\.\d+\.0$')][string]$Version = '1.0.0.0',
    [string]$MakeAppxPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$packageVersion = [version]$Version
if ($packageVersion.Major -eq 0) {
    throw 'Store 用 Version の先頭の桁は 1 以上にしてください。'
}
if ($packageVersion.Major -gt 65535 -or $packageVersion.Minor -gt 65535 -or $packageVersion.Build -gt 65535) {
    throw 'Version の各桁は 65535 以下にしてください。'
}

if (-not $MakeAppxPath) {
    $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $MakeAppxPath = Get-ChildItem -LiteralPath $sdkBin -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^10\.0\.\d+\.0$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64/MakeAppx.exe' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $MakeAppxPath -or -not (Test-Path -LiteralPath $MakeAppxPath -PathType Leaf)) {
    throw 'Windows SDK の MakeAppx.exe が必要です。Windows SDK を導入するか -MakeAppxPath で指定してください。'
}

$dotnetHomeBefore = $env:DOTNET_CLI_HOME
$nugetBefore = $env:NUGET_PACKAGES
$nugetHttpCacheBefore = $env:NUGET_HTTP_CACHE_PATH
try {
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot 'artifacts/dotnet'
    $env:NUGET_PACKAGES = Join-Path $projectRoot 'artifacts/nuget'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot 'artifacts/nuget-http-cache'
    $payloadDir = Join-Path $projectRoot ('artifacts/store-payload/' + [guid]::NewGuid().ToString('N'))
    & dotnet publish (Join-Path $projectRoot 'src/MangaViewer/MangaViewer.csproj') `
        -c Release -r win-x64 --self-contained true -o $payloadDir "-p:Version=$Version" --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

    [xml]$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'installer/Store.appxmanifest') -Raw
    $manifest.Package.Identity.SetAttribute('Name', $IdentityName)
    $manifest.Package.Identity.SetAttribute('Publisher', $Publisher)
    $manifest.Package.Identity.SetAttribute('Version', $Version)
    $manifest.Package.Properties.DisplayName = $DisplayName
    $manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
    $manifest.Package.Applications.Application.VisualElements.SetAttribute('DisplayName', $DisplayName)
    $manifest.Save((Join-Path $payloadDir 'AppxManifest.xml'))

    Add-Type -AssemblyName System.Drawing
    $assetsDir = Join-Path $payloadDir 'StoreAssets'
    New-Item -ItemType Directory -Path $assetsDir | Out-Null
    $source = [System.Drawing.Image]::FromFile((Join-Path $projectRoot 'src/MangaViewer/Assets/app-icon-256.png'))
    try {
        foreach ($asset in @(
            @{ Name = 'StoreLogo.png'; Size = 50 },
            @{ Name = 'Square44x44Logo.png'; Size = 44 },
            @{ Name = 'Square150x150Logo.png'; Size = 150 }
        )) {
            $bitmap = [System.Drawing.Bitmap]::new($asset.Size, $asset.Size)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($source, 0, 0, $asset.Size, $asset.Size)
                $bitmap.Save((Join-Path $assetsDir $asset.Name), [System.Drawing.Imaging.ImageFormat]::Png)
            } finally {
                $graphics.Dispose()
                $bitmap.Dispose()
            }
        }
    } finally {
        $source.Dispose()
    }

    $outputDir = Join-Path $projectRoot 'artifacts/store'
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    $packagePath = Join-Path $outputDir "Komado_${Version}_x64.msix"
    & $MakeAppxPath pack /d $payloadDir /p $packagePath /o
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed: $LASTEXITCODE" }
    Write-Output $packagePath
} finally {
    $env:DOTNET_CLI_HOME = $dotnetHomeBefore
    $env:NUGET_PACKAGES = $nugetBefore
    $env:NUGET_HTTP_CACHE_PATH = $nugetHttpCacheBefore
}
