param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [uri]$UpdateFeed
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path -Parent $PSScriptRoot
$bundledDotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
$dotnet = 'dotnet'
if (Test-Path -LiteralPath $bundledDotnet) {
    $dotnet = $bundledDotnet
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
}
$env:DOTNET_CLI_HOME = Join-Path $root '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path $root '.tools/packages'
if ($UpdateFeed -and (!$UpdateFeed.IsAbsoluteUri -or $UpdateFeed.Scheme -ne 'https' -or
    $UpdateFeed.UserInfo -or $UpdateFeed.Query -or $UpdateFeed.Fragment)) {
    throw 'Use an HTTPS update directory without credentials, query parameters, or a fragment.'
}

$toolPath = Join-Path $root '.tools/velopack'
$vpk = Join-Path $toolPath 'vpk.exe'
if (!(Test-Path -LiteralPath $vpk)) {
    & $dotnet tool install vpk --version 1.2.161 --tool-path $toolPath
}

$artifacts = Join-Path $root "artifacts/$Version"
if (Test-Path -LiteralPath $artifacts) {
    throw "Output already exists at $artifacts. Use a new version or archive the previous packaging output."
}
$publish = Join-Path $artifacts 'publish'
$release = Join-Path $artifacts 'release'
Push-Location $root
try {
    & $dotnet publish 'src/EasyClipboard/EasyClipboard.csproj' -c Release -r win-x64 --self-contained -o $publish "-p:Version=$Version" -p:DebugType=None -p:DebugSymbols=false
    if ($UpdateFeed) {
        Set-Content -LiteralPath (Join-Path $publish 'update-source.txt') -Value $UpdateFeed.AbsoluteUri -Encoding ascii
    }
    # The install directory must be separate from the existing EasyClipboard data directory.
    & $vpk pack --packId EasyClipboard.Desktop --runtime win-x64 --packVersion $Version --packDir $publish --mainExe EasyClipboard.exe --icon (Join-Path $root 'assets/easyclipboard.ico') --outputDir $release
    Get-ChildItem -LiteralPath $release | Select-Object Name, Length
} finally {
    Pop-Location
}
