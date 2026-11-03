$ErrorActionPreference = 'Stop'

$src = Split-Path $PSScriptRoot -Parent
$required = @(
    'Core/GameBridge.cs',
    'Core/GameContext.cs',
    'Core/ReflectionMemberCache.cs',
    'Core/TypeMissCache.cs',
    'Features/PriceGun/PricingToolSelection.cs',
    'Features/PriceGun/ShelfProductSelection.cs',
    'Features/Checkout/CheckoutBridge.cs',
    'Features/Checkout/CheckoutHoldController.cs',
    'Features/Checkout/CheckoutDiscoveryThrottle.cs',
    'UI/MenuSettings.cs',
    'UI/RuntimeMenu.cs'
)
foreach ($relative in $required) {
    if (-not (Test-Path (Join-Path $src $relative))) { throw "Missing organized source file: $relative" }
}

$oldFlat = @(
    'GameContext.cs','PricingToolSelection.cs','ShelfProductSelection.cs','CheckoutBridge.cs','CheckoutColliderGeometry.cs',
    'CheckoutColliderRepair.cs','CheckoutDelay.cs','CheckoutHoldController.cs','CheckoutPayment.cs','CheckoutScanGate.cs',
    'MenuSettings.cs','RuntimeMenu.cs'
)
foreach ($name in $oldFlat) {
    if (Test-Path (Join-Path $src $name)) { throw "Old flat source file still exists: $name" }
}

$pluginSource = Get-Content (Join-Path $src 'Plugin.cs') -Raw
$bridgeSource = Get-Content (Join-Path $src 'Core/GameBridge.cs') -Raw
if ($pluginSource -match 'internal\s+sealed\s+class\s+GameBridge') { throw 'GameBridge must be split out of Plugin.cs.' }
foreach ($legacy in @('TryResolveProductId', 'TryReadProductIdDeep', 'LooksLikeProductIdMember', 'ProductIdNames')) {
    if ($bridgeSource -match [regex]::Escape($legacy)) { throw "Legacy product-ID crawler remains: $legacy" }
}

$project = Get-Content (Join-Path $src '../SMT.NQOL.csproj') -Raw
if ($project -notmatch '<Version>0\.7\.5</Version>') { throw 'Project version must be 0.7.5.' }
if ($project -notmatch 'BepInEx\.Core"\s+Version="5\.4\.21"') { throw 'BepInEx.Core must be pinned to 5.4.21.' }
if ($project -match 'BepInEx\.Core"\s+Version="5\.\*"') { throw 'Floating BepInEx package version remains.' }

Write-Output 'PASS: source layout is organized, legacy resolver is removed, and build dependencies/version are reproducible.'
