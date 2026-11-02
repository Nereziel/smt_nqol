$ErrorActionPreference = 'Stop'

$throttlePath = Join-Path $PSScriptRoot '../Features/Checkout/CheckoutDiscoveryThrottle.cs'
$holdPath = Join-Path $PSScriptRoot '../Features/Checkout/CheckoutHoldController.cs'
$bridgePath = Join-Path $PSScriptRoot '../Features/Checkout/CheckoutBridge.cs'
if (-not (Test-Path $throttlePath)) { throw 'Missing Features/Checkout/CheckoutDiscoveryThrottle.cs.' }
if (-not (Test-Path $holdPath)) { throw 'Missing moved CheckoutHoldController.cs.' }
if (-not (Test-Path $bridgePath)) { throw 'Missing moved CheckoutBridge.cs.' }

$throttleSource = Get-Content $throttlePath -Raw
$holdSource = Get-Content $holdPath -Raw
$bridgeSource = Get-Content $bridgePath -Raw

$harness = @"
#nullable enable
using System;
using SMT.PriceGun200;

public static class CheckoutCacheChecks {
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run() {
        var throttle = new CheckoutDiscoveryThrottle();
        Check(throttle.ShouldDiscover(10f), "First discovery was not immediate");
        Check(!throttle.ShouldDiscover(10.49f), "Discovery was not throttled for 0.5 seconds");
        Check(throttle.ShouldDiscover(10.5f), "Discovery did not reopen at 0.5 seconds");
        Check(!throttle.ShouldDiscover(10.75f), "Second discovery window was not throttled");
        throttle.Reset();
        Check(throttle.ShouldDiscover(10.75f), "Reset did not allow immediate discovery");
    }
}
"@

Add-Type -TypeDefinition ("#nullable enable`n" + $throttleSource + "`n" + $harness)
[CheckoutCacheChecks]::Run()

if ($holdSource -notmatch 'List<Component>\s+_productCache') {
    throw 'Held checkout must keep a register-scoped product cache.'
}
if ($holdSource -notmatch 'CheckoutDiscoveryThrottle') {
    throw 'Held checkout must throttle fallback scene discovery.'
}
if ($holdSource -notmatch 'RefreshProductCache') {
    throw 'Held checkout must isolate global product discovery in RefreshProductCache.'
}
if ($bridgeSource -notmatch 'if\s*\(_pendingCheckout\s*==\s*null\)\s*\r?\n\s*return;') {
    throw 'Idle checkout refresh must return immediately.'
}
$nullAssignments = [regex]::Matches($bridgeSource, '_pendingCheckout\s*=\s*null\s*;').Count
if ($nullAssignments -ne 1) {
    throw "CancelPending must own the only _pendingCheckout=null transition; found $nullAssignments assignments."
}

Write-Output 'PASS: held checkout discovery is cached/throttled and idle pending refresh has a single reset owner.'
