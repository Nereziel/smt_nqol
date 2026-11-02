$ErrorActionPreference = 'Stop'

$pluginPath = Join-Path $PSScriptRoot '../Plugin.cs'
$menuPath = Join-Path $PSScriptRoot '../UI/RuntimeMenu.cs'
$pluginSource = Get-Content $pluginPath -Raw
$menuSource = Get-Content $menuPath -Raw

if ($pluginSource -notmatch 'Config\.Bind\("Pricing",\s*"MarketPriceMultiplier",\s*2\.01f') {
    throw 'Default MarketPriceMultiplier must be 2.01 (201%).'
}

if ($pluginSource -match 'RoundDownStep|_roundDownStep|\bRoundDown\s*\(') {
    throw 'Configurable price rounding must be removed from Plugin.cs.'
}

if ($menuSource -match '\bRounding\b|_roundDownStep|Zaokrouhlení dolů') {
    throw 'Configurable price rounding must be removed from the runtime menu.'
}

function Extract-Method([string] $signature) {
    $start = $pluginSource.IndexOf($signature)
    if ($start -lt 0) { throw "Missing method: $signature" }
    $open = $pluginSource.IndexOf('{', $start)
    if ($open -lt 0) { throw "Missing method body: $signature" }
    $depth = 1
    $end = $open + 1
    while ($depth -gt 0 -and $end -lt $pluginSource.Length) {
        if ($pluginSource[$end] -eq '{') { $depth++ }
        if ($pluginSource[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced method body: $signature" }
    return $pluginSource.Substring($start, $end - $start)
}

$normalize = Extract-Method '    private static float NormalizePrice('
$harness = @"
using System;
public static class PricingChecks {
$normalize
    private static void Check(float actual, float expected, string reason) {
        if (Math.Abs(actual - expected) > 0.0001f) throw new Exception(reason + ": " + actual + " != " + expected);
    }
    public static void Run() {
        Check(NormalizePrice(20.099998f), 20.10f, "Float noise was not normalized to cents");
        Check(NormalizePrice(20.0799f), 20.08f, "Ordinary price was not rounded to cents");
        Check(NormalizePrice(0.0201f), 0.02f, "Small price was not rounded to cents");
    }
}
"@

Add-Type -TypeDefinition $harness
[PricingChecks]::Run()
Write-Output 'PASS: default pricing is 201%, configurable rounding is removed, and outgoing prices are normalized to cents.'
