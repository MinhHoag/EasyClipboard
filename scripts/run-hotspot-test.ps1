param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\packages'
$project = Join-Path $projectRoot 'src\EasyClipboard.HotspotProbe\EasyClipboard.HotspotProbe.csproj'
if (-not $NoBuild) {
    & $dotnetCommand build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
$assembly = Join-Path $projectRoot 'src\EasyClipboard.HotspotProbe\bin\Release\net10.0-windows10.0.22621.0\EasyClipboard.HotspotProbe.dll'
& $dotnetCommand $assembly
exit $LASTEXITCODE
