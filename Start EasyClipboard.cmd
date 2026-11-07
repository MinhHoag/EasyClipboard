@echo off
setlocal
cd /d "%~dp0"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_GENERATE_ASPNET_CERTIFICATE=false"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_CLI_HOME=%~dp0.tools\cli-home"
set "NUGET_PACKAGES=%~dp0.tools\packages"
set "EASYCLIP_DOTNET=dotnet"
if exist "%~dp0.tools\dotnet\dotnet.exe" (
    set "EASYCLIP_DOTNET=%~dp0.tools\dotnet\dotnet.exe"
    set "DOTNET_ROOT=%~dp0.tools\dotnet"
)
"%EASYCLIP_DOTNET%" build "%~dp0src\EasyClipboard\EasyClipboard.csproj" -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false --nologo
if errorlevel 1 (
    echo Build failed. Install the .NET 10 SDK or check the message above.
    pause
    exit /b 1
)
start "" "%~dp0src\EasyClipboard\bin\Release\net10.0-windows10.0.22621.0\EasyClipboard.exe"
