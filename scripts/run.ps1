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
if (-not $NoBuild) {
    & $dotnetCommand build (Join-Path $projectRoot 'src\EasyClipboard\EasyClipboard.csproj') -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
$application = Join-Path $projectRoot 'src\EasyClipboard\bin\Release\net10.0-windows10.0.22621.0\EasyClipboard.exe'
if (Test-Path -LiteralPath $localDotnet) { $env:DOTNET_ROOT = Split-Path -Parent $localDotnet }
Start-Process -FilePath $application -WorkingDirectory $projectRoot -WindowStyle Hidden
