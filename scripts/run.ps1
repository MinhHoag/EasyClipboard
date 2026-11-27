param([switch]$NoBuild, [switch]$StartReceiving)
$ErrorActionPreference = 'Stop'
$activation = $null
if ([Threading.EventWaitHandle]::TryOpenExisting('Local\EasyClipboard.Activate', [ref]$activation)) {
    try { [void]$activation.Set() }
    finally { $activation.Dispose() }
    return
}
$receiver = $null
if ([Threading.Mutex]::TryOpenExisting('Local\EasyClipboard.Receiver', [ref]$receiver)) {
    $receiver.Dispose()
    Write-Host 'EasyClipboard is already running. Open it from the system tray, or quit it there before rebuilding.'
    return
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\packages'
if (-not $NoBuild) {
    # Small splash while building, so a CMD launch or tray restart doesn't look like nothing happened.
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing
    $appearance = Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'EasyClipboard\appearance.txt') -ErrorAction SilentlyContinue
    $light = $appearance -eq 'Light' -or ($appearance -eq 'System' -and
        (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -ErrorAction SilentlyContinue).AppsUseLightTheme -ne 0)
    $splash = New-Object Windows.Forms.Form -Property @{
        FormBorderStyle = 'None'; StartPosition = 'CenterScreen'; Size = New-Object Drawing.Size 340, 90
        ShowInTaskbar = $false; TopMost = $true
        BackColor = if ($light) { [Drawing.Color]::FromArgb(226, 226, 226) } else { [Drawing.Color]::FromArgb(29, 29, 29) }
    }
    $splash.Controls.Add((New-Object Windows.Forms.Label -Property @{
        Text = "Starting EasyClipboard$([char]0x2026)"; Dock = 'Fill'; TextAlign = 'MiddleCenter'
        Font = New-Object Drawing.Font 'Segoe UI', 11
        ForeColor = if ($light) { [Drawing.Color]::FromArgb(25, 25, 25) } else { [Drawing.Color]::White }
    }))
    $splash.Show()
    $project = Join-Path $projectRoot 'src\EasyClipboard\EasyClipboard.csproj'
    $build = Start-Process -FilePath $dotnetCommand -NoNewWindow -PassThru -ArgumentList @(
        'build', "`"$project`"", '-c', 'Release', '-m:1', '-nodeReuse:false', '--nologo')
    $null = $build.Handle # keeps ExitCode available after exit
    while (-not $build.HasExited) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 50 }
    $splash.Close()
    if ($build.ExitCode -ne 0) {
        # A tray restart has no visible console, so say it out loud.
        [void][Windows.Forms.MessageBox]::Show('EasyClipboard did not build. Run Start EasyClipboard.cmd to see the errors.', 'EasyClipboard')
        throw 'Build failed.'
    }
}
$application = Join-Path $projectRoot 'src\EasyClipboard\bin\Release\net10.0-windows10.0.22621.0\EasyClipboard.exe'
if (Test-Path -LiteralPath $localDotnet) { $env:DOTNET_ROOT = Split-Path -Parent $localDotnet }
$env:EASYCLIPBOARD_SOURCE_ROOT = $projectRoot
if ($StartReceiving) {
    Start-Process -FilePath $application -WorkingDirectory $projectRoot -ArgumentList '--start-receiving'
} else {
    Start-Process -FilePath $application -WorkingDirectory $projectRoot
}
