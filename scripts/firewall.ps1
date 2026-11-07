# Run in Administrator PowerShell while EasyClipboard's selected local network is active.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Enable', 'Disable')][string]$Action,
    [Parameter(Mandatory)][Guid]$InterfaceId,
    [ValidateSet('Home', 'Hotspot', 'Tailscale')][string]$NetworkMode = 'Home',
    [ValidateSet('Http', 'Https')][string]$Transport = 'Http'
)
$ErrorActionPreference = 'Stop'
$ruleName = 'EasyClipboard-Receiver-' + $InterfaceId.ToString('D')
$ruleGroup = 'EasyClipboard prototype receiver'
$existing = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
if ($existing -and $existing.Group -ne $ruleGroup) { throw 'An unrelated rule uses this name. No changes made.' }
if ($Action -eq 'Disable') {
    if ($existing) { $existing | Remove-NetFirewallRule }
    Write-Output 'Removed the EasyClipboard receiver rule.'
    return
}
$adapter = Get-NetAdapter -IncludeHidden | Where-Object { $_.InterfaceGuid -eq $InterfaceId }
if (-not $adapter -or $adapter.Status -ne 'Up') {
    throw 'Select an active adapter using the interface ID from EasyClipboard.'
}
$isDirect = $adapter.InterfaceDescription -like '*Wi-Fi Direct*'
$isTailscale = $adapter.InterfaceDescription -like '*Tailscale*' -or $adapter.Name -like '*Tailscale*'
$profile = 'Private'
if ($NetworkMode -eq 'Tailscale') {
    if (-not $isTailscale -or $Transport -ne 'Http') { throw 'Tailscale mode requires the Tailscale interface and HTTP transport inside its encrypted tunnel.' }
    $profile = 'Any'
} elseif ($NetworkMode -eq 'Hotspot') {
    if (-not $isDirect -or $Transport -ne 'Https') { throw 'Hotspot rules require the Wi-Fi Direct interface and HTTPS transport.' }
    $profile = 'Any'
} else {
    if ($isDirect -or $isTailscale) { throw 'Select the home Wi-Fi/Ethernet interface for Home mode.' }
    $connection = Get-NetConnectionProfile -InterfaceIndex $adapter.ifIndex -ErrorAction Stop
    if (-not ($connection | Where-Object NetworkCategory -eq 'Private')) {
        throw 'The home network must be classified as Private in Windows. Change that setting only for your trusted house network; no rule was added.'
    }
}
$addresses = @(Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 |
    Where-Object { $_.AddressState -eq 'Preferred' -and $_.IPAddress -notlike '169.254.*' })
if ($addresses.Count -ne 1) { throw 'Expected one preferred private IPv4 address.' }
$address = $addresses[0]
$bytes = [System.Net.IPAddress]::Parse($address.IPAddress).GetAddressBytes()
$isPrivate = $bytes[0] -eq 10 -or ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or ($bytes[0] -eq 192 -and $bytes[1] -eq 168)
if ($NetworkMode -eq 'Tailscale') {
    if ($bytes[0] -ne 100 -or $bytes[1] -lt 64 -or $bytes[1] -gt 127) { throw 'Expected a Tailscale IPv4 address in 100.64.0.0/10. No changes made.' }
    $remoteScope = '100.64.0.0/10'
} else {
    if (-not $isPrivate -or $address.PrefixLength -lt 16 -or $address.PrefixLength -gt 30) { throw 'Unexpected local address/subnet. No changes made.' }
    $remoteScope = $address.IPAddress + '/' + $address.PrefixLength
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$application = Join-Path $projectRoot 'src\EasyClipboard\bin\Release\net10.0-windows10.0.22621.0\EasyClipboard.exe'
if (-not (Test-Path -LiteralPath $application)) { throw 'Build EasyClipboard before creating its firewall rule.' }
if ($existing) { $existing | Remove-NetFirewallRule }
$ports = if ($Transport -eq 'Http') { @(47831) } else { @(47831, 47832) }
New-NetFirewallRule -Name $ruleName -DisplayName 'EasyClipboard local receiver' -Group $ruleGroup `
    -Direction Inbound -Action Allow -Protocol TCP -LocalPort $ports `
    -LocalAddress $address.IPAddress -RemoteAddress $remoteScope `
    -InterfaceAlias $adapter.Name -Program $application -Profile $profile -EdgeTraversalPolicy Block | Out-Null
Write-Output ('Allowed ' + $Transport + ' only for EasyClipboard.exe on this interface/address/subnet and profile ' + $profile + '. Firewall remains enabled.')
