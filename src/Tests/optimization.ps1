$ErrorActionPreference = 'Stop'

$pluginSource = Get-Content (Join-Path $PSScriptRoot '../Plugin.cs') -Raw
$holdSource = Get-Content (Join-Path $PSScriptRoot '../CheckoutHoldController.cs') -Raw
$checkoutSource = Get-Content (Join-Path $PSScriptRoot '../CheckoutBridge.cs') -Raw

if ($pluginSource -match 'FindObjectsOfType<Camera>') {
    throw 'Camera fallback must use the non-obsolete FindObjectsByType API.'
}

if ($holdSource -match '\.OrderBy\s*\(' -or $holdSource -match '\.ToArray\s*\(') {
    throw 'Held checkout product selection must not sort/copy the full scene product list.'
}

if ($holdSource -match '^using System\.Linq;' -im) {
    throw 'CheckoutHoldController no longer needs LINQ after single-pass product selection.'
}

if ($checkoutSource -match 'if\s*\(_pendingCheckout\s*==\s*null\)\s*\{\s*CancelPending\(\);' ) {
    throw 'Idle checkout refresh must return immediately instead of resetting state every frame.'
}

Write-Output 'PASS: reviewed hot paths avoid obsolete camera lookup, idle checkout resets, and LINQ sorting/copying during held scanning.'
