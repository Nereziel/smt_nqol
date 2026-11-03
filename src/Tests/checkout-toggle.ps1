$ErrorActionPreference = 'Stop'

$pluginPath = Join-Path $PSScriptRoot '../Plugin.cs'
$holdPath = Join-Path $PSScriptRoot '../Features/Checkout/CheckoutHoldController.cs'
$menuPath = Join-Path $PSScriptRoot '../UI/RuntimeMenu.cs'
$plugin = Get-Content $pluginPath -Raw
$hold = Get-Content $holdPath -Raw
$menu = Get-Content $menuPath -Raw

if ($plugin -match '_checkoutHotkey\.Value\.IsPressed\(\)') {
    throw 'Checkout automation must not depend on holding the hotkey.'
}
if ($plugin -notmatch 'ToggleAutomation') {
    throw 'Checkout config must expose ToggleAutomation.'
}
if ($plugin -match 'HoldToScan') {
    throw 'Legacy HoldToScan config binding must be removed.'
}
if ($hold -notmatch 'internal\s+bool\s+Toggle\s*\(\s*\)') {
    throw 'CheckoutHoldController must expose a contextual Toggle() entry point.'
}
if ($hold -notmatch 'internal\s+bool\s+Tick\s*\(\s*float\s+interval\s*\)') {
    throw 'CheckoutHoldController Tick must run persistent toggle state without a held-key argument.'
}
if ($hold -notmatch '!Application\.isFocused') {
    throw 'Toggle automation must self-stop when the game loses focus.'
}
if ($hold -notmatch 'sqrMagnitude\s*>\s*16f') {
    throw 'Toggle automation must stop when the player moves more than 4 m from the checkout.'
}
if ($hold -notmatch '!CheckoutBridge\.CanOperate\(\)') {
    throw 'Toggle automation must stop when checkout permission/context becomes invalid.'
}

$paymentIndex = $plugin.IndexOf('TryConfirmPayment()')
$toggleIndex = $plugin.IndexOf('.Toggle()')
if ($paymentIndex -lt 0 -or $toggleIndex -lt 0 -or $paymentIndex -gt $toggleIndex) {
    throw 'Manual payment confirmation must be attempted before checkout automation toggle.'
}
if ($menu -match 'Držením tlačítka') {
    throw 'F12 menu must no longer describe hold-to-run checkout automation.'
}
if ($menu -notmatch 'toggle|zapnout automatiku|automatick') {
    throw 'F12 menu must describe the checkout toggle behavior.'
}

Write-Output 'PASS: checkout hotkey is contextual payment-first toggle automation with automatic safety stop.'
