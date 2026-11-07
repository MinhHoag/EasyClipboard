$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\packages'
$project = Join-Path $projectRoot 'tests\EasyClipboard.HotspotProbe.Checks\EasyClipboard.HotspotProbe.Checks.csproj'
& $dotnetCommand build $project -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Check build failed.' }
$assembly = Join-Path $projectRoot 'tests\EasyClipboard.HotspotProbe.Checks\bin\Release\net10.0-windows10.0.22621.0\EasyClipboard.HotspotProbe.Checks.dll'
& $dotnetCommand $assembly
if ($LASTEXITCODE -ne 0) { throw 'A check failed.' }
