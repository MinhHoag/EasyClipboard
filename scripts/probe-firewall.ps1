# Run in an Administrator PowerShell. This rule is scoped to the active Wi-Fi Direct AP.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Enable', 'Disable')][string]$Action,
    [Parameter(Mandatory)][Guid]$InterfaceId
)
$ErrorActionPreference = 'Stop'
$ruleName = 'EasyClipboard-Probe-' + $InterfaceId.ToString('D')
$ruleGroup = 'EasyClipboard development probe'
$existing = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
if ($existing -and $existing.Group -ne $ruleGroup) { throw 'A different rule already uses this name. No changes made.' }
if ($Action -eq 'Disable') {
    if ($existing) { $existing | Remove-NetFirewallRule }
    Write-Output 'The EasyClipboard probe rule has been removed.'
    return
}
$adapter = Get-NetAdapter -IncludeHidden | Where-Object { $_.InterfaceGuid -eq $InterfaceId }
if (-not $adapter -or $adapter.InterfaceDescription -notlike '*Wi-Fi Direct*' -or $adapter.Status -ne 'Up') {
    throw 'The specified interface is not an active Wi-Fi Direct adapter. No changes made.'
}
$addresses = @(Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 |
    Where-Object { $_.AddressState -eq 'Preferred' -and $_.IPAddress -notlike '169.254.*' })
if ($addresses.Count -ne 1) { throw 'Expected exactly one preferred IPv4 address. No changes made.' }
$address = $addresses[0]
$bytes = [System.Net.IPAddress]::Parse($address.IPAddress).GetAddressBytes()
$isPrivate = $bytes[0] -eq 10 -or ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or ($bytes[0] -eq 192 -and $bytes[1] -eq 168)
if (-not $isPrivate -or $address.PrefixLength -lt 16 -or $address.PrefixLength -gt 30) {
    throw 'Unexpected AP address or subnet. No changes made.'
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$program = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
if ($existing) { $existing | Remove-NetFirewallRule }
New-NetFirewallRule -Name $ruleName -DisplayName 'EasyClipboard private hotspot connection test' `
    -Group $ruleGroup -Direction Inbound -Action Allow -Protocol TCP -LocalPort 47831 `
    -LocalAddress $address.IPAddress -RemoteAddress ($address.IPAddress + '/' + $address.PrefixLength) `
    -InterfaceAlias $adapter.Name -Program $program -Profile Any -EdgeTraversalPolicy Block | Out-Null
Write-Output 'Allowed TCP 47831 only for this program, AP interface, local address, and AP subnet. The firewall remains enabled.'
Write-Output ('After testing, remove the rule using: .\scripts\probe-firewall.ps1 -Action Disable -InterfaceId ' + $InterfaceId)
