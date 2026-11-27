param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    # Where installed copies look for updates: an HTTPS folder or a GitHub repo URL.
    # Defaults to this checkout's GitHub remote, so every build can update itself.
    [uri]$UpdateFeed,
    # Upload the finished release to GitHub Releases. Needs $env:GITHUB_TOKEN; never pass a token on the command line.
    [switch]$Publish
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

if (!$UpdateFeed) {
    $remote = (& git -C $root remote get-url origin 2>$null) | Select-Object -First 1
    if ($remote -match '^(?:https://github\.com/|git@github\.com:)([^/]+)/(.+?)(?:\.git)?/?$') {
        $UpdateFeed = [uri]"https://github.com/$($Matches[1])/$($Matches[2])"
    }
}
if ($UpdateFeed -and (!$UpdateFeed.IsAbsoluteUri -or $UpdateFeed.Scheme -ne 'https' -or
    $UpdateFeed.UserInfo -or $UpdateFeed.Query -or $UpdateFeed.Fragment)) {
    throw 'Use an HTTPS update directory or GitHub repo URL without credentials, query parameters, or a fragment.'
}
$githubFeed = $UpdateFeed -and $UpdateFeed.Host -eq 'github.com'
if ($Publish -and !$githubFeed) { throw '-Publish uploads to GitHub Releases; the update feed must be a github.com repo URL.' }
if ($Publish -and !$env:GITHUB_TOKEN) { throw 'Set $env:GITHUB_TOKEN (a token with access to the repo) before using -Publish.' }

$toolPath = Join-Path $root '.tools/velopack'
$vpk = Join-Path $toolPath 'vpk.exe'
if (!(Test-Path -LiteralPath $vpk)) {
    & $dotnet tool install vpk --version 1.2.161 --tool-path $toolPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not install vpk.' }
}

$artifacts = Join-Path $root "artifacts/$Version"
if (Test-Path -LiteralPath $artifacts) {
    throw "Output already exists at $artifacts. Use a new version or archive the previous packaging output."
}
$publishDir = Join-Path $artifacts 'publish'
$release = Join-Path $artifacts 'release'
Push-Location $root
try {
    # Windows PowerShell 5.1 ignores $PSNativeCommandUseErrorActionPreference, so check exit codes explicitly.
    & $dotnet publish 'src/EasyClipboard/EasyClipboard.csproj' -c Release -r win-x64 --self-contained -o $publishDir "-p:Version=$Version" -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if ($UpdateFeed) {
        Set-Content -LiteralPath (Join-Path $publishDir 'update-source.txt') -Value $UpdateFeed.AbsoluteUri.TrimEnd('/') -Encoding ascii
        Write-Host "Update feed: $($UpdateFeed.AbsoluteUri)"
    } else {
        Write-Warning 'No update feed: installed copies of this build will not update themselves.'
    }
    # The install directory must be separate from the existing EasyClipboard data directory.
    & $vpk pack --packId EasyClipboard.Desktop --packTitle EasyClipboard --runtime win-x64 --packVersion $Version --packDir $publishDir --mainExe EasyClipboard.exe --icon (Join-Path $root 'assets/easyclipboard.ico') --shortcuts StartMenuRoot --outputDir $release
    if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed.' }
    Get-ChildItem -LiteralPath $release | Select-Object Name, Length
    if ($Publish) {
        & $vpk upload github --outputDir $release --repoUrl $UpdateFeed.AbsoluteUri.TrimEnd('/') --token $env:GITHUB_TOKEN --publish --tag "v$Version" --releaseName "EasyClipboard $Version"
        if ($LASTEXITCODE -ne 0) { throw 'Upload to GitHub Releases failed.' }
        Write-Host "Published v$Version. Installed copies will offer it on their next launch."
    }
} finally {
    Pop-Location
}
